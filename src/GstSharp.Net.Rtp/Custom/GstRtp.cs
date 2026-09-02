namespace Gst.Rtp;

/// <summary>
/// The entry point of the <c>GstRtp</c> binding: it initialises GstSharp.Net
/// and makes sure that the types of this assembly are in the type registry.
/// </summary>
/// <remarks>
/// <para>
/// Every binding assembly registers its types from a module initialiser, and
/// the runtime runs a module initialiser before the first <em>call</em> into
/// that assembly, not before the first type of it is merely named. What this
/// module hands to the registry is four GObject classes — the payloader base
/// class <see cref="RTPBasePayload"/>, the audio payloader base class
/// <see cref="RTPBaseAudioPayload"/> derived from it, the depayloader base
/// class <see cref="RTPBaseDepayload"/> and the header extension
/// <see cref="RTPHeaderExtension"/>. Nothing else of the module carries an
/// entry: the mapping structures — <see cref="RTPBuffer"/>,
/// <see cref="RTCPBuffer"/> and <see cref="RTCPPacket"/> — are plain
/// structures the caller declares as values, <see cref="RTPPayloadInfo"/> is
/// one row of the static payload table and the two lookups that hand a row out
/// are not bound, and <see cref="RTPSourceMeta"/> is a buffer meta that only
/// the calls of this module ever produce.
/// </para>
/// <para>
/// An application that leaves every call to another binding assembly therefore
/// never executes a line of this one, and an element it asks
/// <c>Gst.ElementFactory.Make</c> for arrives as the closest type the registry
/// does know: an <c>rtpL16pay</c> is a <c>GstRTPBaseAudioPayload</c>, and
/// without this module it wraps as a plain <see cref="Gst.Element"/> rather
/// than as <see cref="RTPBaseAudioPayload"/> — the failure described under
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
public static class GstRtp
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
