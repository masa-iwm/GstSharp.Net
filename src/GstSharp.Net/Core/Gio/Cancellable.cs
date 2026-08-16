using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// A <c>GCancellable</c>: the token that Gio operations watch to find out that
/// the caller has lost interest in them.
/// </summary>
/// <remarks>
/// The wrapper covers the four operations that make the object usable from
/// managed code. It is deliberately not bridged to
/// <see cref="System.Threading.CancellationToken"/>: linking the two needs
/// <c>g_cancellable_connect</c> and a disposal protocol of its own, which
/// belongs with the asynchronous surface rather than with the type.
/// </remarks>
public partial class Cancellable : Gst.GObject.Object
{
    /// <summary>
    /// Wraps a native <c>GCancellable</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal Cancellable(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the token has been cancelled.
    /// </summary>
    public bool IsCancelled
    {
        get
        {
            int result = GCancellableIsCancelled(Handle);
            GC.KeepAlive(this);
            return result != 0;
        }
    }

    /// <summary>
    /// Creates a token that has not been cancelled.
    /// </summary>
    /// <returns>The new token.</returns>
    public static Cancellable New()
    {
        nint nativeResult = GCancellableNew();
        return FromNative<Cancellable>(nativeResult, Transfer.Full)
            ?? throw new InvalidOperationException("g_cancellable_new returned no value.");
    }

    /// <summary>
    /// Cancels the token, which makes every operation that watches it fail with
    /// <c>G_IO_ERROR_CANCELLED</c>.
    /// </summary>
    /// <remarks>
    /// Cancelling a token that is already cancelled does nothing. The call is
    /// safe from any thread.
    /// </remarks>
    public void Cancel()
    {
        GCancellableCancel(Handle);
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Puts a cancelled token back into its unused state, so that it can be
    /// reused.
    /// </summary>
    /// <remarks>
    /// GLib requires that no operation is still using the token when it is
    /// reset; resetting one that is in use is a programming error that GLib
    /// reports with a critical warning.
    /// </remarks>
    public void Reset()
    {
        GCancellableReset(Handle);
        GC.KeepAlive(this);
    }

    /// <summary>Returns the <c>GType</c> that GObject registered <c>GCancellable</c> under.</summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("Gio", EntryPoint = "g_cancellable_get_type")]
    internal static partial nuint GetGType();

    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new Cancellable(handle, transfer);

    /// <summary>The <c>g_cancellable_new</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_cancellable_new")]
    private static partial nint GCancellableNew();

    /// <summary>The <c>g_cancellable_cancel</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_cancellable_cancel")]
    private static partial void GCancellableCancel(nint cancellable);

    /// <summary>The <c>g_cancellable_is_cancelled</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_cancellable_is_cancelled")]
    private static partial int GCancellableIsCancelled(nint cancellable);

    /// <summary>The <c>g_cancellable_reset</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_cancellable_reset")]
    private static partial void GCancellableReset(nint cancellable);
}
