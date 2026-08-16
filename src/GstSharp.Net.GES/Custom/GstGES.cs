using Gst;
using Gst.GLib;

namespace GES;

/// <summary>
/// The entry point of the <c>GES</c> binding: it initialises GstSharp.Net, puts
/// the types of this assembly into the type registry and initialises the
/// GStreamer Editing Services themselves.
/// </summary>
/// <remarks>
/// <para>
/// The class is <c>GstGES</c> rather than <c>GES</c>, which is what the sibling
/// modules would suggest — <c>Gst.Sdp.GstSdp</c>, <c>Gst.Rtsp.GstRtsp</c>. The
/// C# namespace of this module is the gir namespace itself, so the name of the
/// convention would be the name of the namespace that holds it and every use of
/// it would read <c>GES.GES</c>. The prefix of the C library is what the
/// sibling names carry as well, and it is unambiguous here.
/// </para>
/// <para>
/// This module needs the call for a reason the others do not have. Every
/// binding assembly registers its types from a module initialiser, and the
/// runtime runs a module initialiser before the first <em>call</em> into that
/// assembly, not before a type of it is merely named; what this module hands to
/// the registry is the whole class hierarchy of the editing services, from
/// <see cref="Asset"/> to <see cref="Timeline"/>. An application that only
/// names one of them and leaves every call to another binding assembly never
/// executes a line of this one: the registry has no entry to build their
/// wrappers from, and what arrives is the closest type it does know — the
/// failure mode §2.1 of the acceptance requirements is about.
/// <c>GstSharp.Initialize</c> closes that hole on its own, by sweeping the
/// assemblies that are loaded and running their module initialisers, and it
/// keeps doing so for assemblies that are loaded later.
/// </para>
/// <para>
/// What it cannot do is initialise the editing services, and that is the second
/// gate: the type table above is a map from a <c>GType</c> to a wrapper, while
/// <c>ges_init</c> is what makes those <c>GType</c>s appear in a running
/// pipeline at all. Naming a GES type without having called
/// <see cref="Initialize"/> therefore leaves more broken than a wrong wrapper —
/// there is nothing to wrap.
/// </para>
/// </remarks>
public static class GstGES
{
    private static readonly object Sync = new();

    private static bool _initialized;

    /// <summary>
    /// Gets a value indicating whether <see cref="Initialize"/> has run.
    /// </summary>
    public static bool IsInitialized
    {
        get
        {
            lock (Sync)
            {
                return _initialized;
            }
        }
    }

    /// <summary>
    /// Loads the native libraries, initialises GStreamer, puts the types of
    /// this assembly into the type registry and initialises the editing
    /// services.
    /// </summary>
    /// <param name="options">
    /// Where the native libraries are and how GStreamer should be initialised,
    /// or <see langword="null"/> for the defaults.
    /// </param>
    /// <remarks>
    /// <para>
    /// This forwards to <c>GstSharp.Initialize</c> and is idempotent in the same
    /// way: after the first call, a call with <see langword="null"/> options
    /// does nothing, and options that contradict the first call are refused.
    /// Calling it after something else has already initialised GstSharp.Net,
    /// which is the normal shape of an application that uses several modules,
    /// is what the second half of it is for.
    /// </para>
    /// <para>
    /// That second half is <c>ges_init</c>, which runs exactly once and only
    /// after GStreamer itself is up. It registers the classes of the editing
    /// services with the type system, sets the formatter and asset caches up,
    /// and adds the elements GES builds a timeline out of to the plugin
    /// registry — the non linear engine <c>nle</c> among them, which is a
    /// plugin of its own that ships beside the library. Until it has run, no
    /// timeline can be built, whatever the type registry already knows.
    /// </para>
    /// <para>
    /// The editing services do not guarantee thread safety: an application is
    /// expected to use them from the thread that initialised them. This call is
    /// safe to make from several threads — the first one initialises and the
    /// others wait for it — but which thread wins decides where the rest of the
    /// GES work belongs, so initialise from the thread that will drive the
    /// timeline.
    /// </para>
    /// <para>
    /// There is no counterpart. <c>ges_deinit</c> exists in C, has to run on the
    /// thread <c>ges_init</c> ran on and leaves GES unusable until another
    /// <c>ges_init</c>, which the latch of this class has no way of learning
    /// about; it is not bound, and girs/overlays/fixups.json says why. Ending
    /// the process is how a GStreamer application releases GES, and the C
    /// documentation says the same.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The options conflict with the ones of the first call.
    /// </exception>
    /// <exception cref="Gst.Interop.GstNativeLoadException">
    /// The native libraries could not be found.
    /// </exception>
    /// <exception cref="GException">
    /// GStreamer or the editing services refused to initialise.
    /// </exception>
    public static void Initialize(GstSharpOptions? options = null)
    {
        lock (Sync)
        {
            // GStreamer first: ges_init expects it to be up, and the loader has
            // to be configured before the first native call of this assembly.
            global::GstSharp.Initialize(options);

            if (_initialized)
            {
                return;
            }

            if (!GESGlobal.Init())
            {
                // ges_init reports no GError. The one of ges_init_check is not
                // reachable either, since its argument vector is not bound.
                // What it does write is a GStreamer error message, which names
                // the missing nle plugin in the case that fails in practice.
                throw new GException(
                    "The GStreamer Editing Services could not be initialised, and did not report why. " +
                    "Run with GST_DEBUG=ges:3 to read what the library logged; a missing nle plugin, " +
                    "which is installed beside the GES library, is the usual cause.");
            }

            _initialized = true;
        }
    }
}
