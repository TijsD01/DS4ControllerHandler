using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DS4Handler.App.ViewModels;

namespace DS4Handler.App.Views;

/// <summary>The "Profielen" tab; DataContext is a <see cref="ProfilesViewModel"/>.</summary>
public partial class ProfilesPage : UserControl
{
    public ProfilesPage() => InitializeComponent();

    private void OnNameLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ProfileRowViewModel row || box.Tag is true) return;
        box.Tag = true;
        // "Hernoemen" focuses the name box and selects the text.
        row.RenameRequested += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileRowViewModel row }) row.CommitName();
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape) || sender is not TextBox box) return;
        if (e.Key == Key.Escape && box.DataContext is ProfileRowViewModel row) row.RevertName();
        Keyboard.ClearFocus(); // commits through LostKeyboardFocus
        e.Handled = true;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProfileRowViewModel row } button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var target in row.CopyTargets)
        {
            var item = new MenuItem { Header = target.Label, ToolTip = target.Tip };
            item.Click += (_, _) => row.CopyTo(target);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
