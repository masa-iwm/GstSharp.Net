using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.Controller;

/// <summary>
/// Registers this assembly with the native loader and the type registry.
/// </summary>
/// <remarks>
/// This is the whole registration half of the module contract, and it is
/// written against the public surface of <c>GstSharp.Net</c> only. A binding
/// module outside this repository copies these three calls and changes the
/// names; see
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md">docs/modules.md</see>.
/// </remarks>
internal static unsafe class GstControllerModule
{
    /// <summary>The logical name of <c>libgstcontroller-1.0</c>.</summary>
    /// <remarks>
    /// It is what the <c>LibraryImport</c> stubs of this assembly name and what
    /// the <see cref="NativeModule"/> carries, and it has to be a name that
    /// <c>GstSharp.Net</c> does not already use.
    /// </remarks>
    internal const string LogicalName = "GstController";

    /// <summary>
    /// Runs before any code of this assembly: it hooks the native loader up so
    /// that the <c>GstController</c> imports of this assembly resolve through
    /// it, teaches the loader the file names of that library, and hands the type
    /// table of the module to the registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order inside this method does not matter, but the fact that it runs
    /// from a module initialiser does: a module initialiser runs before the
    /// first call into its assembly, and therefore before any stub of this
    /// assembly can ask the loader to resolve <c>GstController</c>. Under
    /// NativeAOT every module initialiser has already run by the time the entry
    /// point does.
    /// </para>
    /// <para>
    /// Nothing here touches native code. The file names are remembered, not
    /// resolved, and the <c>get_type</c> entries are only called when the
    /// registry is frozen, which happens after the native libraries have been
    /// loaded.
    /// </para>
    /// </remarks>
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The ModuleInitializer attribute should not be used in libraries",
        Justification = "The resolver has to be registered before the first native call of this assembly, " +
            "and every binding module does the same. There is no entry point that could do it instead.")]
    internal static void Initialize()
    {
        NativeLoader.EnsureRegistered(typeof(GstControllerModule).Assembly);

        NativeLoader.RegisterLibrary(LogicalName, new NativeLibraryNames
        {
            Linux = "libgstcontroller-1.0.so.0",
            MacOs = "libgstcontroller-1.0.0.dylib",
            WindowsMsvc = "gstcontroller-1.0-0.dll",
            WindowsMinGW = "libgstcontroller-1.0-0.dll",
        });

        TypeRegistry.RegisterModule(new NativeModule(LogicalName, CreateEntries()));
    }

    /// <summary>
    /// Builds the type table of the module.
    /// </summary>
    /// <returns>One entry per wrapper of this assembly.</returns>
    /// <remarks>
    /// <para>
    /// The three control bindings of the library are deliberately absent. None
    /// of them has a wrapper class of its own — see
    /// <see cref="DirectControlBinding"/>, <see cref="ARGBControlBinding"/> and
    /// <see cref="ProxyControlBinding"/>, which are factories — so an instance
    /// of any of them should keep arriving as the
    /// <see cref="Gst.ControlBinding"/> that <c>GstSharp.Net</c> registers, and
    /// an entry here would take that away.
    /// </para>
    /// <para>
    /// <c>GstTimedValueControlSource</c> is abstract and no instance of it ever
    /// exists, but the entry is not pointless: a type that derives from it and
    /// has no binding here — one that a later release of GStreamer or another
    /// library adds — is wrapped through it instead of falling back further.
    /// </para>
    /// </remarks>
    private static ModuleTypeEntry[] CreateEntries() =>
    [
        new ModuleTypeEntry(
            &InterpolationControlSource.GetGType,
            &InterpolationControlSource.CreateWrapper),
        new ModuleTypeEntry(
            &LFOControlSource.GetGType,
            &LFOControlSource.CreateWrapper),
        new ModuleTypeEntry(
            &TriggerControlSource.GetGType,
            &TriggerControlSource.CreateWrapper),
        new ModuleTypeEntry(
            &TimedValueControlSource.GetGType,
            &TimedValueControlSource.CreateWrapper),
    ];
}
