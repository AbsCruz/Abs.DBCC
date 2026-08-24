using Abs.DBCC.Application.Connections;
using Abs.DBCC.Desktop.Localization;
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
    public partial bool IsConnecting { get; set; }

    public event EventHandler<ConnectionProfile>? ConnectionConfirmed;

    private ConnectionProfile BuildProfile() => new(Server, Database, User, Password, TrustServerCertificate);

    /// <summary>Tests the connection and only proceeds once it actually succeeds.</summary>
    [RelayCommand]
    private async Task ContinueAsync()
    {
        IsConnecting = true;
        StatusMessage = null;

        try
        {
            var profile = BuildProfile();
            var result = await sender.Send(new TestConnectionQuery(profile));

            if (result.IsSuccess)
                ConnectionConfirmed?.Invoke(this, profile);
            else
                StatusMessage = string.Format(Strings.ConnectionFailedFormat, result.Error);
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
