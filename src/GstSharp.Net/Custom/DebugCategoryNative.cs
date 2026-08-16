using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// The raw entry point that registers a debug category.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the other hand written imports of this assembly, this one is
/// <b>permanent</b>: <c>_gst_debug_category_new</c> is not in the introspection
/// data at all. It is the function behind the
/// <c>GST_DEBUG_CATEGORY_INIT</c> macro, and a macro leaves no trace in a gir
/// file, so no generator change can ever emit it. There is nothing to put on
/// the skip list of <c>girs/overlays/fixups.json</c> either — the gir describes
/// no way of creating a <c>GstDebugCategory</c>.
/// </para>
/// <para>
/// The leading underscore is part of the exported symbol, not a private name:
/// the header declares it <c>GST_API</c> and it is present in the export table
/// of the shipped library. The header asks C code to use the macro instead,
/// which is advice about the caching the macro does around the call — the call
/// itself is the supported way in, and it is the only way in from a language
/// that has no preprocessor.
/// </para>
/// </remarks>
internal static unsafe partial class DebugCategoryNative
{
    /// <summary>
    /// Returns the category of <paramref name="name"/>, registering it with the
    /// given color and description when no category of that name exists yet.
    /// </summary>
    /// <param name="name">The name of the category.</param>
    /// <param name="color">The color of the category, as a set of term flags.</param>
    /// <param name="description">The description of the category, or <c>null</c>.</param>
    /// <returns>
    /// The category, which the debug system owns and keeps for the lifetime of
    /// the process.
    /// </returns>
    /// <remarks>
    /// The lookup and the registration happen under a lock of the debug system,
    /// and a name that is already known wins: the candidate that this call
    /// describes is dropped, and the category that was registered first comes
    /// back unchanged. That is what makes <c>GST_DEBUG_CATEGORY_INIT</c> safe to
    /// run from several translation units for one category.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "_gst_debug_category_new")]
    internal static partial nint New(byte* name, uint color, byte* description);
}
