using System.Collections.Concurrent;

namespace XtremScan.Services;

/// <summary>
/// WIA exige un thread STA. Les objets COM restent sur ce thread.
/// </summary>
public sealed class ComStaThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public ComStaThread(string name)
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                source.SetResult(action());
            }
            catch (Exception exception)
            {
                source.SetException(exception);
            }
        });

        return source.Task;
    }

    public void Dispose() => _queue.CompleteAdding();

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }
}
