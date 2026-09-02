using System.Runtime.InteropServices;

namespace Gst.Play;

/// <content>
/// The three factories of the adapter. The C object stores the play without
/// taking a reference of it, so the wrapper is what has to keep it alive.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_play_signal_adapter_new</c> assigns <c>self-&gt;play = play</c> and
/// refs nothing but the bus, and <c>gst_play_signal_adapter_get_play</c> hands
/// that field back as transfer none. Nothing on the C side therefore keeps the
/// play alive for the adapter, and an adapter that outlives the last reference
/// of its play holds a pointer to freed memory. Each factory below keeps the
/// <see cref="Gst.Play.Play"/> wrapper it was given on the adapter wrapper,
/// which holds the reference C does not, and <see cref="GetPlay"/> answers that
/// wrapper instead of reading the C field.
/// </para>
/// </remarks>
public unsafe partial class PlaySignalAdapter
{
    /// <summary>
    /// The play the adapter watches. C keeps no reference of it, so this field
    /// is what does.
    /// </summary>
    private Gst.Play.Play? _play;

    /// <summary>
    /// Creates an adapter that emits its signals from the main context that is
    /// the thread-default one at this moment.
    /// </summary>
    /// <param name="play">The play whose API bus to watch.</param>
    /// <returns>The adapter, which the caller owns.</returns>
    /// <remarks>
    /// <para>
    /// <b>The signals only fire while that context is iterated.</b> The bus
    /// watch is attached to <c>g_main_context_get_thread_default()</c> as it is
    /// when this is called, which in a .NET application that runs no GLib main
    /// loop is the global default context that nothing ever iterates: every
    /// signal of the adapter then stays silent for the whole life of the
    /// process, with no error anywhere. Use
    /// <see cref="NewSyncEmit(Play)"/>, or a context this application iterates
    /// itself through <see cref="NewWithMainContext(Play, Gst.GLib.MainContext?)"/>,
    /// or read <see cref="Play.GetMessageBus"/> directly.
    /// </para>
    /// <para>
    /// Disposing the adapter sets the API bus of the play flushing, which every
    /// other consumer of that bus sees.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="play"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="play"/> was disposed.</exception>
    public static Gst.Play.PlaySignalAdapter New(Gst.Play.Play play)
    {
        ArgumentNullException.ThrowIfNull(play);

        nint nativeResult = GstPlaySignalAdapterNewNative(play.Handle);
        GC.KeepAlive(play);
        return Adopt(nativeResult, play, "gst_play_signal_adapter_new");
    }

    /// <summary>
    /// Creates an adapter that emits its signals from a main context of the
    /// caller's choosing.
    /// </summary>
    /// <param name="play">The play whose API bus to watch.</param>
    /// <param name="context">
    /// The context the bus watch is attached to, or <see langword="null"/> for
    /// the thread-default one, which is what <see cref="New(Play)"/> uses. The
    /// context stays the caller's.
    /// </param>
    /// <returns>The adapter, which the caller owns.</returns>
    /// <remarks>
    /// The signals fire on whichever thread iterates <paramref name="context"/>,
    /// and only while it is iterated; the warning on <see cref="New(Play)"/>
    /// applies to a context nobody runs. The <c>.gir</c> of this module marks
    /// the parameter neither nullable nor optional, but the C function only
    /// refuses <see langword="null"/> in
    /// <c>gst_play_signal_adapter_new_with_main_context</c> itself and
    /// <see cref="New(Play)"/> is exactly the call with the thread-default
    /// context, so <see langword="null"/> is answered here rather than passed
    /// on.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="play"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="play"/> was disposed.</exception>
    public static Gst.Play.PlaySignalAdapter NewWithMainContext(
        Gst.Play.Play play,
        Gst.GLib.MainContext? context)
    {
        ArgumentNullException.ThrowIfNull(play);

        if (context is null)
        {
            return New(play);
        }

        nint nativeResult = GstPlaySignalAdapterNewWithMainContextNative(play.Handle, context.Handle);
        GC.KeepAlive(play);
        GC.KeepAlive(context);
        return Adopt(nativeResult, play, "gst_play_signal_adapter_new_with_main_context");
    }

