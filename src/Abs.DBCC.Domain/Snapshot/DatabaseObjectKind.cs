namespace Abs.DBCC.Domain.Snapshot;

public enum DatabaseObjectKind
{
    Table,
    Column,
    Index,
    PrimaryKey,
    UniqueConstraint,
    ForeignKey,
    CheckConstraint,
    DefaultConstraint,
    ComputedColumn,
    View,
    StoredProcedure,
    Function,
    Trigger,
    Sequence,
    Synonym
}
