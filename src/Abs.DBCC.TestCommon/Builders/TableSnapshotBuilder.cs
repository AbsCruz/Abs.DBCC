using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.TestCommon.Builders;

public sealed class TableSnapshotBuilder(string schema, string name)
{
    private readonly List<ColumnSnapshot> _columns = [];
    private readonly List<IndexSnapshot> _indexes = [];
    private readonly List<CheckConstraintSnapshot> _checks = [];
    private readonly List<DefaultConstraintSnapshot> _defaults = [];

    public ObjectRef Ref { get; } = new(schema, name, DatabaseObjectKind.Table);

    public TableSnapshotBuilder WithColumn(
        string columnName, string type, string? collation, bool isNullable = true, int? maxLength = null)
    {
        _columns.Add(new ColumnSnapshot(
            columnName, type, maxLength, null, null, isNullable,
            collation is null ? null : new SqlCollationName(collation), false, null, false));
        return this;
    }

    public TableSnapshotBuilder WithComputedColumn(string columnName, string definition, bool persisted = false)
    {
        _columns.Add(new ColumnSnapshot(columnName, "nvarchar", null, null, null, true, null, true, definition, persisted));
        return this;
    }

    public TableSnapshotBuilder WithIndex(
        string indexName, IReadOnlyList<string> keyColumns, bool isUnique = false, bool isClustered = false,
        bool isPrimaryKey = false, bool isUniqueConstraint = false, string? filter = null)
    {
        _indexes.Add(new IndexSnapshot(
            indexName, isUnique, isClustered, isPrimaryKey, isUniqueConstraint,
            keyColumns.Select(c => new IndexColumnSnapshot(c, false, false)).ToList(), filter));
        return this;
    }

    public TableSnapshotBuilder WithCheckConstraint(string name, string? columnName, string definition)
    {
        _checks.Add(new CheckConstraintSnapshot(name, columnName, definition));
        return this;
    }

    public TableSnapshotBuilder WithDefaultConstraint(string name, string columnName, string definition)
    {
        _defaults.Add(new DefaultConstraintSnapshot(name, columnName, definition));
        return this;
    }

    public TableSnapshot Build() => new(Ref, _columns, _indexes, _checks, _defaults);
}
