using System.Text.RegularExpressions;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Migration;

/// <summary>
/// Pure planning logic (no I/O): decides which objects must be dropped, which columns altered, and
/// which objects recreated, then emits the steps as one fixed sequence of phases:
///
///   drop check -> drop default -> drop indexed-view's indexes/schema-bound view/function/computed
///   column [combined, dependency-safe order - see below] -> drop full-text index -> drop FK -> drop
///   index/PK/UQ [+ its extended properties captured for reapply] -> drop remaining computed column
///   (one with no schema-bound relationship)
///   -> alter column collation
///   -> (optional) alter database default collation
///   -> add remaining computed column [+ its extended properties] -> add index/PK/UQ [+ its extended
///   properties] -> add FK [+ its extended properties] -> add full-text index -> add indexed-view's
///   indexes/schema-bound view/function/computed column [combined, reverse of the drop order, + its
///   permissions/extended properties] -> add default [+ its extended properties] -> add check [+ its
///   extended properties]
///
/// ALTER DATABASE ... COLLATE is deliberately placed BEFORE the recreate phase, not after it: SQL
/// Server refuses to change the database's default collation while any object created without an
/// explicit COLLATE clause (computed columns, check constraints, filtered index predicates,
/// schema-bound views/functions) still implicitly depends on it - recreating those objects first would
/// immediately re-block that statement.
///
/// Sequences, synonyms and full-text catalogs never depend on a column's collation and are therefore
/// never touched - only captured for structural verification. The same is true for permissions and
/// extended properties on a table or on a merely ALTER COLUMN'd column: dropping/recreating an object
/// (a computed column, a constraint, an index, a schema-bound view/function) is the only case that
/// actually loses them (SQL Server ties them to the object's own internal id, which changes only when
/// the object itself is dropped).
///
/// A single global ordering of these phases (rather than a per-object topological sort) is sufficient
/// for most of this object scope: nothing recreated in an earlier phase ever depends on something
/// recreated in a later one (e.g. every foreign key is only added once all tables' columns/indexes
/// already exist). There are exceptions, resolved with a proper dependency-closure and topological sort
/// within their own combined phase instead:
///
///  - Schema-bound views/functions that reference each other (e.g. an indexed wrapper view built WITH
///    SCHEMABINDING on top of another schema-bound view) - see <see cref="SchemaBoundObjectReference"/>.
///  - A computed column that is referenced by a schema-bound view/function being dropped (the column
///    must survive until that view/function is gone) or whose own expression calls a schema-bound
///    function being dropped (the column must be gone before that function is) - see
///    <see cref="ComputedColumnObjectReference"/>. Only computed columns actually touching a
///    to-be-dropped schema-bound object join this combined phase; every other computed column keeps its
///    original, simpler position (dropped after regular indexes so an index built on it is gone first,
///    recreated before them so the column exists again first) - a computed column that is BOTH covered
///    by a regular index AND tied to a schema-bound object in this way is a further, unhandled
///    combination (rare in practice: SQL Server requires a persisted/indexed computed column's
///    expression to be deterministic, which a call to an ordinary scalar function usually is not).
///
/// Check/default constraints are dropped before, and recreated after, the entire combined phase above
/// (not simply grouped with the other column-level constraints): their expression can call a
/// schema-bound function, which SQL Server then refuses to drop while the constraint still references
/// it (the same class of problem as above, but for a constraint referencing a function) - and unlike a
/// computed column, a constraint is never itself the *target* a schema-bound view depends on, so moving
/// it earlier is always safe.
///
/// Plain (non-schema-bound) views/procedures/functions/triggers never block ALTER COLUMN in SQL Server
/// and are therefore never touched here - they are only captured for structural verification.
///
/// When <see cref="Build"/> is called with updateDatabaseDefaultCollation, the drop/recreate scope for
/// schema-bound objects, check constraints, computed columns and filtered indexes widens from "tied to a
/// changing column" to "every one of these in the entire database" - ALTER DATABASE ... COLLATE checks
/// dependencies database-wide, not just on the columns being altered, and there is no catalog view that
/// tells you in advance which of these implicitly depend on the database's default collation (only
/// attempting the statement does). Column-scoped indexes/checks/defaults/computed columns tied to a
/// changing column are still dropped for the usual ALTER COLUMN reason regardless of this flag.
/// </summary>
public sealed class MigrationPlanBuilder : IMigrationPlanBuilder
{
    public Domain.Migration.MigrationPlan Build(DatabaseSnapshot snapshot, SqlCollationName targetCollation, bool updateDatabaseDefaultCollation)
    {
        var changingColumnsByTable = snapshot.Tables
            .Select(t => (Table: t, Columns: t.ColumnsRequiringCollationChange(targetCollation).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .Where(x => x.Columns.Count > 0)
            .ToDictionary(x => x.Table.Ref, x => x.Columns);

        var steps = new List<MigrationStep>();
        var order = 0;

        var foreignKeysToDrop = snapshot.ForeignKeys
            .Where(fk =>
                (changingColumnsByTable.TryGetValue(fk.ParentTable, out var parentCols) && fk.Columns.Any(c => parentCols.Contains(c.ParentColumn))) ||
                (changingColumnsByTable.TryGetValue(fk.ReferencedTable, out var referencedCols) && fk.Columns.Any(c => referencedCols.Contains(c.ReferencedColumn))))
            .ToList();

        // WITH SCHEMABINDING views/functions are the only programmable objects that block ALTER COLUMN;
        // plain views/procedures/functions/triggers never block it and are left completely untouched.
        // When the database's default collation is also changing, ALTER DATABASE requires every
        // schema-bound object in the database to be gone, not just the ones tied to a changing column.
        var directSchemaBoundDrops = updateDatabaseDefaultCollation
            ? snapshot.ProgrammableObjects.Where(o => o.IsSchemaBound).Select(o => o.Ref).ToHashSet()
            : snapshot.SchemaBoundDependencies
                .Where(dep => changingColumnsByTable.TryGetValue(dep.ReferencedTable, out var cols) && cols.Contains(dep.ReferencedColumn))
                .Select(dep => dep.DependentObject)
                .ToHashSet();

        // A schema-bound object that itself references another schema-bound object being dropped (e.g.
        // an indexed wrapper view built WITH SCHEMABINDING on top of another schema-bound view) must
        // also be dropped/recreated around it - close over that chain first (computed columns never
        // trigger *discovering* an extra schema-bound object to drop, only affect ordering, so this
        // closure is schema-bound-to-schema-bound only).
        var dependentsByReferenced = snapshot.SchemaBoundObjectReferences
            .GroupBy(r => r.ReferencedObject)
            .ToDictionary(g => g.Key, g => g.Select(r => r.DependentObject).ToList());

        var schemaBoundRefsToDrop = new HashSet<ObjectRef>(directSchemaBoundDrops);
        var pending = new Queue<ObjectRef>(directSchemaBoundDrops);
        while (pending.TryDequeue(out var referenced))
            foreach (var dependent in dependentsByReferenced.GetValueOrDefault(referenced, []))
                if (schemaBoundRefsToDrop.Add(dependent))
                    pending.Enqueue(dependent);

        var viewIndexesByView = snapshot.ViewIndexes.GroupBy(vi => vi.View).ToDictionary(g => g.Key, g => g.ToList());

        var fullTextIndexesToDrop = snapshot.FullTextIndexes
            .Where(fti => changingColumnsByTable.TryGetValue(fti.Table, out var cols) && cols.Any(fti.CoversColumn))
            .ToList();

        var tablesToAlter = snapshot.Tables.Where(t => changingColumnsByTable.ContainsKey(t.Ref)).ToList();

        var indexesToDrop = new List<(TableSnapshot Table, IndexSnapshot Index)>();
        var checksToDrop = new List<(TableSnapshot Table, CheckConstraintSnapshot Check)>();
        var defaultsToDrop = new List<(TableSnapshot Table, DefaultConstraintSnapshot Default)>();
        var computedColumnsToDrop = new List<(TableSnapshot Table, ColumnSnapshot Column)>();

        // A filtered index's predicate, and a check/default constraint's or computed column's
        // expression, can all implicitly depend on the database's default collation regardless of which
        // table/column they live on - so when that is also changing, every one of them in the database
        // is in scope, not just the ones on a table with a changing column (those are still covered by
        // the per-column checks below, for the usual ALTER COLUMN reason). A default/check constraint's
        // expression can also simply call a schema-bound function that is about to be dropped for the
        // same reason, which is an independent reason it needs to be in scope database-wide too.
        foreach (var table in snapshot.Tables)
        {
            var changingColumns = changingColumnsByTable.GetValueOrDefault(table.Ref, []);

            // Computed first: an index covering one of these (below) must be dropped regardless of its
            // own filter/changing-column status, because the column underneath it is about to go.
            var droppedComputedColumnNames = table.Columns
                .Where(col => col.IsComputed && (updateDatabaseDefaultCollation || ReferencesAnyColumn(null, col.ComputedDefinition ?? "", changingColumns)))
                .Select(col => col.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            indexesToDrop.AddRange(table.Indexes
                .Where(index => changingColumns.Any(index.CoversColumn) ||
                                 (updateDatabaseDefaultCollation && index.FilterDefinition is not null) ||
                                 droppedComputedColumnNames.Any(index.CoversColumn))
                .Select(index => (table, index)));

            checksToDrop.AddRange(table.CheckConstraints
                .Where(check => updateDatabaseDefaultCollation || ReferencesAnyColumn(check.ColumnName, check.Definition, changingColumns))
                .Select(check => (table, check)));

            defaultsToDrop.AddRange(table.DefaultConstraints
                .Where(def => updateDatabaseDefaultCollation || changingColumns.Contains(def.ColumnName))
                .Select(def => (table, def)));

            computedColumnsToDrop.AddRange(table.Columns
                .Where(col => droppedComputedColumnNames.Contains(col.Name))
                .Select(col => (table, col)));
        }

        // Of all computed columns being dropped, only the ones that actually touch a to-be-dropped
        // schema-bound object (in either direction) need to join its combined ordering phase below -
        // every other computed column keeps its simpler, original position relative to regular indexes
        // (see the class doc comment for why these two concerns don't otherwise interleave).
        var computedColumnKeys = computedColumnsToDrop.ToDictionary(x => (x.Table.Ref, x.Column.Name), x => x);

        var computedColumnsCalledBySchemaBound = snapshot.SchemaBoundDependencies
            .Where(dep => schemaBoundRefsToDrop.Contains(dep.DependentObject) && computedColumnKeys.ContainsKey((dep.ReferencedTable, dep.ReferencedColumn)))
            .ToList();

        var computedColumnsCallingSchemaBound = snapshot.ComputedColumnObjectReferences
            .Where(r => computedColumnKeys.ContainsKey((r.Table, r.ColumnName)) && schemaBoundRefsToDrop.Contains(r.ReferencedObject))
            .ToList();

        var computedColumnKeysInCombinedPhase = computedColumnsCalledBySchemaBound.Select(dep => (dep.ReferencedTable, dep.ReferencedColumn))
            .Concat(computedColumnsCallingSchemaBound.Select(r => (r.Table, r.ColumnName)))
            .ToHashSet();

        var computedColumnsOutsideCombinedPhase = computedColumnsToDrop
            .Where(x => !computedColumnKeysInCombinedPhase.Contains((x.Table.Ref, x.Column.Name)))
            .ToList();

        // The combined phase's own node set and edges: schema-bound objects (from SchemaBoundObjectReferences)
        // plus the computed columns just identified above (from the two dependency sources just built).
        // "dependent -> referenced" means dependent must be dropped before, and recreated after, referenced.
        var combinedNodes = new HashSet<GraphNode>(schemaBoundRefsToDrop.Select(GraphNode.ForSchemaBoundObject));
        foreach (var key in computedColumnKeysInCombinedPhase)
            combinedNodes.Add(GraphNode.ForComputedColumn(key.Item1, key.Item2));

        var edgesByDependent = new Dictionary<GraphNode, List<GraphNode>>();
        void AddEdge(GraphNode dependent, GraphNode referenced)
        {
            if (!edgesByDependent.TryGetValue(dependent, out var list))
                edgesByDependent[dependent] = list = [];
            list.Add(referenced);
        }

        // One dependent can reference several other nodes (e.g. a function whose body calls two other
        // schema-bound functions), so all of this is one-to-many, not one-to-one.
        foreach (var r in snapshot.SchemaBoundObjectReferences)
            if (schemaBoundRefsToDrop.Contains(r.DependentObject) && schemaBoundRefsToDrop.Contains(r.ReferencedObject))
                AddEdge(GraphNode.ForSchemaBoundObject(r.DependentObject), GraphNode.ForSchemaBoundObject(r.ReferencedObject));

        foreach (var dep in computedColumnsCalledBySchemaBound)
            AddEdge(GraphNode.ForSchemaBoundObject(dep.DependentObject), GraphNode.ForComputedColumn(dep.ReferencedTable, dep.ReferencedColumn));

        foreach (var r in computedColumnsCallingSchemaBound)
            AddEdge(GraphNode.ForComputedColumn(r.Table, r.ColumnName), GraphNode.ForSchemaBoundObject(r.ReferencedObject));

        // Post-order DFS: a node is only appended once everything it references (and is also being
        // dropped) has already been appended - so this list is "referenced nodes first, dependents
        // last", i.e. exactly the RECREATE order. Reversed, it is the DROP order (dependents first).
        var combinedRecreateOrder = new List<GraphNode>();
        var visited = new HashSet<GraphNode>();

        void VisitForRecreateOrder(GraphNode node)
        {
            if (!visited.Add(node))
                return;

            foreach (var referenced in edgesByDependent.GetValueOrDefault(node, []))
                if (combinedNodes.Contains(referenced))
                    VisitForRecreateOrder(referenced);

            combinedRecreateOrder.Add(node);
        }

        foreach (var node in combinedNodes)
            VisitForRecreateOrder(node);

        var combinedDropOrder = Enumerable.Reverse(combinedRecreateOrder).ToList();

        ObjectDefinition SchemaBoundObjectFor(ObjectRef objRef) => snapshot.ProgrammableObjects.First(o => o.Ref == objRef);

        IEnumerable<ExtendedPropertySnapshot> ExtendedPropsFor(ObjectRef parentTable, DatabaseObjectKind kind, string name) =>
            snapshot.ExtendedProperties.Where(p =>
                p.ParentTable == parentTable && p.Object.Kind == kind && string.Equals(p.Object.Name, name, StringComparison.OrdinalIgnoreCase));

        // Phase 1: drop everything that would block ALTER COLUMN, in dependency-safe order.
        //
        // Check/default constraints go first, before the combined phase: their expression can call a
        // schema-bound function that is about to be dropped below, and a constraint is never itself
        // something a schema-bound view/function depends on, so this is always safe.
        foreach (var (table, check) in checksToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropCheckConstraint, $"Check-Constraint {SqlIdentifier.QuotePart(check.Name)} auf {table.Ref} entfernen", CheckConstraintScriptGenerator.GenerateDrop(table.Ref, check)));

        foreach (var (table, def) in defaultsToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropDefaultConstraint, $"Default-Constraint {SqlIdentifier.QuotePart(def.Name)} auf {table.Ref} entfernen", DefaultConstraintScriptGenerator.GenerateDrop(table.Ref, def)));

        foreach (var node in combinedDropOrder)
        {
            if (node.SchemaBoundObject is { } objRef)
            {
                var obj = SchemaBoundObjectFor(objRef);

                // An indexed view's own indexes must go before the view itself (SQL Server refuses to
                // DROP VIEW while it still has an index, exactly like a table).
                foreach (var vi in viewIndexesByView.GetValueOrDefault(obj.Ref, []))
                    steps.Add(new MigrationStep(order++, MigrationStepKind.DropIndex, $"Index {SqlIdentifier.QuotePart(vi.Index.Name)} auf indizierter View {obj.Ref} entfernen", IndexScriptGenerator.GenerateDrop(obj.Ref, vi.Index)));

                steps.Add(new MigrationStep(order++, MigrationStepKind.DropSchemaBoundObject, $"Schema-gebundenes Objekt {obj.Ref} entfernen", RawDefinitionScriptGenerator.GenerateDrop(obj)));
            }
            else if (node.ComputedColumn is { } key)
            {
                var (table, column) = computedColumnKeys[key];
                steps.Add(new MigrationStep(order++, MigrationStepKind.DropComputedColumn, $"Berechnete Spalte {SqlIdentifier.QuotePart(column.Name)} auf {table.Ref} entfernen", ComputedColumnScriptGenerator.GenerateDrop(table.Ref, column)));
            }
        }

        // Full-text indexes are dropped before regular indexes: a full-text index depends on its
        // KEY INDEX (a regular unique index) staying present while it exists, so that key index -
        // even if it is itself among indexesToDrop - must never be dropped first.
        foreach (var fti in fullTextIndexesToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropFullTextIndex, $"Volltextindex auf {fti.Table} entfernen", FullTextIndexScriptGenerator.GenerateDrop(fti)));

        foreach (var fk in foreignKeysToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropForeignKey, $"Foreign Key {SqlIdentifier.QuotePart(fk.Name)} entfernen", ForeignKeyScriptGenerator.GenerateDrop(fk)));

