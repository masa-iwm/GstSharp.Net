using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Gst.Interop;

/// <summary>
/// Registers the native library resolver of the runtime layer.
/// </summary>
/// <remarks>
/// The runtime layer ships inside the <c>GstSharp.Net</c> assembly, whose
/// generated bindings register the same resolver from <c>GstModule</c>. Both
/// initialisers run, in an order the runtime does not define, and
/// <see cref="NativeLoader.EnsureRegistered"/> is idempotent, so the layer keeps
/// its own registration rather than depending on the one next to it.
/// </remarks>
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
