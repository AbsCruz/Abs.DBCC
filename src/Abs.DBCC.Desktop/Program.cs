using Avalonia;
using System;

namespace Abs.DBCC.Desktop;

sealed class Program
{
    // Don't use Avalonia/third-party APIs or SynchronizationContext-reliant code before AppMain
    // is called: nothing is initialized yet.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