        foreach (var (table, index) in indexesToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropIndex, $"Index/Constraint {SqlIdentifier.QuotePart(index.Name)} auf {table.Ref} entfernen", IndexScriptGenerator.GenerateDrop(table.Ref, index)));

        // A computed column with no schema-bound relationship still must go after any index built on
        // it - dropped last here, for exactly that reason.
        foreach (var (table, column) in computedColumnsOutsideCombinedPhase)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropComputedColumn, $"Berechnete Spalte {SqlIdentifier.QuotePart(column.Name)} auf {table.Ref} entfernen", ComputedColumnScriptGenerator.GenerateDrop(table.Ref, column)));

        // Phase 2: the actual collation change.
        foreach (var table in tablesToAlter)
        {
            var changingColumns = changingColumnsByTable[table.Ref];
            foreach (var column in table.Columns.Where(c => changingColumns.Contains(c.Name)))
                steps.Add(new MigrationStep(order++, MigrationStepKind.AlterColumnCollation, $"Collation von {table.Ref}.{SqlIdentifier.QuotePart(column.Name)} ändern", AlterColumnScriptGenerator.Generate(table.Ref, column, targetCollation)));
        }

        if (updateDatabaseDefaultCollation)
            steps.Add(new MigrationStep(order++, MigrationStepKind.AlterDatabaseCollation, "Datenbank-Default-Collation ändern", $"ALTER DATABASE CURRENT COLLATE {targetCollation.Value};"));

        // Phase 3: recreate everything dropped in phase 1, in dependency-safe order, reapplying any
        // extended properties/permissions that were attached to an object that got fully recreated.
        //
        // A computed column with no schema-bound relationship is recreated first here (mirroring where
        // it was dropped last), so any index built on it already has the column to refer to.
        foreach (var (table, column) in computedColumnsOutsideCombinedPhase)
        {
            steps.Add(new MigrationStep(order++, MigrationStepKind.AddComputedColumn, $"Berechnete Spalte {SqlIdentifier.QuotePart(column.Name)} auf {table.Ref} wiederherstellen", ComputedColumnScriptGenerator.GenerateCreate(table.Ref, column)));

            foreach (var prop in snapshot.ExtendedProperties.Where(p => p.Object == table.Ref && p.ColumnName == column.Name))
                steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {table.Ref}.{SqlIdentifier.QuotePart(column.Name)} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));
        }

        foreach (var (table, index) in indexesToDrop
                     .OrderByDescending(x => x.Index.IsPrimaryKey)
                     .ThenByDescending(x => x.Index.IsUniqueConstraint)
                     .ThenByDescending(x => x.Index.IsClustered))
        {
            steps.Add(new MigrationStep(order++, MigrationStepKind.CreateIndex, $"Index/Constraint {SqlIdentifier.QuotePart(index.Name)} auf {table.Ref} wiederherstellen", IndexScriptGenerator.GenerateCreate(table.Ref, index)));

            var kind = index.IsPrimaryKey ? DatabaseObjectKind.PrimaryKey : index.IsUniqueConstraint ? DatabaseObjectKind.UniqueConstraint : DatabaseObjectKind.Index;
            foreach (var prop in ExtendedPropsFor(table.Ref, kind, index.Name))
                steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {SqlIdentifier.QuotePart(index.Name)} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));
        }

        foreach (var fk in foreignKeysToDrop)
        {
            steps.Add(new MigrationStep(order++, MigrationStepKind.AddForeignKey, $"Foreign Key {SqlIdentifier.QuotePart(fk.Name)} wiederherstellen", ForeignKeyScriptGenerator.GenerateCreate(fk)));

            foreach (var prop in ExtendedPropsFor(fk.ParentTable, DatabaseObjectKind.ForeignKey, fk.Name))
                steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {SqlIdentifier.QuotePart(fk.Name)} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));
        }

        // Recreated after regular indexes so its KEY INDEX already exists again.
        foreach (var fti in fullTextIndexesToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.AddFullTextIndex, $"Volltextindex auf {fti.Table} wiederherstellen", FullTextIndexScriptGenerator.GenerateCreate(fti)));

        // Dropping and recreating a schema-bound view/function as a whole object loses any permissions
        // and extended properties scoped to it (or to its columns, or - for an indexed view - its
        // indexes) - all must be replayed afterwards. Recreated in reverse of the drop order, so a node
        // that another dropped node references (e.g. the base view under an indexed wrapper view, or a
        // function a computed column calls) exists again before its dependent is recreated.
        foreach (var node in combinedRecreateOrder)
        {
            if (node.SchemaBoundObject is { } objRef)
            {
                var obj = SchemaBoundObjectFor(objRef);

                steps.Add(new MigrationStep(order++, MigrationStepKind.AddSchemaBoundObject, $"Schema-gebundenes Objekt {obj.Ref} wiederherstellen", RawDefinitionScriptGenerator.GenerateCreate(obj)));

                foreach (var perm in snapshot.Permissions.Where(p => p.OnObject == obj.Ref))
                    steps.Add(new MigrationStep(order++, MigrationStepKind.GrantPermission, $"Berechtigung {perm.PermissionName} für {SqlIdentifier.QuotePart(perm.GranteePrincipal)} auf {obj.Ref} wiederherstellen", PermissionScriptGenerator.Generate(perm)));

                foreach (var prop in snapshot.ExtendedProperties.Where(p => p.Object == obj.Ref))
                    steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {obj.Ref} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));

                foreach (var vi in viewIndexesByView.GetValueOrDefault(obj.Ref, []))
                {
                    steps.Add(new MigrationStep(order++, MigrationStepKind.CreateIndex, $"Index {SqlIdentifier.QuotePart(vi.Index.Name)} auf indizierter View {obj.Ref} wiederherstellen", IndexScriptGenerator.GenerateCreate(obj.Ref, vi.Index)));

                    foreach (var prop in ExtendedPropsFor(obj.Ref, DatabaseObjectKind.Index, vi.Index.Name))
                        steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {SqlIdentifier.QuotePart(vi.Index.Name)} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));
                }
            }
            else if (node.ComputedColumn is { } key)
            {
                var (table, column) = computedColumnKeys[key];
                steps.Add(new MigrationStep(order++, MigrationStepKind.AddComputedColumn, $"Berechnete Spalte {SqlIdentifier.QuotePart(column.Name)} auf {table.Ref} wiederherstellen", ComputedColumnScriptGenerator.GenerateCreate(table.Ref, column)));

                foreach (var prop in snapshot.ExtendedProperties.Where(p => p.Object == table.Ref && p.ColumnName == column.Name))
                    steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {table.Ref}.{SqlIdentifier.QuotePart(column.Name)} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));
            }
        }

        // Recreated last, after the combined phase: a check/default constraint's expression can call a
        // schema-bound function, which must exist again before the constraint can be recreated.
        foreach (var (table, def) in defaultsToDrop)
        {
            steps.Add(new MigrationStep(order++, MigrationStepKind.AddDefaultConstraint, $"Default-Constraint {SqlIdentifier.QuotePart(def.Name)} auf {table.Ref} wiederherstellen", DefaultConstraintScriptGenerator.GenerateCreate(table.Ref, def)));

            foreach (var prop in ExtendedPropsFor(table.Ref, DatabaseObjectKind.DefaultConstraint, def.Name))
                steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {SqlIdentifier.QuotePart(def.Name)} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));
        }

        foreach (var (table, check) in checksToDrop)
        {
            steps.Add(new MigrationStep(order++, MigrationStepKind.AddCheckConstraint, $"Check-Constraint {SqlIdentifier.QuotePart(check.Name)} auf {table.Ref} wiederherstellen", CheckConstraintScriptGenerator.GenerateCreate(table.Ref, check)));

            foreach (var prop in ExtendedPropsFor(table.Ref, DatabaseObjectKind.CheckConstraint, check.Name))
                steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {SqlIdentifier.QuotePart(check.Name)} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));
        }

        return new Domain.Migration.MigrationPlan(
            snapshot.DatabaseCollation, targetCollation, updateDatabaseDefaultCollation, snapshot, steps,
            tablesToAlter.Select(t => t.Ref).ToList());
    }

    /// <summary>
    /// True if a column-level constraint/computed-column is attributed directly to one of the changing
    /// columns, or (for table-level, multi-column expressions) its definition text mentions one of them.
    /// The text match is a conservative heuristic (word-boundary / bracketed match) that may over-drop
    /// in rare cases (e.g. a column name that also appears as a string literal) but never under-drops in
    /// a way that would corrupt data - a missed drop simply makes SQL Server reject the ALTER COLUMN and
    /// the whole migration cleanly rolls back.
    /// </summary>
    private static bool ReferencesAnyColumn(string? attributedColumn, string definition, HashSet<string> changingColumns)
    {
        if (attributedColumn is not null)
            return changingColumns.Contains(attributedColumn);

        return changingColumns.Any(column =>
            Regex.IsMatch(definition, $@"\[{Regex.Escape(column)}\]|\b{Regex.Escape(column)}\b", RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// One node in the combined drop/recreate ordering graph: either a schema-bound view/function, or a
    /// computed column identified by its table and column name. Exactly one of <see cref="SchemaBoundObject"/>
    /// / <see cref="ComputedColumn"/> is non-null for any given instance.
    /// </summary>
    private readonly record struct GraphNode
    {
        private readonly ObjectRef? _computedColumnTable;
        private readonly string? _computedColumnName;

        private GraphNode(ObjectRef? schemaBoundObject, ObjectRef? computedColumnTable, string? computedColumnName)
        {
            SchemaBoundObject = schemaBoundObject;
            _computedColumnTable = computedColumnTable;
            _computedColumnName = computedColumnName;
        }

        public ObjectRef? SchemaBoundObject { get; }

        public (ObjectRef Table, string ColumnName)? ComputedColumn =>
            _computedColumnTable is { } table ? (table, _computedColumnName!) : null;

        public static GraphNode ForSchemaBoundObject(ObjectRef objectRef) => new(objectRef, null, null);

        public static GraphNode ForComputedColumn(ObjectRef table, string columnName) => new(null, table, columnName);
    }
}
