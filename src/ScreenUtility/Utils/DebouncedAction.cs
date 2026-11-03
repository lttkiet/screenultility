namespace ScreenUtility.Utils;

/// <summary>
/// Coalesces rapid value changes and runs the most recent value after a quiet period.
/// </summary>
public sealed class DebouncedAction<T> : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<T, Task> _action;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private T? _latest;
    private bool _hasValue;

    public DebouncedAction(TimeSpan delay, Func<T, Task> action)
    {
        _delay = delay;
        _action = action;
    }

    public void Trigger(T value)
    {
        CancellationToken token;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            _latest = value;
            _hasValue = true;
        }

        _ = RunAsync(token);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        T value;
        lock (_gate)
        {
            if (token.IsCancellationRequested || !_hasValue)
            {
                return;
            }

            value = _latest!;
            _hasValue = false;
        }

        try
        {
            await _action(value).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation during shutdown or reset.
        }
        catch (Exception ex)
        {
            // Keep background unobserved task exceptions from crashing the process.
            System.Diagnostics.Debug.WriteLine($"DebouncedAction execution failed: {ex}");
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _hasValue = false;
        }
    }

    public void Dispose() => Cancel();
}
