using System.Windows;
using DS4Handler.App.Lang;
using DS4Handler.App.ViewModels;
using Microsoft.Win32;

namespace DS4Handler.App.Views;

/// <summary>WPF implementation of <see cref="IDialogs"/>, centred on the main window.</summary>
public sealed class Dialogs : IDialogs
{
    private static readonly string Filter = Loc.Get("Dialog_ProfileFilter");
    private readonly Func<Window?> _owner;

    public Dialogs(Func<Window?> owner) => _owner = owner;

    public bool Confirm(string title, string message, string confirmText) =>
        ConfirmDialog.Ask(_owner(), title, message, confirmText);

    public void Inform(string title, string message) => ConfirmDialog.Inform(_owner(), title, message);

    public string? SaveProfileFile(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.Get("Dialog_ExportTitle"),
            Filter = Filter,
            FileName = suggestedName,
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    private bool Show(CommonDialog dialog)
    {
        var owner = _owner();
        return (owner is { IsVisible: true } ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
    }

    public string? OpenProfileFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.Get("Dialog_ImportTitle"),
            Filter = Filter,
            CheckFileExists = true,
        };
        return Show(dialog) ? dialog.FileName : null;
    }
}
