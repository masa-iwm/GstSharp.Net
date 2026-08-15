namespace GstSharp.Generator.Semantic;

/// <summary>
/// One binding module: a gir namespace, the C# namespace it is emitted into and
/// the project directory that holds the generated sources.
/// </summary>
/// <param name="GirNamespace">The gir namespace name, for example <c>GstBase</c>.</param>
/// <param name="ClrNamespace">The C# namespace, for example <c>Gst.Base</c>.</param>
/// <param name="ProjectDirectory">The project directory below the output root.</param>
/// <param name="IsGenerated">Whether sources are emitted for the module.</param>
internal sealed record ModuleInfo(
    string GirNamespace,
    string ClrNamespace,
    string ProjectDirectory,
    bool IsGenerated);

/// <summary>
/// Maps gir namespaces onto binding projects.
/// </summary>
internal static class ModuleMap
{
    /// <summary>
    /// Gets every known module, in generation order. The GLib stack is present
    /// for type resolution only; its runtime layer is hand written in
    /// <c>GstSharp.Net.Core</c>.
    /// </summary>
    internal static IReadOnlyList<ModuleInfo> Modules { get; } =
    [
        new ModuleInfo("Gst", "Gst", "GstSharp.Net", IsGenerated: true),
        new ModuleInfo("GstBase", "Gst.Base", "GstSharp.Net.Base", IsGenerated: true),
        new ModuleInfo("GstApp", "Gst.App", "GstSharp.Net.App", IsGenerated: true),
        new ModuleInfo("GstAudio", "Gst.Audio", "GstSharp.Net.Audio", IsGenerated: true),
        new ModuleInfo("GstVideo", "Gst.Video", "GstSharp.Net.Video", IsGenerated: true),
        new ModuleInfo("GstPbutils", "Gst.Pbutils", "GstSharp.Net.Pbutils", IsGenerated: true),
        new ModuleInfo("GLib", "Gst.GLib", "GstSharp.Net.Core", IsGenerated: false),
        new ModuleInfo("GObject", "Gst.GObject", "GstSharp.Net.Core", IsGenerated: false),
        new ModuleInfo("GModule", "Gst.GLib", "GstSharp.Net.Core", IsGenerated: false),
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
