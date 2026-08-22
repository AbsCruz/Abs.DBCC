using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Verification;

/// <summary>
/// Pure diffing logic: compares a before/after pair of <see cref="DatabaseSnapshot"/>s and reports
/// every structural difference, except the expected collation change on columns that now carry
/// <paramref name="targetCollation"/> (a column's collation differing from before is only acceptable
/// when the new value is exactly the target collation - anything else is reported).
/// </summary>
public static class DatabaseSnapshotComparer
{
    public static IReadOnlyList<StructuralDiff> Compare(DatabaseSnapshot before, DatabaseSnapshot after, SqlCollationName targetCollation)
    {
        var diffs = new List<StructuralDiff>();

        var beforeTables = before.Tables.ToDictionary(t => t.Ref);
        var afterTables = after.Tables.ToDictionary(t => t.Ref);

        foreach (var missing in beforeTables.Keys.Except(afterTables.Keys))
            diffs.Add(new StructuralDiff(missing.ToString(), "Tabelle fehlt nach der Migration."));
        foreach (var extra in afterTables.Keys.Except(beforeTables.Keys))
            diffs.Add(new StructuralDiff(extra.ToString(), "Unerwartete zusätzliche Tabelle nach der Migration."));

        foreach (var tableRef in beforeTables.Keys.Intersect(afterTables.Keys))
            CompareTable(beforeTables[tableRef], afterTables[tableRef], targetCollation, diffs);

        CompareForeignKeys(before.ForeignKeys, after.ForeignKeys, diffs);

        CompareByName("Datenbank", "Objekt", before.ProgrammableObjects, after.ProgrammableObjects, o => o.Ref.ToString(),
            (b, a) => b.DefinitionScript == a.DefinitionScript && b.IsSchemaBound == a.IsSchemaBound, diffs);

        CompareByName("Datenbank", "Sequenz", before.Sequences, after.Sequences, s => s.Ref.ToString(),
            (b, a) => b == a, diffs);

        CompareByName("Datenbank", "Synonym", before.Synonyms, after.Synonyms, s => s.Ref.ToString(),
            (b, a) => b.BaseObjectName == a.BaseObjectName, diffs);

        CompareByName("Datenbank", "Volltextkatalog", before.FullTextCatalogs, after.FullTextCatalogs, c => c.Name,
            (b, a) => b.IsDefault == a.IsDefault, diffs);

        CompareByName("Datenbank", "Volltextindex", before.FullTextIndexes, after.FullTextIndexes, i => i.Table.ToString(),
            FullTextIndexesEqual, diffs);

        CompareByName("Datenbank", "Berechtigung", before.Permissions, after.Permissions, PermissionKey, (b, a) => b == a, diffs);

        CompareByName("Datenbank", "Extended Property", before.ExtendedProperties, after.ExtendedProperties, ExtendedPropertyKey, (b, a) => b == a, diffs);

        CompareByName("Datenbank", "View-Index", before.ViewIndexes, after.ViewIndexes, vi => $"{vi.View}/{vi.Index.Name}",
            (b, a) => IndexesEqual(b.Index, a.Index), diffs);

        return diffs;
    }

    private static string PermissionKey(PermissionSnapshot p) =>
        $"{p.GranteePrincipal}/{p.PermissionName}/{p.OnObject}/{p.OnColumn}/{p.OnSchema}";

    private static string ExtendedPropertyKey(ExtendedPropertySnapshot p) =>
        $"{p.Object}/{p.ColumnName}/{p.PropertyName}/{p.ParentTable}";

    private static bool FullTextIndexesEqual(FullTextIndexSnapshot before, FullTextIndexSnapshot after) =>
        before.CatalogName == after.CatalogName &&
        before.KeyIndexName == after.KeyIndexName &&
        before.ChangeTracking == after.ChangeTracking &&
        before.Columns.Select(c => (c.ColumnName.ToUpperInvariant(), c.LanguageId))
            .SequenceEqual(after.Columns.Select(c => (c.ColumnName.ToUpperInvariant(), c.LanguageId)));

