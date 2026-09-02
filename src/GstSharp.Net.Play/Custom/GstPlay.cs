namespace Gst.Play;

/// <summary>
/// The entry point of the <c>GstPlay</c> binding: it initialises GstSharp.Net
/// and makes sure that the types of this assembly are in the type registry.
/// </summary>
/// <remarks>
/// <para>
/// Every binding assembly registers its types from a module initialiser, and
/// the runtime runs a module initialiser before the first <em>call</em> into
/// that assembly, not before the first type of it is merely named. What this
/// module hands to the registry is its nine objects, <see cref="Play"/> and
/// <see cref="PlaySignalAdapter"/> among them; the class structures the gir
/// declares beside them carry no wrapper, as no class structure of any module
/// does.
/// </para>
/// <para>
/// An application that only names one of them and leaves every call to another
/// binding assembly therefore never executes a line of this one: the registry
/// has no entry to build their wrappers from, and what arrives is the closest
/// type it does know — the failure described under
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
/// <para>
/// <b>The contract of the module.</b> A <see cref="Play"/> is a small state
/// machine around <c>playbin3</c> that runs on a thread of its own, and the six
/// rules below are what the C library expects of a caller. Upstream marks the
/// whole library API <i>unstable</i>
/// (<c>gst-plugins-bad/docs/libs/play/index.md</c>); this binding follows the
/// same additive promise as every other module of the repository all the same.
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Stop reading the API bus before disposing the play.</b>
/// <c>Play.Dispose</c> sets the bus of <see cref="Play.GetMessageBus"/>
/// flushing, because the messages of that bus name the play as their source
/// and an unread bus would keep the play and its thread alive for good. A
/// polling loop that is still running sees the bus fall silent.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="PlaySignalAdapter.NewSyncEmit(Play)"/> installs the one sync
/// handler of the API bus and answers <c>GST_BUS_DROP</c> for every message,
/// so it consumes the whole bus. The synchronous adapter, an asynchronous
/// adapter and a poll of <see cref="Play.GetMessageBus"/> are therefore
/// mutually exclusive on one play, and disposing any adapter flushes the bus
/// for whatever else was reading it.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="PlaySignalAdapter.New(Play)"/> and
/// <see cref="PlaySignalAdapter.NewWithMainContext(Play, Gst.GLib.MainContext?)"/>
/// attach a bus watch to a <c>GMainContext</c>. <b>Their signals only fire
/// while somebody iterates that context</b>, so in an application that runs no
/// GLib main loop they never fire at all, silently. Use the synchronous
/// adapter or poll the bus there.
/// </description>
/// </item>
/// <item>
/// <description>
/// The signals of the synchronous adapter arrive on the internal thread of the
/// play, except <c>volume-changed</c> and <c>mute-changed</c>, which arrive on
/// whichever thread changed the value. Disposing the play from a handler that
/// runs on the internal thread deadlocks: the disposal joins that thread.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Play.GetMediaInfo"/> answers <see langword="null"/> until the URI
/// has prerolled, and a snapshot afterwards: what it hands back never changes,
/// and a later call answers a newer one. The stream lists of a
/// <see cref="PlayMediaInfo"/> are read out of it, and each stream info is a
/// wrapper of its own that holds its own reference, so one of them outlives the
/// snapshot it was read from.
/// </description>
/// </item>
/// <item>
/// <description>
/// The index based track selection —
/// <c>SetAudioTrack</c>, <c>SetVideoTrack</c>, <c>SetSubtitleTrack</c> and
/// <see cref="PlayStreamInfo.GetIndex"/> — is marked obsolete because
/// GStreamer 1.26 deprecated it, and it is the only selection API a 1.24
/// installation has. The replacement,
/// <see cref="Play.SetAudioTrackId(string?)"/> and its siblings, throws
/// <see cref="EntryPointNotFoundException"/> there.
/// <see cref="PlayMessageExtensions.ParseDurationUpdated"/> and
/// <see cref="PlayMessageExtensions.ParseBufferingPercent"/> are the same
/// shape: obsolete since 1.26, and the only parses of their two messages a
/// 1.24 installation exports.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class GstPlay
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
