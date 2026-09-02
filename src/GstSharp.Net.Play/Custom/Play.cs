using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Play;

/// <content>
/// The four members of <see cref="Play"/> the generator cannot write: the
/// constructor, the configuration setter whose C half leaks on refusal, the
/// enumeration of the visualizations, and the flush of the API bus that
/// disposal has to do before it lets go.
/// </content>
public unsafe partial class Play
{
    /// <summary>
    /// Creates a play, optionally with a video renderer that decides where the
    /// video goes.
    /// </summary>
    /// <param name="videoRenderer">
    /// The renderer that creates the video sink, or <see langword="null"/> for
    /// the default handling, which is a video sink the pipeline picks itself.
    /// The play takes a reference of its own, so the renderer stays the
    /// caller's to keep and to dispose, and
    /// <see cref="PlayVideoOverlayVideoRenderer.Expose"/> and the render
    /// rectangle of an overlay renderer stay reachable while the play runs.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_play_new</c>. The C function takes the reference of its
    /// caller over — it hands the renderer to <c>g_object_new</c>, which
    /// duplicates it, and then drops the one it was given — so the binding
    /// raises one reference before the call and leaves the wrapper of the
    /// caller holding what it held.
    /// </para>
    /// <para>
    /// The first play of a process initialises GStreamer itself, so the library
    /// is usable without <c>GstSharp.Initialize</c>; the type registry is not,
    /// so an application still calls it.
    /// </para>
    /// <para>
    /// A play runs a main loop on a thread of its own and posts every event as
    /// a message on the bus of <see cref="GetMessageBus"/>. Read that bus, or
    /// connect a <see cref="PlaySignalAdapter"/> to it, and see the remarks on
    /// <see cref="Dispose(bool)"/> before the play is let go.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// <paramref name="videoRenderer"/> was disposed.
    /// </exception>
    public Play(Gst.Play.IPlayVideoRenderer? videoRenderer = null)
        : base(NewNative(videoRenderer), Gst.Interop.Transfer.Full)
    {
    }

