namespace Gst.Controller;

/// <summary>
/// The entry point of the <c>GstController</c> binding: it initialises
/// GstSharp.Net and makes sure that the types of this assembly are in the type
/// registry.
/// </summary>
/// <remarks>
/// <para>
/// Every binding module registers its types from a module initialiser, and the
/// runtime runs a module initialiser before the first <em>call</em> into that
/// assembly, not before one of its types is merely named. An application that
/// reaches for this assembly only in a cast therefore never executes a line of
/// it, and the wrapper of a control source is built as the closest type the
/// registry does know — the failure described under
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#the-gtype-registry">The GType registry</see>.
/// </para>
/// <para>
/// Calling <see cref="Initialize"/> instead of <c>GstSharp.Initialize</c> is a
/// call into this assembly and closes that hole. Reaching for
/// <see cref="InterpolationControlSource.New"/> is one as well, so an
/// application that only ever builds its control sources here needs nothing
/// extra.
/// </para>
/// </remarks>
public static class GstController
{
    /// <summary>
    /// Loads the native libraries, initialises GStreamer and puts the types of
    /// this assembly into the type registry.
    /// </summary>
    /// <param name="options">
    /// Where the native libraries are and how GStreamer should be initialised,
    /// or <see langword="null"/> for the defaults.
    /// </param>
    /// <remarks>
    /// This forwards to <c>GstSharp.Initialize</c> and is idempotent in the
    /// same way: after the first call, a call with <see langword="null"/>
    /// options does nothing but register this module, and options that
    /// contradict the first call are refused.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The options conflict with the ones of the first call.
    /// </exception>
    /// <exception cref="Gst.Interop.GstNativeLoadException">
    /// The native libraries could not be found.
    /// </exception>
    /// <exception cref="Gst.GLib.GException">GStreamer refused to initialise.</exception>
    public static void Initialize(GstSharpOptions? options = null) =>
        global::GstSharp.Initialize(options);
}
