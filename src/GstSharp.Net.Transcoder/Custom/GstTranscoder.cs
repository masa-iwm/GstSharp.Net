namespace Gst.Transcoder;

/// <summary>
/// The entry point of the <c>GstTranscoder</c> binding: it initialises
/// GstSharp.Net and makes sure that the types of this assembly are in the type
/// registry.
/// </summary>
/// <remarks>
/// <para>
/// Every binding assembly registers its types from a module initialiser, and
/// the runtime runs a module initialiser before the first <em>call</em> into
/// that assembly, not before the first type of it is merely named. What this
/// module hands to the registry is its two objects, <see cref="Transcoder"/>
/// and <see cref="TranscoderSignalAdapter"/>; the two class structures the gir
/// declares beside them carry no wrapper, as no class structure of any module
/// does.
/// </para>
/// <para>
/// An application that only names one of the two and leaves every call to
/// another binding assembly therefore never executes a line of this one: the
/// registry has no entry to build their wrappers from, and what arrives is the
/// closest type it does know — the failure described under
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
/// as well. Calling this one is the deterministic way to say it. The sweep
/// reaches the assemblies that are loaded at the time of the lookup, and a
/// wrapper the registry built before this assembly was loaded keeps the type it
/// got.
/// </para>
/// <para>
/// <b>The contract of the module.</b> A transcoder is a small state machine
/// around a <c>uritranscodebin</c> that runs on a thread of its own, and the
/// seven rules below are what the C library expects of a caller. They are not
/// checked by the binding, because none of them can be: what enforces them is
/// the shape of the sample under <c>samples/GstTranscode</c>.
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// The synchronous adapter of <see cref="Transcoder.GetSyncSignalAdapter"/>
/// installs a sync handler on the API bus that answers <c>GST_BUS_DROP</c> for
/// every message, so it consumes the whole bus. The synchronous adapter, the
/// asynchronous adapter of <see cref="Transcoder.GetSignalAdapter"/> and the
/// raw bus of <see cref="Transcoder.GetMessageBus"/> are therefore mutually
/// exclusive on one instance, and <see cref="Transcoder.Run"/> hangs forever
/// while a synchronous adapter exists: the messages it waits for are dropped
/// before its own adapter sees them.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Transcoder.Run"/> is a one-shot per instance. It connects its
/// handlers to a state on its own stack frame and never disconnects them, and
/// the adapter it connected them to is cached on the transcoder, so a second
/// run reaches a handler that is bound to a frame that has returned. Build a
/// new <see cref="Transcoder"/> for every job, and do not mix
/// <see cref="Transcoder.Run"/> and <see cref="Transcoder.RunAsync"/> on one
/// instance.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Transcoder.Run"/> has to be called from a thread that has no
/// thread-default main context — that is, one that uses the global default —
/// and before any <see cref="Transcoder.GetSignalAdapter"/> for a different
/// context. It runs its own main loop on the default context while its adapter
/// is attached to the thread-default one, so a caller that breaks either half
/// deadlocks.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Transcoder.RunAsync"/> reports its two synchronous failures — no
/// encoding profile, and a state change the pipeline refused — on the calling
/// thread, before it returns. Every other message arrives on the internal
/// worker thread of the transcoder. Disposing the transcoder from inside a
/// handler that runs on that worker deadlocks: the disposal joins the very
/// thread the handler is running on.
/// </description>
/// </item>
/// <item>
/// <description>
/// The details of an error or a warning may be absent. Both
/// <see cref="TranscoderMessageExtensions.ParseError"/> and the
/// <c>TranscoderSignalAdapter.Error</c> event answer <see langword="null"/>
/// for them, and the four errors the transcoder raises itself carry none.
/// </description>
/// </item>
/// <item>
/// <description>
/// The recommended route is <see cref="Transcoder.RunAsync"/> together with a
/// poll of <see cref="Transcoder.GetMessageBus"/>:
/// <see cref="Transcoder.IsTranscoderMessage"/> to recognise a message,
/// <see cref="TranscoderMessageExtensions.ParseType"/> to say which of the six
/// it is, and then
/// <see cref="TranscoderMessageExtensions.ParseState"/>,
/// <see cref="TranscoderMessageExtensions.ParsePosition"/> or
/// <see cref="TranscoderMessageExtensions.ParseError"/> to read it. That is
/// what the sample does.
/// </description>
/// </item>
/// <item>
/// <description>
/// The elements <c>uritranscodebin</c> and <c>transcodebin</c> have to be
/// installed at run time. They are in the <c>transcode</c> plugin of
/// gst-plugins-bad, which is a separate package from the
/// <c>libgsttranscoder-1.0</c> library this module imports from:
/// <see cref="Transcoder.New"/> succeeds without them, and
/// <see cref="Transcoder.GetPipeline"/> is what answers
/// <see langword="null"/>.
/// </description>
/// </item>
/// </list>
/// <para>
/// <see cref="Transcoder.New"/> also succeeds for an encoding profile string it
/// cannot parse: the profile is parsed when the transcoder is constructed and a
/// failure is reported by <see cref="Transcoder.RunAsync"/> as an error message
/// rather than by the factory.
/// <see cref="TranscoderSignalAdapter.GetTranscoder"/> answers
/// <see langword="null"/> on a synchronous adapter always — that adapter never
/// stores the transcoder — and on an asynchronous one once the transcoder it
/// holds weakly is gone.
/// </para>
/// </remarks>
public static class GstTranscoder
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
