namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// An extended property (e.g. MS_Description) on:
///  - a table/view/procedure/function/trigger, or one of its columns (sys.extended_properties class 1,
///    "Object or Column" - <see cref="Object"/> is the table/view/etc, <see cref="ParentTable"/> null),
///  - a constraint (PK/UQ/CHECK/DEFAULT/FK - also class 1, since constraints are rows in sys.objects
///    too; <see cref="Object"/> is the constraint itself, <see cref="ParentTable"/> the table it belongs
///    to), or
///  - an index, including one on an indexed view (sys.extended_properties class 7, "Index";
///    <see cref="Object"/> is the index, <see cref="ParentTable"/> the table or view it is defined on).
/// </summary>
public sealed record ExtendedPropertySnapshot(
    ObjectRef Object,
    string? ColumnName,
    string PropertyName,
    string PropertyValue,
    ObjectRef? ParentTable = null);
