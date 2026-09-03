using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Gst.GObject;
using Gst.Interop;

namespace GES;

/// <summary>
/// Registers this assembly with the native loader and the type registry.
/// </summary>
/// <remarks>
/// The class is named after the gir namespace, which is what the generated half
/// of it is called: <c>GES</c> plus <c>Module</c>. Its file follows the class
/// rather than the <c>GstGES</c> entry point beside it.
/// </remarks>
internal static partial class GESModule
{
    /// <summary>
    /// Runs before any code of this assembly: it hooks
    /// <see cref="NativeLoader"/> up, so that the <c>GES</c> imports of this
    /// assembly resolve through it, and hands the type table of the module to
    /// <see cref="TypeRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Nothing here touches native code, and in particular nothing here
    /// initialises the editing services: the entries of the module are resolved
    /// when the registry is frozen, which <c>GstSharp.Initialize</c> does after
    /// the loader has been configured, and <c>ges_init</c> is what
    /// <see cref="GstGES.Initialize"/> adds on top of that.
    /// </remarks>
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The ModuleInitializer attribute should not be used in libraries",
        Justification = "The resolver has to be registered before the first native call of this assembly, " +
            "and every binding assembly does the same. There is no entry point that could do it instead.")]
    internal static void Initialize()
    {
        NativeLoader.EnsureRegistered(typeof(GESModule).Assembly);
        TypeRegistry.RegisterModule(new NativeModule("GES", CreateEntries(), CreateInterfaceEntries()));
    }
}
