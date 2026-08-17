using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The module level functions of <c>libgstreamer-1.0</c> that the generator
/// cannot emit, in the class it would have emitted them into.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_value_serialize</c> takes a <c>const GValue *</c>, and a plain
/// <c>GValue</c> parameter is a signature the function emitter does not cover,
/// so the symbol sits in <c>girs/skip-report.md</c> together with most of the
/// <c>gst_value_*</c> family. It is written by hand here rather than in a class
/// of its own, for the reason <see cref="Message.ParseError"/> is written on
/// <see cref="Message"/>: a hand written binding belongs where the generated
/// one would be, so that there is one place to look and one name to learn. The
/// generated <see cref="Global.ValueRegister"/> is its neighbour.
/// </para>
/// <para>
/// Should the emitter ever learn the shape, the symbol goes on the skip list of
/// <c>girs/overlays/fixups.json</c> and this stays the single definition —
/// which is the same arrangement the parse calls of <see cref="Message"/>
/// document.
/// </para>
/// </remarks>
public static unsafe partial class Global
{
    /// <summary>
    /// Writes a value as the text that a pipeline description, a caps or a
    /// structure would carry it as.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <returns>
    /// The text, or <see langword="null"/> when nothing can write a value of
    /// that type.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_value_serialize</c>, the printer behind
    /// <see cref="Structure.ToString"/> and the counterpart of the parser that
    /// reads a pipeline description. It knows the GStreamer types that GObject
    /// does not — a fraction becomes <c>30/1</c>, an integer range
    /// <c>[ 1, 10 ]</c>, a bitmask <c>0x0000000000000003</c> — and falls back
    /// to the GObject transform to a string for everything else. A string is
    /// written the way a structure would carry it, so one that holds a space
    /// comes back quoted and escaped.
    /// </para>
    /// <para>
    /// <b>This is how a property of unknown type is printed.</b> A property
    /// read with <see cref="Gst.GObject.Object.GetProperty"/> or taken from a
    /// <see cref="MessageType.PropertyNotify"/> message is a
    /// <see cref="Gst.GObject.Value"/> whose type is only known at run time,
    /// and this turns it into a line of output without a switch over every type
    /// the pipeline might hold.
    /// </para>
    /// <para>
    /// A type with no serialisation function and no transform to a string
    /// produces <see langword="null"/> rather than an exception: a value that
    /// cannot be written is a normal answer for a printer, the way an object
    /// property is.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> holds nothing. An uninitialised value has no
    /// type to write it as, and C answers that with an assertion failure on the
    /// console.
    /// </exception>
    public static string? ValueSerialize(in Gst.GObject.Value value)
    {
        if (value.IsEmpty)
        {
            throw new ArgumentException(
                "An empty value cannot be serialized: it has no type to write it as.",
                nameof(value));
        }

        // The string is a fresh allocation that the caller owns.
        nint text = GstValueSerialize(ref Unsafe.AsRef(in value).NativeValue);
        return GMarshal.PtrToStringUtf8AndFree(text);
    }

    /// <summary>The <c>gst_value_serialize</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_value_serialize")]
    private static partial nint GstValueSerialize(ref Gst.GObject.GValueNative value);
}
