using Abs.DBCC.Application.Connections;
using Abs.DBCC.Desktop.ViewModels;
using Abs.DBCC.SharedKernel;
using MediatR;
using Moq;

namespace Abs.DBCC.Desktop.Test.ViewModels;

public class ConnectionSetupViewModelTests
{
    private static ConnectionSetupViewModel Vm(Mock<ISender> sender) => new(sender.Object)
    {
        Server = "server", Database = "db", User = "user", Password = "pw"
    };

    [Fact]
    public async Task ContinueCommand_ConnectionSucceeds_RaisesConnectionConfirmed()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<TestConnectionQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var vm = Vm(sender);
        ConnectionProfile? confirmed = null;
        vm.ConnectionConfirmed += (_, profile) => confirmed = profile;

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.NotNull(confirmed);
        Assert.Equal("server", confirmed.Server);
        Assert.Equal("db", confirmed.Database);
        Assert.Null(vm.StatusMessage);
    }

    [Fact]
    public async Task ContinueCommand_ConnectionFails_DoesNotRaiseConnectionConfirmedAndShowsError()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<TestConnectionQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("login failed"));
        var vm = Vm(sender);
        var raised = false;
        vm.ConnectionConfirmed += (_, _) => raised = true;

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Contains("login failed", vm.StatusMessage);
    }

    [Fact]
    public async Task ContinueCommand_ConnectionFails_ThenRetried_ClearsPreviousError()
    {
        var sender = new Mock<ISender>();
        sender.SetupSequence(s => s.Send(It.IsAny<TestConnectionQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("login failed"))
            .ReturnsAsync(Result.Success());
        var vm = Vm(sender);
        ConnectionProfile? confirmed = null;
        vm.ConnectionConfirmed += (_, profile) => confirmed = profile;

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.NotNull(vm.StatusMessage);

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Null(vm.StatusMessage);
        Assert.NotNull(confirmed);
    }
}
