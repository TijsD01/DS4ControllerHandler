using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DS4Handler.App.Infrastructure;

namespace DS4Handler.Mac;

public partial class MainWindow : Window
{
    private readonly MacHost _host;
    private int _version = -1;

    public MainWindow() : this(new MacHost()) { } // designer only

    public MainWindow(MacHost host)
    {
        _host = host;
        InitializeComponent();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    private void Tick()
    {
        int version = _host.Manager.Version;
        if (version != _version)
        {
            _version = version;
            SyncCards();
        }
        foreach (var card in Cards.Children)
            ((ControllerCard)card).Tick();
    }

    /// <summary>One card per running session; existing cards are kept so their switches and confirm step survive.</summary>
    private void SyncCards()
    {
        var sessions = _host.Manager.Sessions;
        var keep = Cards.Children.Cast<ControllerCard>().Where(c => sessions.Contains(c.Session!)).ToList();
        Cards.Children.Clear();
        foreach (var session in sessions)
            Cards.Children.Add(keep.FirstOrDefault(c => c.Session == session) ?? new ControllerCard(session, _host));
        Empty.IsVisible = Cards.Children.Count == 0;
    }

    private void OnOpenProfiles(object? sender, RoutedEventArgs e) => MacHost.OpenFolder(_host.Profiles.Directory);

    private void OnOpenLog(object? sender, RoutedEventArgs e) => MacHost.OpenFolder(Log.DataDirectory);
}
