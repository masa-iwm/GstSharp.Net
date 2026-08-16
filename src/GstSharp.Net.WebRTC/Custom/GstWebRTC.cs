namespace Gst.WebRTC;

/// <summary>
/// The entry point of the <c>GstWebRTC</c> binding: it initialises GstSharp.Net
/// and makes sure that the types of this assembly are in the type registry.
/// </summary>
/// <remarks>
/// <para>
/// Every binding assembly registers its types from a module initialiser, and
/// the runtime runs a module initialiser before the first <em>call</em> into
/// that assembly, not before the first type of it is merely named. What this
/// module hands to the registry is the nine classes a <c>webrtcbin</c> hands
/// out — the ICE and DTLS transports, the ICE stream and its owner, the data
/// channel, the RTP sender, receiver and transceiver and the SCTP transport —
/// together with the four boxed records around them, the session description
/// among them.
/// </para>
/// <para>
/// That makes the hole easy to fall into here. Nothing in this module is
/// created by an application: every one of those wrappers arrives from a
/// property or a signal of an element that lives in another assembly, and
/// reading a property is a call into <em>that</em> assembly. An application
/// that never calls a static of this one therefore never runs its module
/// initialiser, the registry has no entry to build the wrappers from, and what
/// arrives is the closest type it does know — a bare <c>Gst.Object</c> instead
/// of a transceiver. This is the failure mode §2.1 of the acceptance
/// requirements is about.
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
public static class GstWebRTC
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
