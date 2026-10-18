using System.Windows;
using System.Windows.Input;

namespace DS4Handler.App.Views;

/// <summary>Dark-themed yes/no question.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.ToolTip = $"Bevestig: {confirmText.ToLowerInvariant()}.";
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => CancelButton.Focus(); // the safe choice is the default
    }

    /// <summary>Shows the dialog over <paramref name="owner"/> and returns true when confirmed.</summary>
    public static bool Ask(Window? owner, string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(title, message, confirmText);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    /// <summary>Shows a message with a single OK button.</summary>
    public static void Inform(Window? owner, string title, string message)
    {
        var dialog = new ConfirmDialog(title, message, "OK");
        dialog.CancelButton.Visibility = Visibility.Collapsed;
        dialog.ConfirmButton.ToolTip = "Sluit deze melding.";
        dialog.Loaded += (_, _) => dialog.ConfirmButton.Focus();
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
