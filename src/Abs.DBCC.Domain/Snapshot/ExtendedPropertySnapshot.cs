namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// An extended property (e.g. MS_Description) on a table/view/procedure/function/trigger or one of its
/// columns, on a constraint (PK/UQ/CHECK/DEFAULT/FK, itself a row in sys.objects), or on an index
/// (including one on an indexed view). <see cref="ParentTable"/> is set only for constraints and indexes,
/// naming the table/view the constraint or index belongs to; <see cref="Object"/> is always the property's
/// direct target.
/// </summary>
public sealed record ExtendedPropertySnapshot(
    ObjectRef Object,
    string? ColumnName,
    string PropertyName,
    string PropertyValue,
    ObjectRef? ParentTable = null);
