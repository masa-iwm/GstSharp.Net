using System.Runtime.InteropServices;

namespace Gst.Play;

/// <content>
/// The two constructors of the overlay renderer. Both C factories are declared
/// as returning the <c>GstPlayVideoRenderer</c> interface rather than the class
/// they build, and the planner refuses a returned handle typed as an interface
/// it has no wrapper for, so the pair is written here.
/// </content>
public unsafe partial class PlayVideoOverlayVideoRenderer
{
    /// <summary>
    /// Creates a renderer that draws into a window of the platform.
    /// </summary>
    /// <param name="windowHandle">
    /// The platform handle of the window to draw into — an <c>HWND</c> on
    /// Windows, an <c>XID</c> on X11, a <c>NSView</c> on macOS — or
    /// <see cref="nint.Zero"/> to set it later with
    /// <see cref="SetWindowHandle(nint)"/>.
    /// </param>
    /// <remarks>
    /// This is <c>gst_play_video_overlay_video_renderer_new</c>. The video sink
    /// is the one the pipeline picks; use
    /// <see cref="PlayVideoOverlayVideoRenderer(nint, Gst.Element?)"/> to name
    /// one. Hand the renderer to
    /// <see cref="Play(IPlayVideoRenderer?)"/>, which takes a reference of its
    /// own and leaves this wrapper the caller's.
    /// </remarks>
    public PlayVideoOverlayVideoRenderer(nint windowHandle)
        : base(NewNative(windowHandle), Gst.Interop.Transfer.Full)
    {
    }

    /// <summary>
    /// Creates a renderer that draws into a window of the platform through a
    /// video sink of the caller's choosing.
    /// </summary>
    /// <param name="windowHandle">
    /// The platform handle of the window to draw into, or
    /// <see cref="nint.Zero"/> to set it later with
    /// <see cref="SetWindowHandle(nint)"/>.
    /// </param>
    /// <param name="videoSink">
    /// The video sink to render with, which has to implement
    /// <c>GstVideoOverlay</c>, or <see langword="null"/> to let the pipeline
    /// pick one, which is what
    /// <see cref="PlayVideoOverlayVideoRenderer(nint)"/> does. The renderer
    /// takes a reference of its own, so the element stays the caller's.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_play_video_overlay_video_renderer_new_with_sink</c>. The
    /// C documentation calls the sink <c>(transfer floating)</c> and the
    /// implementation does <c>gst_object_ref_sink</c> on it, which on the
    /// already sunk element every wrapper of this binding holds is a plain
    /// reference: the argument is borrowed, exactly as the <c>.gir</c> says.
    /// That set_property has no null check, so a <see langword="null"/> sink
    /// is answered by calling the one argument factory instead of handing the
    /// null on.
    /// </para>
    /// <para>
    /// <b>The sink is a construction argument.</b> The <c>video-sink</c>
    /// property that <see cref="VideoSink"/> binds writes the field without
    /// releasing what was there, so a renderer that is given a second sink
    /// leaks the first one; build a new renderer instead.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException"><paramref name="videoSink"/> was disposed.</exception>
    public PlayVideoOverlayVideoRenderer(nint windowHandle, Gst.Element? videoSink)
        : base(NewWithSinkNative(windowHandle, videoSink), Gst.Interop.Transfer.Full)
    {
    }

    /// <summary>
    /// Gets the video sink the renderer draws with, or <see langword="null"/>
    /// when it was built without one and the pipeline picks its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>video-sink</c> property is read only here although the C class
    /// declares it <c>G_PARAM_READWRITE</c>. Its C setter assigns
    /// <c>gst_object_ref_sink (g_value_get_object (value))</c> over the field
    /// and releases neither what was there nor anything else, so every write
    /// after the first leaks an element; the sink is a construction argument
    /// and the overlay skip of <c>fixups.json</c> is what keeps the write half
    /// off the surface. Build a new renderer to use another sink.
    /// </para>
    /// <para>
    /// What is handed back is the interned wrapper of the element, which the
    /// binding keeps: it is not the reader's to dispose, and the renderer holds
    /// the reference that keeps it alive.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Element? VideoSink
    {
        get
        {
            using Gst.GObject.Value holder = GetProperty("video-sink");
            return (Gst.Element?)holder.GetObject();
        }
    }

    /// <summary>
    /// Builds the native renderer of the first constructor.
    /// </summary>
    /// <param name="windowHandle">The window to draw into.</param>
    /// <returns>The new renderer, which the caller owns.</returns>
    private static nint NewNative(nint windowHandle) =>
        GstPlayVideoOverlayVideoRendererNewNative(windowHandle);

    /// <summary>
    /// Builds the native renderer of the second constructor.
    /// </summary>
    /// <param name="windowHandle">The window to draw into.</param>
    /// <param name="videoSink">The sink to render with, or <see langword="null"/>.</param>
    /// <returns>The new renderer, which the caller owns.</returns>
    private static nint NewWithSinkNative(nint windowHandle, Gst.Element? videoSink)
    {
        if (videoSink is null)
        {
            // The C function hands its sink straight to g_object_new, whose
            // set_property does gst_object_ref_sink on it without a null check,
            // so a null sink is a GLib critical there. The one argument factory
            // is the same call without that property, and video-sink carries no
            // G_PARAM_CONSTRUCT, so it is never written by it.
            return GstPlayVideoOverlayVideoRendererNewNative(windowHandle);
        }

        nint handle = GstPlayVideoOverlayVideoRendererNewWithSinkNative(windowHandle, videoSink.Handle);
        GC.KeepAlive(videoSink);
        return handle;
    }

    /// <summary>The <c>gst_play_video_overlay_video_renderer_new</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_video_overlay_video_renderer_new")]
    private static partial nint GstPlayVideoOverlayVideoRendererNewNative(nint windowHandle);

    /// <summary>The <c>gst_play_video_overlay_video_renderer_new_with_sink</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_video_overlay_video_renderer_new_with_sink")]
    private static partial nint GstPlayVideoOverlayVideoRendererNewWithSinkNative(nint windowHandle, nint videoSink);
}
