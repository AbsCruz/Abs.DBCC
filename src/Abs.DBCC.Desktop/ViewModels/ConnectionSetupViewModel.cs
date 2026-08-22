using Abs.DBCC.Application.Connections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;

namespace Abs.DBCC.Desktop.ViewModels;

public partial class ConnectionSetupViewModel(ISender sender) : ViewModelBase
{
    [ObservableProperty]
    public partial string Server { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Database { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string User { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool TrustServerCertificate { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    public partial bool IsConnectionVerified { get; set; }

    public event EventHandler<ConnectionProfile>? ConnectionConfirmed;

    private ConnectionProfile BuildProfile() => new(Server, Database, User, Password, TrustServerCertificate);

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        IsConnectionVerified = false;
        StatusMessage = null;

        try
        {
            var result = await sender.Send(new TestConnectionQuery(BuildProfile()));
            IsConnectionVerified = result.IsSuccess;
            StatusMessage = result.IsSuccess
                ? "Verbindung erfolgreich."
                : $"Verbindung fehlgeschlagen: {result.Error}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnectionVerified))]
    private void Continue() => ConnectionConfirmed?.Invoke(this, BuildProfile());
}
