using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DS4Handler.App.ViewModels;

namespace DS4Handler.App.Views;

/// <summary>
/// The "Knoppen" content of one controller; DataContext is a <see cref="RemapViewModel"/>.
/// View-only work here: listening for a key, scrolling to a found row, focus, and keeping the
/// controller picture in sight while the list scrolls.
/// </summary>
public partial class RemapPanel : UserControl
{
    private RemapViewModel? _vm;
    private Window? _window;
    private ScrollViewer? _scroller;

    public RemapPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as RemapViewModel);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => { if (!IsVisible) _vm?.CancelCapture(); };
        Columns.SizeChanged += (_, _) => UpdatePicture();
    }

    private void Attach(RemapViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.RowFound -= OnRowFound;
            _vm.CaptureStarted -= OnCaptureStarted;
            _vm.CaptureEnded -= OnCaptureEnded;
        }
        _vm = vm;
        if (_vm != null)
        {
            _vm.RowFound += OnRowFound;
            _vm.CaptureStarted += OnCaptureStarted;
            _vm.CaptureEnded += OnCaptureEnded;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window != null) _window.PreviewKeyDown += OnWindowKeyDown;
        _scroller = FindAncestor<ScrollViewer>(this);
        if (_scroller != null) _scroller.ScrollChanged += OnScrollChanged;
        UpdatePicture();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm?.CancelCapture();
        if (_window != null) _window.PreviewKeyDown -= OnWindowKeyDown;
        if (_scroller != null) _scroller.ScrollChanged -= OnScrollChanged;
        _window = null;
        _scroller = null;
    }

    /// <summary>While listening, every key goes to the view model and nowhere else (no tab switching, no buttons).</summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is not { IsCapturing: true }) return;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,                 // Alt, F10
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        if (_vm.HandleKey(key)) e.Handled = true;
    }

    /// <summary>
    /// Arrow keys on a closed drop-down open the list instead of changing the target one step at a time,
    /// so arrowing past "Toetsenbord…" never starts listening for a key (and "Uit" stays reachable).
    /// </summary>
    private void OnTargetKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: false } box) return;
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            box.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void OnTargetOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox { DataContext: RemapRow row }) row.IsListOpen = true;
    }

    private void OnTargetClosed(object? sender, EventArgs e)
    {
        if (sender is ComboBox { DataContext: RemapRow row })
            Dispatcher.BeginInvoke(() => row.IsListOpen = false, DispatcherPriority.Input);
    }

    private void OnRowFound(RemapRow row)
    {
        if (FindRow(row) is not { } element) return;
        // A little room above and below, so the row does not stick to the edge of the window.
        element.BringIntoView(new Rect(0, -48, element.ActualWidth, element.ActualHeight + 96));
    }

    private void OnCaptureStarted(RemapRow row) => FocusInRow(row, e => e is Border { Tag: "capture" });

    private void OnCaptureEnded(RemapRow row) => FocusInRow(row, e => e is ComboBox);

    /// <summary>Focus after the row has swapped the drop-down and the key box (they change visibility first).</summary>
    private void FocusInRow(RemapRow row, Func<DependencyObject, bool> match) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (FindRow(row) is { } element && FindDescendant(element, match) is IInputElement target)
                Keyboard.Focus(target);
        }, DispatcherPriority.Input);

    private FrameworkElement? FindRow(RemapRow row) =>
        FindDescendant(ListCard, e => e is FrameworkElement { Tag: "row" } f && f.DataContext == row) as FrameworkElement;

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e) => UpdatePicture();

    /// <summary>
    /// Keeps the picture card at the top of the window while the list scrolls past, but never below
    /// the end of the list. A render transform, so it never changes the layout.
    /// </summary>
    private void UpdatePicture()
    {
        if (_scroller == null || !IsVisible) return;
        const double gap = 16;
        double top = Columns.TranslatePoint(new Point(0, 0), _scroller).Y;
        double room = Math.Max(0, Columns.ActualHeight - PictureCard.ActualHeight);
        PictureShift.Y = Math.Clamp(gap - top, 0, room);
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var d = VisualTreeHelper.GetParent(start); d != null; d = VisualTreeHelper.GetParent(d))
            if (d is T t) return t;
        return null;
    }

    private static DependencyObject? FindDescendant(DependencyObject root, Func<DependencyObject, bool> match)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (match(child)) return child;
            if (FindDescendant(child, match) is { } found) return found;
        }
        return null;
    }
}
