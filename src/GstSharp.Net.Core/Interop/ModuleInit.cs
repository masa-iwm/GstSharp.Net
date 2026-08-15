using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Gst.Interop;

/// <summary>
/// Registers the native library resolver of the runtime assembly.
/// </summary>
internal static class ModuleInit
{
    /// <summary>
    /// Runs before any code of this assembly and hooks
    /// <see cref="NativeLoader"/> up, so that the imports of the runtime itself
    /// resolve through it.
    /// </summary>
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The ModuleInitializer attribute should not be used in libraries",
        Justification = "The resolver has to be registered before the first native call of this assembly, " +
            "and every binding assembly does the same. There is no entry point that could do it instead.")]
    internal static void Initialize() => NativeLoader.EnsureRegistered(typeof(ModuleInit).Assembly);
}
