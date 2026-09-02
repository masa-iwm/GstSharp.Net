namespace GstSharp.Generator.Semantic;

/// <summary>
/// One binding module: a gir namespace, the C# namespace it is emitted into and
/// the project directory that holds the generated sources.
/// </summary>
/// <param name="GirNamespace">The gir namespace name, for example <c>GstBase</c>.</param>
/// <param name="ClrNamespace">The C# namespace, for example <c>Gst.Base</c>.</param>
/// <param name="ProjectDirectory">The project directory below the output root.</param>
/// <param name="NativeLibrary">
/// The logical library name used in <c>LibraryImport</c>. It has to be one of
/// the names that <c>Gst.Interop.NativeNames</c> resolves in the runtime
/// library, which is what maps it onto a file name per platform.
/// </param>
/// <param name="IsGenerated">Whether sources are emitted for the module.</param>
internal sealed record ModuleInfo(
    string GirNamespace,
    string ClrNamespace,
    string ProjectDirectory,
    string NativeLibrary,
    bool IsGenerated)
{
    /// <summary>
    /// Gets the name of the holder of the functions that belong to no type.
    /// </summary>
    /// <remarks>
    /// Every module used to call it <c>Global</c>, which reads as five
    /// unrelated types of one name once several modules are referenced
    /// together: <c>Gst.Global</c>, <c>Gst.Base.Global</c> and so on. The core
    /// module keeps the plain name, and every extension module prefixes it with
    /// the last segment of its C# namespace, so a using directive of
    /// <c>Gst.Base</c> brings in <c>BaseGlobal</c>.
    /// </remarks>
    internal string GlobalTypeName =>
        string.Equals(ClrNamespace, "Gst", StringComparison.Ordinal)
            ? "Global"
            : ClrNamespace[(ClrNamespace.LastIndexOf('.') + 1)..] + "Global";
}

/// <summary>
/// Maps gir namespaces onto binding projects.
/// </summary>
internal static class ModuleMap
{
    /// <summary>
    /// Gets every known module, in generation order. The GLib stack and Gio are
    /// present for type resolution only; their runtime layer is hand written in
    /// the <c>GstSharp.Net</c> assembly, under <c>src/GstSharp.Net/Core</c>.
    /// </summary>
    /// <remarks>
    /// The project directory of a module that emits nothing is never used: only
    /// the emitters read it, and they run for the generated modules alone. The
    /// Gio and GLib rows therefore name the assembly their types belong to
    /// rather than a directory sources would be written to.
    /// </remarks>
    internal static IReadOnlyList<ModuleInfo> Modules { get; } =
    [
        new ModuleInfo("Gst", "Gst", "GstSharp.Net", "Gst", IsGenerated: true),
        new ModuleInfo("GstBase", "Gst.Base", "GstSharp.Net.Base", "GstBase", IsGenerated: true),
        new ModuleInfo("GstApp", "Gst.App", "GstSharp.Net.App", "GstApp", IsGenerated: true),
        new ModuleInfo("GstAudio", "Gst.Audio", "GstSharp.Net.Audio", "GstAudio", IsGenerated: true),
        new ModuleInfo("GstVideo", "Gst.Video", "GstSharp.Net.Video", "GstVideo", IsGenerated: true),
        new ModuleInfo("GstPbutils", "Gst.Pbutils", "GstSharp.Net.Pbutils", "GstPbutils", IsGenerated: true),
        new ModuleInfo("GstSdp", "Gst.Sdp", "GstSharp.Net.Sdp", "GstSdp", IsGenerated: true),
        new ModuleInfo("GstWebRTC", "Gst.WebRTC", "GstSharp.Net.WebRTC", "GstWebRTC", IsGenerated: true),
        new ModuleInfo("GstNet", "Gst.Net", "GstSharp.Net.Net", "GstNet", IsGenerated: true),
        new ModuleInfo("GstRtsp", "Gst.Rtsp", "GstSharp.Net.Rtsp", "GstRtsp", IsGenerated: true),
        new ModuleInfo("GstRtp", "Gst.Rtp", "GstSharp.Net.Rtp", "GstRtp", IsGenerated: true),
        new ModuleInfo("GstAllocators", "Gst.Allocators", "GstSharp.Net.Allocators", "GstAllocators", IsGenerated: true),
        new ModuleInfo("GstTag", "Gst.Tag", "GstSharp.Net.Tag", "GstTag", IsGenerated: true),
        new ModuleInfo("GstTranscoder", "Gst.Transcoder", "GstSharp.Net.Transcoder", "GstTranscoder", IsGenerated: true),
        new ModuleInfo("GstPlay", "Gst.Play", "GstSharp.Net.Play", "GstPlay", IsGenerated: true),
        new ModuleInfo("GES", "GES", "GstSharp.Net.GES", "GES", IsGenerated: true),
        new ModuleInfo("Gio", "Gst.Gio", "GstSharp.Net", "Gio", IsGenerated: false),
        new ModuleInfo("GLib", "Gst.GLib", "GstSharp.Net", "GLib", IsGenerated: false),
        new ModuleInfo("GObject", "Gst.GObject", "GstSharp.Net", "GObject", IsGenerated: false),
        new ModuleInfo("GModule", "Gst.GLib", "GstSharp.Net", "GModule", IsGenerated: false),
    ];

    /// <summary>Looks a module up by gir namespace name.</summary>
    /// <param name="girNamespace">The gir namespace name.</param>
    /// <returns>The module, or <see langword="null"/> when unknown.</returns>
    internal static ModuleInfo? Find(string girNamespace)
    {
        foreach (ModuleInfo module in Modules)
        {
            if (string.Equals(module.GirNamespace, girNamespace, StringComparison.Ordinal))
            {
                return module;
            }
        }

        return null;
    }

    /// <summary>Returns the C# namespace a gir namespace is emitted into.</summary>
    /// <param name="girNamespace">The gir namespace name.</param>
    /// <returns>The C# namespace; unknown namespaces fall back to <c>Gst</c>.</returns>
    internal static string ClrNamespaceOf(string girNamespace) => Find(girNamespace)?.ClrNamespace ?? "Gst";
}
