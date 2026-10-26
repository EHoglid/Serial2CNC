using System.Windows.Input;

namespace Serial2CNC.Presentation;

public sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncDelegateCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }
        _executing = true;
        Refresh();
        try
        {
            await execute();
        }
        finally
        {
            _executing = false;
            Refresh();
        }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
