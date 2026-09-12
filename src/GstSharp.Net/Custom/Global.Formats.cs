using System.Buffers;
using System.Runtime.InteropServices;

namespace Gst;

/// <content>
/// The one module level function of <c>libgstreamer-1.0</c> that takes a
/// zero terminated array of an enumeration.
/// </content>
/// <remarks>
/// <c>gst_formats_contains</c> is annotated <c>(array zero-terminated=1)</c>
/// and carries no count at all, so the number of elements the C reads is not
/// in the signature: it walks the array until the first
/// <see cref="Format.Undefined"/>. The emitter has no shape for that, so the
/// symbol sits in <c>girs/skip-report.md</c> and is written here instead, in
/// the class the gir puts it in: it is a function of the <c>Gst</c> namespace
/// rather than one the gir declares inside <c>GstFormat</c>, so it belongs
/// next to the other generated namespace level functions and not on
/// <see cref="FormatExtensions"/>.
/// </remarks>
public static unsafe partial class Global
{
    /// <summary>
    /// The number of formats this copies onto the stack before it rents the
    /// buffer from the array pool instead.
    /// </summary>
    private const int StackFormatCount = 64;

    /// <summary>
    /// Tests whether a list of formats contains one format.
    /// </summary>
    /// <param name="formats">The list to search.</param>
    /// <param name="format">The format to search for.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="formats"/> holds
    /// <paramref name="format"/> before its first
    /// <see cref="Format.Undefined"/> element.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_formats_contains</c>, the search a
    /// <see cref="QueryType.Convert"/> or <see cref="QueryType.Formats"/>
    /// answer is filtered with. The list the C reads is zero terminated, so
    /// the call is handed a copy of <paramref name="formats"/> with a trailing
    /// <see cref="Format.Undefined"/> appended; a list of fewer than 64
    /// formats is copied onto the stack, where the terminator takes the last
    /// of the 64 slots, and a longer one into a buffer rented from
    /// <see cref="ArrayPool{T}"/>.
    /// </para>
    /// <para>
    /// <b>The search stops at the first <see cref="Format.Undefined"/> element
    /// of <paramref name="formats"/>.</b> That value is the terminator of the
    /// list in C, not a format, so nothing behind one is looked at and
    /// searching for <see cref="Format.Undefined"/> itself always answers
    /// <see langword="false"/>. An empty list answers <see langword="false"/>
    /// as well.
    /// </para>
    /// </remarks>
    public static bool FormatsContains(ReadOnlySpan<Format> formats, Format format)
    {
        int[]? rented = null;
        Span<int> buffer = formats.Length < StackFormatCount
            ? stackalloc int[StackFormatCount]
            : (rented = ArrayPool<int>.Shared.Rent(formats.Length + 1));

        try
        {
            // The rented buffer is not cleared, so both the copy and the
            // terminator are written rather than assumed.
            Span<int> list = buffer[..(formats.Length + 1)];
            for (int index = 0; index < formats.Length; index++)
            {
                list[index] = (int)formats[index];
            }

            list[formats.Length] = (int)Format.Undefined;

            fixed (int* listPointer = list)
            {
                return GstFormatsContains(listPointer, (int)format) != 0;
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<int>.Shared.Return(rented);
            }
        }
    }

    /// <summary>The <c>gst_formats_contains</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_formats_contains")]
    private static partial int GstFormatsContains(int* formats, int format);
}
