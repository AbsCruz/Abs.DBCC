using System.Text.RegularExpressions;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Migration;

/// <summary>
/// Pure planning logic (no I/O): decides which objects must be dropped, which columns altered, and which
/// objects recreated, then emits one fixed phase sequence: drop check/default -> drop indexed-view's
/// indexes/schema-bound view or function/computed column [combined, dependency-safe order] -> drop
/// full-text index -> drop FK -> drop index/PK/UQ [+ extended properties] -> drop remaining computed
/// column -> alter column collation -> (optional) alter database default collation -> mirror image of
/// the drop phases in reverse to recreate everything, replaying captured permissions/extended properties.
///
/// ALTER DATABASE ... COLLATE runs before the recreate phase: SQL Server refuses it while any object
/// without an explicit COLLATE clause (computed columns, checks, filtered index predicates, schema-bound
/// views/functions) still implicitly depends on the database's default collation, so recreating first
/// would immediately re-block it.
///
/// Sequences, synonyms, full-text catalogs, and permissions/extended properties on anything only
/// ALTER COLUMN'd are never touched, only captured for verification - SQL Server ties permissions and
/// extended properties to an object's internal id, which survives ALTER COLUMN and only changes when the
/// object itself is dropped and recreated.
///
/// A single global phase order suffices for most objects (nothing recreated earlier ever depends on
/// something recreated later). Two exceptions need a real dependency closure and topological sort within
/// their own combined phase instead, because they can depend on *each other*:
///
///  - Schema-bound views/functions referencing each other (e.g. an indexed wrapper view built WITH
///    SCHEMABINDING on another schema-bound view) - see <see cref="SchemaBoundObjectReference"/>.
///  - A computed column referenced by a schema-bound object being dropped, or whose expression calls one
///    - see <see cref="ComputedColumnObjectReference"/>. Only computed columns actually touching a
///    to-be-dropped schema-bound object join this phase; the rest keep their simpler position around the
///    regular index drop/recreate steps.
///
/// Check/default constraints are dropped before, and recreated after, that combined phase (not folded
/// into it): their expression can call a schema-bound function being dropped, but unlike a computed
/// column a constraint is never itself something a schema-bound view depends on, so moving it earlier is
/// always safe.
///
/// An indexed view's own indexes are ordered clustered-first for recreate, nonclustered-first for drop:
/// SQL Server requires the one clustered index (which materializes the view) to exist before any
/// nonclustered index on it, and refuses to drop it while one still exists.
///
/// Plain (non-schema-bound) views/procedures/functions/triggers never block ALTER COLUMN and are only
/// captured for verification, never touched.
///
/// With updateDatabaseDefaultCollation, scope for schema-bound objects/checks/computed columns/filtered
/// indexes widens from "tied to a changing column" to "every one in the database" - ALTER DATABASE checks
/// dependencies database-wide, and no catalog view predicts them short of attempting the statement.
/// </summary>
public sealed class MigrationPlanBuilder : IMigrationPlanBuilder
{
    public Domain.Migration.MigrationPlan Build(DatabaseSnapshot snapshot, SqlCollationName targetCollation, bool updateDatabaseDefaultCollation, IReadOnlySet<ColumnRef>? excludedColumns = null)
    {
        var excluded = excludedColumns ?? (IReadOnlySet<ColumnRef>)new HashSet<ColumnRef>();

        var changingColumnsByTable = snapshot.Tables
            .Select(t => (Table: t, Columns: t.ColumnsRequiringCollationChange(targetCollation)
                .Where(c => !excluded.Contains(new ColumnRef(t.Ref.SchemaName, t.Ref.Name, c.Name)))
                .Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .Where(x => x.Columns.Count > 0)
            .ToDictionary(x => x.Table.Ref, x => x.Columns);

        var steps = new List<MigrationStep>();
        var order = 0;

        var foreignKeysToDrop = snapshot.ForeignKeys
            .Where(fk =>
                (changingColumnsByTable.TryGetValue(fk.ParentTable, out var parentCols) && fk.Columns.Any(c => parentCols.Contains(c.ParentColumn))) ||
                (changingColumnsByTable.TryGetValue(fk.ReferencedTable, out var referencedCols) && fk.Columns.Any(c => referencedCols.Contains(c.ReferencedColumn))))
            .ToList();

        // Only WITH SCHEMABINDING views/functions block ALTER COLUMN. When the database's default
        // collation is also changing, every schema-bound object in the database is in scope, not just
        // ones tied to a changing column.
        var directSchemaBoundDrops = updateDatabaseDefaultCollation
            ? snapshot.ProgrammableObjects.Where(o => o.IsSchemaBound).Select(o => o.Ref).ToHashSet()
            : snapshot.SchemaBoundDependencies
                .Where(dep => changingColumnsByTable.TryGetValue(dep.ReferencedTable, out var cols) && cols.Contains(dep.ReferencedColumn))
                .Select(dep => dep.DependentObject)
                .ToHashSet();

        // A schema-bound object referencing another one being dropped must also be dropped - close over
        // that chain (schema-bound-to-schema-bound only; computed columns only affect ordering below).
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

        // A filtered index predicate, or a check/default/computed column expression, can implicitly
        // depend on the database's default collation regardless of which table it lives on - so when
        // that is also changing, every one in the database is in scope, not just ones on a table with a
        // changing column (those are still covered by the per-column checks below).
        foreach (var table in snapshot.Tables)
        {
            var changingColumns = changingColumnsByTable.GetValueOrDefault(table.Ref, []);

            // Computed first: an index covering one of these must be dropped regardless of its own
            // filter/changing-column status, because the column underneath it is about to go.
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

        // Only computed columns touching a to-be-dropped schema-bound object join the combined phase below.
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

        // The combined phase's node set and edges. "dependent -> referenced" means dependent must be
        // dropped before, and recreated after, referenced.
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

        foreach (var r in snapshot.SchemaBoundObjectReferences)
            if (schemaBoundRefsToDrop.Contains(r.DependentObject) && schemaBoundRefsToDrop.Contains(r.ReferencedObject))
                AddEdge(GraphNode.ForSchemaBoundObject(r.DependentObject), GraphNode.ForSchemaBoundObject(r.ReferencedObject));

        foreach (var dep in computedColumnsCalledBySchemaBound)
            AddEdge(GraphNode.ForSchemaBoundObject(dep.DependentObject), GraphNode.ForComputedColumn(dep.ReferencedTable, dep.ReferencedColumn));

        foreach (var r in computedColumnsCallingSchemaBound)
            AddEdge(GraphNode.ForComputedColumn(r.Table, r.ColumnName), GraphNode.ForSchemaBoundObject(r.ReferencedObject));

        // Post-order DFS: referenced nodes end up before their dependents, i.e. the RECREATE order.
        // Reversed, it is the DROP order (dependents first).
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

        // Phase 1: drop everything that would block ALTER COLUMN, in dependency-safe order. Checks/defaults
        // go first since a constraint is never itself something a schema-bound object depends on.
        foreach (var (table, check) in checksToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropCheckConstraint, $"Check-Constraint {SqlIdentifier.QuotePart(check.Name)} auf {table.Ref} entfernen", CheckConstraintScriptGenerator.GenerateDrop(table.Ref, check)));

        foreach (var (table, def) in defaultsToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropDefaultConstraint, $"Default-Constraint {SqlIdentifier.QuotePart(def.Name)} auf {table.Ref} entfernen", DefaultConstraintScriptGenerator.GenerateDrop(table.Ref, def)));

        foreach (var node in combinedDropOrder)
        {
            if (node.SchemaBoundObject is { } objRef)
            {
                var obj = SchemaBoundObjectFor(objRef);

                // An indexed view's indexes must go before the view itself, nonclustered before clustered.
                foreach (var vi in viewIndexesByView.GetValueOrDefault(obj.Ref, []).OrderBy(vi => vi.Index.IsClustered))
                    steps.Add(new MigrationStep(order++, MigrationStepKind.DropIndex, $"Index {SqlIdentifier.QuotePart(vi.Index.Name)} auf indizierter View {obj.Ref} entfernen", IndexScriptGenerator.GenerateDrop(obj.Ref, vi.Index)));

                steps.Add(new MigrationStep(order++, MigrationStepKind.DropSchemaBoundObject, $"Schema-gebundenes Objekt {obj.Ref} entfernen", RawDefinitionScriptGenerator.GenerateDrop(obj)));
            }
            else if (node.ComputedColumn is { } key)
            {
                var (table, column) = computedColumnKeys[key];
                steps.Add(new MigrationStep(order++, MigrationStepKind.DropComputedColumn, $"Berechnete Spalte {SqlIdentifier.QuotePart(column.Name)} auf {table.Ref} entfernen", ComputedColumnScriptGenerator.GenerateDrop(table.Ref, column)));
            }
        }

        // Dropped before regular indexes: a full-text index depends on its KEY INDEX staying present.
        foreach (var fti in fullTextIndexesToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropFullTextIndex, $"Volltextindex auf {fti.Table} entfernen", FullTextIndexScriptGenerator.GenerateDrop(fti)));

        foreach (var fk in foreignKeysToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropForeignKey, $"Foreign Key {SqlIdentifier.QuotePart(fk.Name)} entfernen", ForeignKeyScriptGenerator.GenerateDrop(fk)));

