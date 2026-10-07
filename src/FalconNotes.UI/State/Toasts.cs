namespace FalconNotes.UI.State;

/// <summary>A button in a toast, such as Undo; choosing it also dismisses the toast.</summary>
/// <param name="Label">The button's label.</param>
/// <param name="OnClick">What it does.</param>
public sealed record ToastAction(string Label, Func<Task> OnClick);

/// <summary>A short notification.</summary>
/// <param name="Id">Its number.</param>
/// <param name="Message">The text.</param>
/// <param name="IsError">Shown in red, and for longer.</param>
/// <param name="Action">A button, if any.</param>
public sealed record Toast(int Id, string Message, bool IsError, ToastAction? Action);

/// <summary>
/// Short, non-blocking notifications (port of <c>components/Toaster.tsx</c>): at most three at once; each goes after
/// 4 s, or 8 s for errors and toasts with a button, to give time to reach it.
/// </summary>
/// <param name="time">The clock, for the timers.</param>
public sealed class Toasts(TimeProvider time)
{
    private readonly List<Toast> _toasts = [];
    private int _nextId = 1;

    /// <summary>Raised when the list changes; may run on any thread.</summary>
    public event Action? Changed;

    /// <summary>The toasts showing, oldest first.</summary>
    public IReadOnlyList<Toast> Current
    {
        get
        {
            lock (_toasts)
            {
                return _toasts.ToList();
            }
        }
    }

    /// <summary>Shows a message, optionally with a button.</summary>
    /// <param name="message">The message.</param>
    /// <param name="action">A button, such as Undo.</param>
    public void Info(string message, ToastAction? action = null) => Show(message, isError: false, action);

    /// <summary>Shows an error.</summary>
    /// <param name="message">The message.</param>
    public void Error(string message) => Show(message, isError: true, action: null);

    /// <summary>Removes a toast.</summary>
    /// <param name="id">The toast.</param>
    public void Dismiss(int id)
    {
        lock (_toasts)
        {
            if (_toasts.RemoveAll(t => t.Id == id) == 0)
            {
                return;
            }
        }

        Changed?.Invoke();
    }

    private void Show(string message, bool isError, ToastAction? action)
    {
        Toast toast;
        lock (_toasts)
        {
            toast = new Toast(_nextId++, message, isError, action);
            _toasts.Add(toast);
            while (_toasts.Count > 3)
            {
                _toasts.RemoveAt(0);
            }
        }

        Changed?.Invoke();
        var lifetime = isError || action is not null ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(4);
        ITimer? timer = null;
        timer = time.CreateTimer(_ =>
        {
            Dismiss(toast.Id);
            timer?.Dispose();
        }, null, lifetime, Timeout.InfiniteTimeSpan);
    }
}
