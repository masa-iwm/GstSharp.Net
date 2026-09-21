// The control the video is rendered into. It is the whole of what the toolkit
// contributes to this tutorial: a rectangle of the window that owns a real
// native window of its own, whose handle GStreamer understands.
using Avalonia.Controls;
using Avalonia.Platform;

/// <summary>
/// An area of the window that is backed by a native child window — an HWND on
/// Windows, an X11 window on Linux, an NSView on macOS — and reports its handle
/// as soon as it has one.
/// </summary>
/// <remarks>
/// <para>
/// This is what replaces the GtkWidget the C original takes off the
/// <c>gtkglsink</c> element. There the sink owns the widget and the application
/// packs it; here the application owns the surface and the sink is told about
/// it, which is the direction <c>GstVideoOverlay</c> works in.
/// </para>
/// <para>
/// Both callbacks run on the UI thread, and the event is raised there. The
/// handle has to exist before the pipeline reaches PLAYING, which is why the
/// window starts the pipeline from this event rather than in its constructor.
/// </para>
/// </remarks>
internal sealed class VideoHost : NativeControlHost
{
    /// <summary>
    /// Raised with the platform handle once the control has one, and with
    /// <see langword="null"/> when that handle is about to go away.
    /// </summary>
    internal event Action<IPlatformHandle?>? HandleChanged;

    /// <inheritdoc/>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        IPlatformHandle handle = base.CreateNativeControlCore(parent);
        HandleChanged?.Invoke(handle);
        return handle;
    }

    /// <inheritdoc/>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // The control can be detached and attached again — moved to another
        // top level, for instance — so the handle is withdrawn here and the
        // next CreateNativeControlCore offers a new one. A sink that keeps
        // rendering into a destroyed window is the bug this prevents.
        HandleChanged?.Invoke(null);
        base.DestroyNativeControlCore(control);
    }
}
