using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DS4Handler.App.Infrastructure;

namespace DS4Handler.Mac;

public partial class App : Application
{
    private MacHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log.Error("Onverwachte fout (achtergrond)", args.ExceptionObject as Exception);
            Log.Info("Gestart (macOS).");
            _host = new MacHost();
            desktop.MainWindow = new MainWindow(_host);
            desktop.ShutdownRequested += (_, _) => _host.Dispose();
            _host.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
