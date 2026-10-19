using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace DS4Handler.App.ViewModels;

/// <summary>Minimal INotifyPropertyChanged base. SetField only raises when the value really changed.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>For live values: ignores changes smaller than <paramref name="epsilon"/> to avoid needless redraws.</summary>
    protected bool SetNumber(ref double field, double value, double epsilon = 0.002, [CallerMemberName] string? name = null)
    {
        if (Math.Abs(field - value) < epsilon) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>Simple ICommand around delegates.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) _execute();
    }
}

/// <summary>ICommand around a delegate that takes the command parameter.</summary>
public sealed class RelayCommand<T> : ICommand where T : class
{
    private readonly Action<T?> _execute;

    public RelayCommand(Action<T?> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute(parameter as T);
}