    private static void CompareTable(TableSnapshot before, TableSnapshot after, SqlCollationName targetCollation, List<StructuralDiff> diffs)
    {
        var description = before.Ref.ToString();

        var beforeColumns = before.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var afterColumns = after.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var missing in beforeColumns.Keys.Except(afterColumns.Keys, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new StructuralDiff(description, $"Spalte [{missing}] fehlt nach der Migration."));
        foreach (var extra in afterColumns.Keys.Except(beforeColumns.Keys, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new StructuralDiff(description, $"Unerwartete zusätzliche Spalte [{extra}] nach der Migration."));

        foreach (var name in beforeColumns.Keys.Intersect(afterColumns.Keys, StringComparer.OrdinalIgnoreCase))
            CompareColumn(description, beforeColumns[name], afterColumns[name], targetCollation, diffs);

        CompareByName(description, "Index/Constraint", before.Indexes, after.Indexes, i => i.Name,
            (b, a) => IndexesEqual(b, a), diffs);

        CompareByName(description, "Check-Constraint", before.CheckConstraints, after.CheckConstraints, c => c.Name,
            (b, a) => string.Equals(b.ColumnName, a.ColumnName, StringComparison.OrdinalIgnoreCase) && b.Definition == a.Definition, diffs);

        CompareByName(description, "Default-Constraint", before.DefaultConstraints, after.DefaultConstraints, c => c.Name,
            (b, a) => string.Equals(b.ColumnName, a.ColumnName, StringComparison.OrdinalIgnoreCase) && b.Definition == a.Definition, diffs);
    }

    private static void CompareColumn(string tableDescription, ColumnSnapshot before, ColumnSnapshot after, SqlCollationName targetCollation, List<StructuralDiff> diffs)
    {
        var prefix = $"Spalte [{before.Name}]";

        if (!string.Equals(before.SqlDataType, after.SqlDataType, StringComparison.OrdinalIgnoreCase))
            diffs.Add(new StructuralDiff(tableDescription, $"{prefix}: Datentyp {before.SqlDataType} -> {after.SqlDataType}."));
        if (before.MaxLength != after.MaxLength)
            diffs.Add(new StructuralDiff(tableDescription, $"{prefix}: MaxLength {before.MaxLength} -> {after.MaxLength}."));
        if (before.Precision != after.Precision || before.Scale != after.Scale)
            diffs.Add(new StructuralDiff(tableDescription, $"{prefix}: Precision/Scale geändert."));
        if (before.IsNullable != after.IsNullable)
            diffs.Add(new StructuralDiff(tableDescription, $"{prefix}: Nullability geändert."));
        if (before.IsComputed != after.IsComputed || before.ComputedDefinition != after.ComputedDefinition || before.IsComputedPersisted != after.IsComputedPersisted)
            diffs.Add(new StructuralDiff(tableDescription, $"{prefix}: Definition der berechneten Spalte geändert."));

        if (after.Collation != before.Collation && after.Collation != targetCollation)
            diffs.Add(new StructuralDiff(tableDescription, $"{prefix}: unerwartete Collation {before.Collation?.Value} -> {after.Collation?.Value}."));
    }

    private static bool IndexesEqual(IndexSnapshot before, IndexSnapshot after) =>
        before.IsUnique == after.IsUnique &&
        before.IsClustered == after.IsClustered &&
        before.IsPrimaryKey == after.IsPrimaryKey &&
        before.IsUniqueConstraint == after.IsUniqueConstraint &&
        before.FilterDefinition == after.FilterDefinition &&
        before.Columns.Select(c => (c.ColumnName.ToUpperInvariant(), c.IsDescending, c.IsIncluded))
            .SequenceEqual(after.Columns.Select(c => (c.ColumnName.ToUpperInvariant(), c.IsDescending, c.IsIncluded)));

    private static void CompareForeignKeys(
        IReadOnlyList<ForeignKeySnapshot> before, IReadOnlyList<ForeignKeySnapshot> after, List<StructuralDiff> diffs)
    {
        CompareByName("Datenbank", "Foreign Key", before, after, fk => fk.Name, ForeignKeysEqual, diffs);
    }

    private static bool ForeignKeysEqual(ForeignKeySnapshot before, ForeignKeySnapshot after) =>
        before.ParentTable == after.ParentTable &&
        before.ReferencedTable == after.ReferencedTable &&
        before.DeleteAction == after.DeleteAction &&
        before.UpdateAction == after.UpdateAction &&
        before.IsNotForReplication == after.IsNotForReplication &&
        before.Columns.Select(c => (c.ParentColumn.ToUpperInvariant(), c.ReferencedColumn.ToUpperInvariant()))
            .SequenceEqual(after.Columns.Select(c => (c.ParentColumn.ToUpperInvariant(), c.ReferencedColumn.ToUpperInvariant())));

    private static void CompareByName<T>(
        string contextDescription,
        string objectKindLabel,
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        Func<T, string> nameSelector,
        Func<T, T, bool> equals,
        List<StructuralDiff> diffs)
    {
        var beforeByName = before.ToDictionary(nameSelector, StringComparer.OrdinalIgnoreCase);
        var afterByName = after.ToDictionary(nameSelector, StringComparer.OrdinalIgnoreCase);

        foreach (var missing in beforeByName.Keys.Except(afterByName.Keys, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new StructuralDiff(contextDescription, $"{objectKindLabel} [{missing}] fehlt nach der Migration."));
        foreach (var extra in afterByName.Keys.Except(beforeByName.Keys, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new StructuralDiff(contextDescription, $"Unerwarteter zusätzlicher {objectKindLabel} [{extra}] nach der Migration."));

        foreach (var name in beforeByName.Keys.Intersect(afterByName.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (!equals(beforeByName[name], afterByName[name]))
                diffs.Add(new StructuralDiff(contextDescription, $"{objectKindLabel} [{name}] unterscheidet sich nach der Migration."));
        }
    }
}
