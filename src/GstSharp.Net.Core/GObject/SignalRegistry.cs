using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// Connects and disconnects GObject signal handlers.
/// </summary>
/// <remarks>
/// The handler is always an unmanaged function pointer, normally the address of
/// a static method that is marked with
/// <see cref="UnmanagedCallersOnlyAttribute"/>, and its managed state travels
/// as a <see cref="CallbackHandle"/>. GObject releases the state through the
/// closure notification of <see cref="CallbackHandle.ClosureNotify"/> when the
/// handler is disconnected or the object dies, so a handler that is never
/// disconnected explicitly still cleans up.
/// </remarks>
public static class SignalRegistry
{
    /// <summary>
    /// Connects a handler to a signal.
    /// </summary>
    /// <param name="instance">The object to connect to.</param>
    /// <param name="detailedSignal">
    /// The name of the signal, optionally with a detail, for example
    /// <c>notify::uri</c>.
    /// </param>
    /// <param name="callback">The address of the unmanaged callback.</param>
    /// <param name="state">The managed state of the callback.</param>
    /// <param name="after">
    /// <see langword="true"/> to run the handler after the default one.
    /// </param>
    /// <returns>The identifier of the handler, which is never zero.</returns>
    public static unsafe ulong Connect(
        nint instance,
        string detailedSignal,
        nint callback,
        CallbackHandle state,
        bool after)
    {
        ArgumentException.ThrowIfNullOrEmpty(detailedSignal);

        if (instance == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(instance), "An instance handle must not be null.");
        }

        if (callback == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(callback), "A callback address must not be null.");
        }

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(detailedSignal, buffer);

        CULong id = GObjectNative.SignalConnectData(
            instance,
            scope.Pointer,
            callback,
            state.UserData,
            CallbackHandle.ClosureNotify,
            after ? GObjectNative.ConnectAfter : 0);

        return id.Value;
    }

    /// <summary>
    /// Disconnects a handler.
    /// </summary>
    /// <param name="instance">The object the handler is connected to.</param>
    /// <param name="handlerId">The identifier of the handler.</param>
    public static void Disconnect(nint instance, ulong handlerId)
    {
        if (instance == nint.Zero || handlerId == 0)
        {
            return;
        }

        CULong id = new((nuint)handlerId);
        if (GObjectNative.SignalHandlerIsConnected(instance, id) != 0)
        {
            GObjectNative.SignalHandlerDisconnect(instance, id);
        }
    }

    /// <summary>
    /// Tests whether a handler is still connected.
    /// </summary>
    /// <param name="instance">The object the handler was connected to.</param>
    /// <param name="handlerId">The identifier of the handler.</param>
    /// <returns><see langword="true"/> when the handler is still connected.</returns>
    public static bool IsConnected(nint instance, ulong handlerId) =>
        instance != nint.Zero &&
        handlerId != 0 &&
        GObjectNative.SignalHandlerIsConnected(instance, new CULong((nuint)handlerId)) != 0;
}
