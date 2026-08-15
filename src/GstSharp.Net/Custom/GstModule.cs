using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst;

/// <summary>
/// Registers this assembly with the native loader and the type registry.
/// </summary>
internal static partial class GstModule
{
    /// <summary>
    /// Runs before any code of this assembly: it hooks
    /// <see cref="NativeLoader"/> up, so that the <c>Gst</c> imports of this
    /// assembly resolve through it, and hands the type table of the module to
    /// <see cref="TypeRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Nothing here touches native code. The entries of the module are resolved
    /// when the registry is frozen, which <c>GstSharp.Initialize</c> does after
    /// the loader has been configured.
    /// </remarks>
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The ModuleInitializer attribute should not be used in libraries",
        Justification = "The resolver has to be registered before the first native call of this assembly, " +
            "and every binding assembly does the same. There is no entry point that could do it instead.")]
    internal static void Initialize()
    {
        NativeLoader.EnsureRegistered(typeof(GstModule).Assembly);

        // The generated type table of the module replaces the empty one as soon
        // as the class emitter lands; registering an empty module keeps the
        // registration path exercised until then.
        TypeRegistry.RegisterModule(new NativeModule("Gst", CreateEntries()));
    }
}