    /// <summary>
    /// Creates an adapter that emits its signals synchronously, on the thread
    /// that posted the message.
    /// </summary>
    /// <param name="play">The play whose API bus to watch.</param>
    /// <returns>The adapter, which the caller owns.</returns>
    /// <remarks>
    /// <para>
    /// <b>This adapter takes the one sync handler of the API bus and drops
    /// every message it handles.</b> Nothing reaches the queue of the bus
    /// afterwards, so it is mutually exclusive with a poll of
    /// <see cref="Play.GetMessageBus"/> and with any other sync handler on that
    /// bus. Disposing it sets the bus flushing.
    /// </para>
    /// <para>
    /// The signals fire on the internal thread of the play — that is not the
    /// thread that built the play — with two exceptions:
    /// <c>volume-changed</c> and <c>mute-changed</c> are emitted from whichever
    /// thread raised the <c>notify</c> of the underlying pipeline, which for
    /// <see cref="Play.SetVolume(double)"/> and
    /// <see cref="Play.SetMute(bool)"/> is the thread that called them. A
    /// handler that disposes the play from the internal thread deadlocks, as
    /// the disposal joins that thread.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="play"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="play"/> was disposed.</exception>
    public static Gst.Play.PlaySignalAdapter NewSyncEmit(Gst.Play.Play play)
    {
        ArgumentNullException.ThrowIfNull(play);

        nint nativeResult = GstPlaySignalAdapterNewSyncEmitNative(play.Handle);
        GC.KeepAlive(play);
        return Adopt(nativeResult, play, "gst_play_signal_adapter_new_sync_emit");
    }

    /// <summary>
    /// Drops the reference of the play and releases the adapter.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when the call comes from <c>Dispose()</c>,
    /// <see langword="false"/> when it comes from the finalizer.
    /// </param>
    /// <remarks>
    /// A disposed adapter watches nothing, so the reference that kept the play
    /// alive for it is given up here rather than whenever the wrapper of the
    /// adapter is collected. It is given up after the release, not before: the
    /// C dispose destroys the bus watch and sets the API bus of the play
    /// flushing, and holding the play across it is what keeps that bus — which
    /// the play owns — alive while it runs.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Interlocked.Exchange(ref _play, null);
    }

    /// <summary>
    /// Gets the play this adapter watches.
    /// </summary>
    /// <returns>The play the adapter was built for.</returns>
    /// <remarks>
    /// This is <c>gst_play_signal_adapter_get_play</c>, answered from managed
    /// state rather than from the C field. The C adapter stores the play
    /// without taking a reference of it, so that field dangles once the play
    /// has been disposed and the imported call would build a wrapper by
    /// referencing freed memory. What is handed back is the very
    /// <see cref="Play"/> the factory was given, which is also the reference
    /// that keeps the play alive for as long as this adapter is undisposed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// This adapter was disposed, and the play it watched is no longer its to
    /// hand out.
    /// </exception>
    public Gst.Play.Play GetPlay() =>
        Volatile.Read(ref _play) ?? throw new ObjectDisposedException(nameof(PlaySignalAdapter));

    /// <summary>The play this adapter watches.</summary>
    /// <exception cref="ObjectDisposedException">This adapter was disposed.</exception>
    public Gst.Play.Play Play => GetPlay();

    /// <summary>
    /// Wraps what a factory answered and puts the play on the wrapper.
    /// </summary>
    /// <param name="handle">The adapter the C function built.</param>
    /// <param name="play">The play it was built for.</param>
    /// <param name="entryPoint">The C function, for the exception.</param>
    /// <returns>The wrapper of the adapter.</returns>
    private static Gst.Play.PlaySignalAdapter Adopt(nint handle, Gst.Play.Play play, string entryPoint)
    {
        Gst.Play.PlaySignalAdapter adapter =
            Gst.GObject.Object.FromNative<Gst.Play.PlaySignalAdapter>(handle, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException($"{entryPoint} returned no value.");

        adapter._play = play;
        return adapter;
    }

    /// <summary>The <c>gst_play_signal_adapter_new</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_signal_adapter_new")]
    private static partial nint GstPlaySignalAdapterNewNative(nint play);

    /// <summary>The <c>gst_play_signal_adapter_new_sync_emit</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_signal_adapter_new_sync_emit")]
    private static partial nint GstPlaySignalAdapterNewSyncEmitNative(nint play);

    /// <summary>The <c>gst_play_signal_adapter_new_with_main_context</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_signal_adapter_new_with_main_context")]
    private static partial nint GstPlaySignalAdapterNewWithMainContextNative(nint play, nint context);
}
