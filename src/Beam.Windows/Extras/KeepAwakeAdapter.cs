using System.Runtime.InteropServices;
using Beam.Core.Ports;

namespace Beam.Windows.Extras;

/// <summary>
/// Holds the display and system awake while a presentation is active, via
/// <c>SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)</c>.
/// </summary>
/// <remarks>
/// The execution state is a per-thread attribute, and the thread that calls
/// <see cref="Hold"/> may be a short-lived thread-pool thread, so the flags are applied
/// on a dedicated background thread that lives as long as the adapter. The hold is
/// released with <c>ES_CONTINUOUS</c> alone, which clears the previous state.
/// </remarks>
public sealed class KeepAwakeAdapter : IKeepAwake, IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private readonly object _sync = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly ManualResetEventSlim _stop = new(false);
    private volatile uint _desiredFlags;
    private Thread? _thread;
    private bool _disposed;

    /// <inheritdoc />
    public void Hold()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureThread();
            _desiredFlags = EsContinuous | EsSystemRequired | EsDisplayRequired;
            _wake.Set();
        }
    }

    /// <inheritdoc />
    public void Release()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            // ES_CONTINUOUS without the *_REQUIRED flags clears the hold.
            _desiredFlags = EsContinuous;
            _wake.Set();
        }
    }

    private void EnsureThread()
    {
        if (_thread is { IsAlive: true })
        {
            return;
        }

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "Beam.KeepAwake",
        };
        _thread.Start();
    }

    private void Loop()
    {
        uint applied = 0;
        WaitHandle[] handles = { _stop.WaitHandle, _wake.WaitHandle };
        while (WaitHandle.WaitAny(handles) != 0)
        {
            _wake.Reset();
            uint desired = _desiredFlags;
            if (desired != applied)
            {
                SetThreadExecutionState(desired);
                applied = desired;
            }
        }

        // Always clear the hold when the thread exits.
        SetThreadExecutionState(EsContinuous);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stop.Set();
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
        _stop.Dispose();
    }
}