    /// <summary>
    /// Sets the configuration of the play, which only a stopped play accepts.
    /// </summary>
    /// <param name="config">
    /// The configuration to install, as <see cref="GetConfig"/> answers it and
    /// the <c>ConfigSet*</c> members write it. It stays the caller's: the call
    /// is handed a copy of it, and the copy is released again when the play
    /// refuses it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the configuration was installed,
    /// <see langword="false"/> when the play was not in
    /// <see cref="PlayState.Stopped"/>, in which case the active configuration
    /// is unchanged.
    /// </returns>
    /// <remarks>
    /// This is <c>gst_play_set_config</c>, which is documented as taking
    /// ownership of the structure and only does so on success:
    /// <c>gstplay.c</c> answers <c>FALSE</c> for a play that is not stopped
    /// without freeing what it was given. The binding hands over a copy of the
    /// caller's structure and frees that copy itself on the refusal, so neither
    /// half leaks and the caller keeps exactly what it passed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="config"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or <paramref name="config"/> was disposed.
    /// </exception>
    public bool SetConfig(Gst.Structure config)
    {
        ArgumentNullException.ThrowIfNull(config);

        nint instanceHandle = Handle;
        nuint configType = config.BoxedType.Value;
        nint configOwned = GObjectNative.BoxedCopy(configType, config.Handle);
        int nativeResult = GstPlaySetConfigNative(instanceHandle, configOwned);
        GC.KeepAlive(this);
        GC.KeepAlive(config);

        if (nativeResult == 0)
        {
            // The C function only honours the documented ownership transfer on
            // success. Freeing the copy here is what keeps the refusal from
            // leaking a structure nobody can reach any more.
            GObjectNative.BoxedFree(configType, configOwned);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Lists the visualizations the installed plugins offer, by the name
    /// <see cref="SetVisualization(string?)"/> takes.
    /// </summary>
    /// <returns>
    /// The visualizations, which the caller owns and should dispose, or an
    /// empty list when no plugin registers one.
    /// </returns>
    /// <remarks>
    /// This is <c>gst_play_visualizations_get</c> together with its
    /// <c>gst_play_visualizations_free</c>. The C function answers a
    /// <see langword="null"/> terminated array of individually allocated
    /// descriptors that only that free may release, so the member copies every
    /// element into a wrapper of its own and frees the array before it returns:
    /// nothing the caller holds points into it. The list is a snapshot of the
    /// registry as it was when the call was made.
    /// </remarks>
    public static System.Collections.Generic.IReadOnlyList<Gst.Play.PlayVisualization> GetVisualizations()
    {
        nint array = GstPlayVisualizationsGetNative();
        if (array == nint.Zero)
        {
            return [];
        }

        List<Gst.Play.PlayVisualization> visualizations = [];
        try
        {
            nint* element = (nint*)array;
            while (*element != nint.Zero)
            {
                // Transfer.None is a g_boxed_copy through the copy function the
                // boxed type was registered with, which is
                // gst_play_visualization_copy: the wrapper owns a descriptor of
                // its own and the array stays whole for the free below.
                visualizations.Add(Gst.Play.PlayVisualization.FromNative(*element, Gst.Interop.Transfer.None)!);
                element++;
            }
        }
        catch
        {
            foreach (Gst.Play.PlayVisualization made in visualizations)
            {
                made.Dispose();
            }

            GstPlayVisualizationsFreeNative(array);
            throw;
        }

        GstPlayVisualizationsFreeNative(array);
        return visualizations;
    }

    /// <summary>
    /// Sets the API bus flushing and releases the play.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when the call comes from <c>Dispose()</c>,
    /// <see langword="false"/> when it comes from the finalizer.
    /// </param>
    /// <remarks>
    /// <para>
    /// Every message the play posts carries the play as its source, so a
    /// message that is still queued on the API bus holds a reference of the
    /// play, and the play holds the bus: an unread bus is a reference cycle
    /// that keeps the play alive after the last wrapper let go of it, and the
    /// internal thread with it. The C documentation says to set the bus
    /// flushing before dropping the last reference, and this is where the
    /// binding does it.
    /// </para>
    /// <para>
    /// <b>A loop of its own that pops <see cref="GetMessageBus"/> has to stop
    /// before the play is disposed.</b> After this, that bus answers nothing: a
    /// flushing bus drops what it holds and refuses what is posted next.
    /// </para>
    /// <para>
    /// <b>Stop the play and wait until it reports
    /// <see cref="PlayState.Stopped"/> before disposing it.</b>
    /// <see cref="Stop"/> only queues the stop on the thread of the play, and
    /// GStreamer 1.28 queues it without a reference of the play, while the
    /// messages that thread posts do hold one: a play that is disposed while
    /// its thread is still working can therefore have its last reference
    /// dropped by that thread and be finalised underneath its own running
    /// dispatch, which crashes inside <c>libgstplay</c>. Wait for the
    /// <c>state-changed</c> message of the API bus that carries
    /// <see cref="PlayState.Stopped"/>, or for the state change an adapter
    /// emits, and dispose after it. A play that reported
    /// <see cref="PlayState.Stopped"/> already, which is what every play does
    /// after end of stream and after an error, does not report it again, so
    /// what an application waits for is the last state it saw rather than a
    /// fresh message. This is an upstream limitation and not a contract of the
    /// binding, and disposal does not wait here because nothing in the C API
    /// joins the thread of a play and the barrier that is left — polling the
    /// state of the pipeline — would block a disposal to work around a defect
    /// of the library this binds:
    /// see
    /// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#a-play-and-its-api-bus">A
    /// play and its API bus</see>.
    /// </para>
    /// <para>
    /// <b>Dispose every <see cref="PlaySignalAdapter"/> of a play before the
    /// play itself.</b> The C adapter stores the play without referencing it,
    /// so an adapter that outlives its play holds a dangling pointer; the
    /// binding answers <see cref="PlaySignalAdapter.GetPlay"/> from its own
    /// field rather than from that pointer, but the bus watch of the adapter
    /// still runs against an object that is gone.
    /// </para>
    /// <para>
    /// The finalizer path does none of it. A finalizer must not call into
    /// native code — the release of the object itself is queued for the same
    /// reason — so a play that is collected without being disposed leaves its
    /// bus as it is, exactly as the C caller that forgets the flush does.
    /// </para>
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            nint bus = GstPlayGetMessageBus(Handle);
            if (bus != nint.Zero)
            {
                GstBusSetFlushing(bus, 1);
                GObjectNative.ObjectUnref(bus);
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Builds the native play the constructor wraps.
    /// </summary>
    /// <param name="videoRenderer">The renderer to hand over, or <see langword="null"/>.</param>
    /// <returns>The new play, which the caller owns.</returns>
    private static nint NewNative(Gst.Play.IPlayVideoRenderer? videoRenderer)
    {
        nint renderer = videoRenderer?.Handle ?? nint.Zero;
        if (renderer != nint.Zero)
        {
            // gst_play_new drops one reference of the renderer, which is the
            // one its C caller held. The wrapper still needs the one it holds,
            // so the call is given a reference of its own.
            GObjectNative.ObjectRef(renderer);
        }

        nint handle = GstPlayNewNative(renderer);
        GC.KeepAlive(videoRenderer);
        return handle;
    }

    /// <summary>The <c>gst_play_new</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_new")]
    private static partial nint GstPlayNewNative(nint videoRenderer);

    /// <summary>The <c>gst_play_set_config</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_set_config")]
    private static partial int GstPlaySetConfigNative(nint play, nint config);

    /// <summary>The <c>gst_play_visualizations_get</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_visualizations_get")]
    private static partial nint GstPlayVisualizationsGetNative();

    /// <summary>The <c>gst_play_visualizations_free</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_visualizations_free")]
    private static partial void GstPlayVisualizationsFreeNative(nint viss);

    /// <summary>The <c>gst_bus_set_flushing</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_bus_set_flushing")]
    private static partial void GstBusSetFlushing(nint bus, int flushing);
}
