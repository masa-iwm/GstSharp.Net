namespace Gst.Net;

/// <summary>
/// The entry point of the <c>GstNet</c> binding: it initialises GstSharp.Net
/// and makes sure that the types of this assembly are in the type registry.
/// </summary>
/// <remarks>
/// <para>
/// Every binding assembly registers its types from a module initialiser, and
/// the runtime runs a module initialiser before the first <em>call</em> into
/// that assembly, not before the first type of it is merely named. What this
/// module hands to the registry is four GObject classes — the network client
/// clock, the NTP and the PTP clock, and the time provider that serves a clock
/// over the network — together with the boxed <c>GstNetTimePacket</c>.
/// </para>
/// <para>
/// Constructing one of them is a call into this assembly and is therefore safe
/// on its own. What is not is reading a clock back: a pipeline hands its clock
/// out as a <see cref="Gst.Clock"/>, and so does the <c>new-clock</c> message
/// of a bus, so <c>pipeline.GetClock() as NetClientClock</c> is a use of
/// <c>Gst.Net</c> that never executes a line of this assembly. The registry has
/// no entry for the native type, the wrapper is created as the closest base
/// type it does know — <see cref="Gst.SystemClock"/> — and the cast is silently
/// <see langword="null"/>: the failure described under
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#the-gtype-registry">The GType registry</see>.
/// </para>
/// <para>
/// Calling <see cref="Initialize"/> instead of <c>GstSharp.Initialize</c> is a
/// call into this assembly and closes that hole. The registry is rebuilt on the
/// next lookup after a module is added, so the order of the two does not
/// matter; what matters is that the module initialiser runs at all.
/// </para>
/// <para>
/// <c>GstSharp.Initialize</c> also sweeps the assemblies that are loaded and
/// runs their module initialisers, and it keeps doing so for assemblies that
/// are loaded later, so an application that never names this class is covered
/// as well. Calling this one is the deterministic way to say it.
/// </para>
/// </remarks>
public static class GstNet
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
