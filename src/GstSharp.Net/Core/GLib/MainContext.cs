using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// A <c>GMainContext</c>: the set of event sources that a main loop watches.
/// </summary>
public sealed class MainContext : IDisposable
{
    private static readonly Lazy<MainContext> DefaultContext =
        new(static () => new MainContext(GLibNative.MainContextDefault(), Transfer.None), isThreadSafe: true);

    private nint _handle;

    /// <summary>
    /// Wraps an existing <c>GMainContext</c>.
    /// </summary>
    /// <param name="handle">The context to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// <see cref="Transfer.None"/> when the wrapper has to take its own.
    /// </param>
    public MainContext(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "A main context handle must not be null.");
        }

        _handle = transfer == Transfer.Full ? handle : GLibNative.MainContextRef(handle);
    }

    /// <summary>
    /// Gets the default main context of the process. It must not be disposed.
    /// </summary>
    public static MainContext Default => DefaultContext.Value;

    /// <summary>
    /// Gets the thread default main context, or <see langword="null"/> when the
    /// calling thread uses the default one.
    /// </summary>
    public static MainContext? ThreadDefault
    {
        get
        {
            nint handle = GLibNative.MainContextGetThreadDefault();
            return handle == nint.Zero ? null : new MainContext(handle, Transfer.None);
        }
    }

    /// <summary>
    /// Gets the native <c>GMainContext</c>.
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
    /// Gets a value indicating whether the context has sources that are ready
    /// to run.
    /// </summary>
    public bool Pending => GLibNative.MainContextPending(Handle) != 0;

    /// <summary>
    /// Gets a value indicating whether the calling thread owns the context,
    /// which is the case while it runs the loop of that context.
    /// </summary>
    public bool IsOwner => GLibNative.MainContextIsOwner(Handle) != 0;

    /// <summary>
    /// Creates a new main context.
    /// </summary>
    /// <returns>The new context.</returns>
    public static MainContext New() => new(GLibNative.MainContextNew(), Transfer.Full);

    /// <summary>
    /// Runs a single iteration of the context.
    /// </summary>
    /// <param name="mayBlock">
    /// <see langword="true"/> to wait until a source becomes ready.
    /// </param>
    /// <returns><see langword="true"/> when a source was dispatched.</returns>
    public bool Iteration(bool mayBlock) => GLibNative.MainContextIteration(Handle, mayBlock ? 1 : 0) != 0;

    /// <summary>
    /// Makes this context the thread default one.
    /// </summary>
    public void PushThreadDefault() => GLibNative.MainContextPushThreadDefault(Handle);

    /// <summary>
    /// Undoes the most recent <see cref="PushThreadDefault"/>.
    /// </summary>
    public void PopThreadDefault() => GLibNative.MainContextPopThreadDefault(Handle);

    /// <summary>
    /// Releases the reference this wrapper holds.
    /// </summary>
    public void Dispose()
    {
        nint handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero)
        {
            GLibNative.MainContextUnref(handle);
        }
    }
}
