using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Inspection;
using Abs.DBCC.Domain.Migration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Abs.DBCC.Desktop.ViewModels;

/// <summary>Bindable wrapper around a <see cref="ColumnCollationState"/>, adding the exclude-from-migration toggle.</summary>
public partial class CollationColumnRowViewModel(ColumnCollationState state) : ObservableObject
{
    public ColumnCollationState State { get; } = state;

    public string ColumnName => State.ColumnName;

    public string SqlDataType => State.SqlDataType;

    public string? CollationDisplay => State.Collation?.Value;

    /// <summary>Only a column that would actually be touched by a collation migration can be excluded from it.</summary>
    public bool CanExclude => State.Collation is not null;

    [ObservableProperty]
    public partial bool IsExcluded { get; set; }

    public ColumnRef ColumnRef => new(State.SchemaName, State.TableName, State.ColumnName);
}

/// <summary>Bindable wrapper around a <see cref="TableCollationReport"/>.</summary>
public sealed class CollationTableRowViewModel(TableCollationReport report)
{
    public string SchemaName => report.SchemaName;

    public string TableName => report.TableName;

    public bool IsMixedCollation => report.IsMixedCollation;

    public IReadOnlyList<CollationColumnRowViewModel> Columns { get; } =
        report.Columns.Select(c => new CollationColumnRowViewModel(c)).ToList();
}

/// <summary>A table row as currently shown, after the collation filter narrows down its visible columns.</summary>
public sealed record CollationTableDisplay(CollationTableRowViewModel Row, IReadOnlyList<CollationColumnRowViewModel> VisibleColumns);

/// <summary>One entry in the collation filter dropdown; <see cref="Collation"/> is null for the "show everything" option.</summary>
public sealed record CollationFilterOption(SqlCollationName? Collation, string DisplayName);
