using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// A <c>GMainLoop</c>.
/// </summary>
public sealed class MainLoop : IDisposable
{
    private nint _handle;

    /// <summary>
    /// Creates a main loop for a context.
    /// </summary>
    /// <param name="context">
    /// The context to run, or <see langword="null"/> for the default context.
    /// </param>
    /// <param name="isRunning">
    /// <see langword="true"/> to mark the loop as already running.
    /// </param>
    public MainLoop(MainContext? context = null, bool isRunning = false)
    {
        _handle = GLibNative.MainLoopNew(context?.Handle ?? nint.Zero, isRunning ? 1 : 0);
    }

    /// <summary>
    /// Wraps an existing <c>GMainLoop</c>.
    /// </summary>
    /// <param name="handle">The loop to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// <see cref="Transfer.None"/> when the wrapper has to take its own.
    /// </param>
    public MainLoop(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "A main loop handle must not be null.");
        }

        _handle = transfer == Transfer.Full ? handle : GLibNative.MainLoopRef(handle);
    }

    /// <summary>
    /// Gets the native <c>GMainLoop</c>.
    /// </summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
            return _handle;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the loop is running.
    /// </summary>
    public bool IsRunning => GLibNative.MainLoopIsRunning(Handle) != 0;

    /// <summary>
    /// Gets the context of the loop.
    /// </summary>
    /// <returns>A new wrapper around the context of the loop.</returns>
    public MainContext GetContext() => new(GLibNative.MainLoopGetContext(Handle), Transfer.None);

    /// <summary>
    /// Runs the loop until <see cref="Quit"/> is called.
    /// </summary>
    /// <remarks>
    /// Running a loop also lets the runtime release the native objects whose
    /// wrappers have been collected, see
    /// <see cref="Gst.GObject.Object.DrainPendingReleases"/>.
    /// </remarks>
    public void Run()
    {
        nint handle = Handle;
        Gst.GObject.Object.EnableIdleDrain();
        GLibNative.MainLoopRun(handle);
    }

    /// <summary>
    /// Stops the loop.
    /// </summary>
    public void Quit() => GLibNative.MainLoopQuit(Handle);

    /// <summary>
    /// Releases the reference this wrapper holds.
    /// </summary>
    public void Dispose()
    {
        nint handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero)
        {
            GLibNative.MainLoopUnref(handle);
        }
    }
}