        foreach (var (table, index) in indexesToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropIndex, $"Index/Constraint {SqlIdentifier.QuotePart(index.Name)} auf {table.Ref} entfernen", IndexScriptGenerator.GenerateDrop(table.Ref, index)));

        // Dropped last: must go after any index built on it.
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

        // Phase 3: recreate everything dropped in phase 1, reapplying extended properties/permissions
        // that were attached to a fully recreated object. Computed columns first, so indexes built on
        // them have a column to refer to.
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

        // Recreated after regular indexes so its KEY INDEX exists again.
        foreach (var fti in fullTextIndexesToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.AddFullTextIndex, $"Volltextindex auf {fti.Table} wiederherstellen", FullTextIndexScriptGenerator.GenerateCreate(fti)));

        // Recreated in reverse of the drop order, so a referenced node exists again before its dependent.
        // Permissions/extended properties on the recreated object (and, for an indexed view, its indexes)
        // must be replayed since dropping/recreating loses them.
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

                // Clustered index must be created before any nonclustered index on the same view.
                foreach (var vi in viewIndexesByView.GetValueOrDefault(obj.Ref, []).OrderByDescending(vi => vi.Index.IsClustered))
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

        // Recreated last: their expression can call a schema-bound function, which must exist again first.
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
    /// True if attributed directly to one of the changing columns, or (for table-level expressions) its
    /// definition text mentions one by a word-boundary/bracketed match. The heuristic may over-drop
    /// (e.g. a column name inside a string literal) but never under-drops in a way that corrupts data -
    /// a missed drop just makes SQL Server reject the ALTER COLUMN and the migration rolls back.
    /// </summary>
    private static bool ReferencesAnyColumn(string? attributedColumn, string definition, HashSet<string> changingColumns)
    {
        if (attributedColumn is not null)
            return changingColumns.Contains(attributedColumn);

        return changingColumns.Any(column =>
            Regex.IsMatch(definition, $@"\[{Regex.Escape(column)}\]|\b{Regex.Escape(column)}\b", RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// One node in the combined drop/recreate ordering graph: a schema-bound object or a computed column.
    /// Exactly one of <see cref="SchemaBoundObject"/> / <see cref="ComputedColumn"/> is non-null.
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
