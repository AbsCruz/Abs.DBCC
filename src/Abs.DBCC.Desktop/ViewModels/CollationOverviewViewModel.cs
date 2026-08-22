using Abs.DBCC.Application.Collations;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Domain.Inspection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;

namespace Abs.DBCC.Desktop.ViewModels;

public partial class CollationOverviewViewModel : ViewModelBase
{
    private readonly ISender _sender;
    private readonly ConnectionProfile _profile;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? DatabaseDefaultCollation { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<TableCollationReport> Tables { get; set; } = [];

    public event EventHandler? BackRequested;
    public event EventHandler? ContinueRequested;

    public CollationOverviewViewModel(ISender sender, ConnectionProfile profile)
    {
        _sender = sender;
        _profile = profile;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var report = await _sender.Send(new GetDatabaseCollationReportQuery(_profile));
            DatabaseDefaultCollation = report.DatabaseDefaultCollation.Value;
            Tables = report.Tables;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Continue() => ContinueRequested?.Invoke(this, EventArgs.Empty);
}
