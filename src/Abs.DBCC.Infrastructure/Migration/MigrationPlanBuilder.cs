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
///   drop indexed-view's indexes -> drop schema-bound view/function -> drop full-text index -> drop FK
///   -> drop index/PK/UQ [+ its extended properties captured for reapply] -> drop check -> drop default
///   -> drop computed column
///   -> alter column collation
///   -> (optional) alter database default collation
///   -> add computed column [+ its extended properties] -> add default [+ its extended properties]
///   -> add check [+ its extended properties] -> add index/PK/UQ [+ its extended properties] -> add FK
///   [+ its extended properties] -> add full-text index -> add schema-bound view/function [+ its
///   permissions/extended properties] -> add indexed-view's indexes [+ their extended properties]
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
/// for this object scope: nothing recreated in an earlier phase ever depends on something recreated in
/// a later one (e.g. every foreign key is only added once all tables' columns/indexes already exist).
/// If a later milestone adds objects with genuine cross-object dependencies (e.g. a schema-bound view
/// referencing another schema-bound view), a real dependency graph will be needed for those - not for
/// this scope, which only resolves direct table-column dependencies (sys.sql_expression_dependencies).
///
/// Plain (non-schema-bound) views/procedures/functions/triggers never block ALTER COLUMN in SQL Server
/// and are therefore never touched here - they are only captured for structural verification.
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
        var schemaBoundObjectsToDrop = snapshot.SchemaBoundDependencies
            .Where(dep => changingColumnsByTable.TryGetValue(dep.ReferencedTable, out var cols) && cols.Contains(dep.ReferencedColumn))
            .Select(dep => dep.DependentObject)
            .Distinct()
            .Select(objRef => snapshot.ProgrammableObjects.First(o => o.Ref == objRef))
            .ToList();

        var viewIndexesByView = snapshot.ViewIndexes.GroupBy(vi => vi.View).ToDictionary(g => g.Key, g => g.ToList());

        var fullTextIndexesToDrop = snapshot.FullTextIndexes
            .Where(fti => changingColumnsByTable.TryGetValue(fti.Table, out var cols) && cols.Any(fti.CoversColumn))
            .ToList();

        var tablesToAlter = snapshot.Tables.Where(t => changingColumnsByTable.ContainsKey(t.Ref)).ToList();

        var indexesToDrop = new List<(TableSnapshot Table, IndexSnapshot Index)>();
        var checksToDrop = new List<(TableSnapshot Table, CheckConstraintSnapshot Check)>();
        var defaultsToDrop = new List<(TableSnapshot Table, DefaultConstraintSnapshot Default)>();
        var computedColumnsToDrop = new List<(TableSnapshot Table, ColumnSnapshot Column)>();

        foreach (var table in tablesToAlter)
        {
            var changingColumns = changingColumnsByTable[table.Ref];

            indexesToDrop.AddRange(table.Indexes
                .Where(index => changingColumns.Any(index.CoversColumn))
                .Select(index => (table, index)));

            checksToDrop.AddRange(table.CheckConstraints
                .Where(check => ReferencesAnyColumn(check.ColumnName, check.Definition, changingColumns))
                .Select(check => (table, check)));

            defaultsToDrop.AddRange(table.DefaultConstraints
                .Where(def => changingColumns.Contains(def.ColumnName))
                .Select(def => (table, def)));

            computedColumnsToDrop.AddRange(table.Columns
                .Where(col => col.IsComputed && ReferencesAnyColumn(null, col.ComputedDefinition ?? "", changingColumns))
                .Select(col => (table, col)));
        }

        IEnumerable<ExtendedPropertySnapshot> ExtendedPropsFor(ObjectRef parentTable, DatabaseObjectKind kind, string name) =>
            snapshot.ExtendedProperties.Where(p =>
                p.ParentTable == parentTable && p.Object.Kind == kind && string.Equals(p.Object.Name, name, StringComparison.OrdinalIgnoreCase));

        // Phase 1: drop everything that would block ALTER COLUMN, in dependency-safe order.
        foreach (var obj in schemaBoundObjectsToDrop)
        {
            // An indexed view's own indexes must go before the view itself (SQL Server refuses to DROP
            // VIEW while it still has an index, exactly like a table).
            foreach (var vi in viewIndexesByView.GetValueOrDefault(obj.Ref, []))
                steps.Add(new MigrationStep(order++, MigrationStepKind.DropIndex, $"Index {SqlIdentifier.QuotePart(vi.Index.Name)} auf indizierter View {obj.Ref} entfernen", IndexScriptGenerator.GenerateDrop(obj.Ref, vi.Index)));

            steps.Add(new MigrationStep(order++, MigrationStepKind.DropSchemaBoundObject, $"Schema-gebundenes Objekt {obj.Ref} entfernen", RawDefinitionScriptGenerator.GenerateDrop(obj)));
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

        foreach (var (table, check) in checksToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropCheckConstraint, $"Check-Constraint {SqlIdentifier.QuotePart(check.Name)} auf {table.Ref} entfernen", CheckConstraintScriptGenerator.GenerateDrop(table.Ref, check)));

        foreach (var (table, def) in defaultsToDrop)
            steps.Add(new MigrationStep(order++, MigrationStepKind.DropDefaultConstraint, $"Default-Constraint {SqlIdentifier.QuotePart(def.Name)} auf {table.Ref} entfernen", DefaultConstraintScriptGenerator.GenerateDrop(table.Ref, def)));

        foreach (var (table, column) in computedColumnsToDrop)
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
        foreach (var (table, column) in computedColumnsToDrop)
        {
            steps.Add(new MigrationStep(order++, MigrationStepKind.AddComputedColumn, $"Berechnete Spalte {SqlIdentifier.QuotePart(column.Name)} auf {table.Ref} wiederherstellen", ComputedColumnScriptGenerator.GenerateCreate(table.Ref, column)));

            foreach (var prop in snapshot.ExtendedProperties.Where(p => p.Object == table.Ref && p.ColumnName == column.Name))
                steps.Add(new MigrationStep(order++, MigrationStepKind.AddExtendedProperty, $"Extended Property {SqlIdentifier.QuotePart(prop.PropertyName)} auf {table.Ref}.{SqlIdentifier.QuotePart(column.Name)} wiederherstellen", ExtendedPropertyScriptGenerator.GenerateAdd(prop)));
        }

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
        // indexes) - all must be replayed afterwards.
        foreach (var obj in schemaBoundObjectsToDrop)
        {
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
}
