using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <summary>
/// How two values compare, which for the types GStreamer adds is not always an
/// answer of the three an ordering gives.
/// </summary>
/// <remarks>
/// This is the return of <c>gst_value_compare</c>. Two integer ranges that
/// overlap without containing one another, and any two values of different
/// types, are <see cref="Unordered"/>: neither is smaller, neither is larger,
/// and they are not equal either.
/// </remarks>
public enum ValueOrder
{
    /// <summary>The first value sorts before the second. <c>GST_VALUE_LESS_THAN</c>.</summary>
    LessThan = -1,

    /// <summary>The two values are equal. <c>GST_VALUE_EQUAL</c>.</summary>
    Equal = 0,

    /// <summary>The first value sorts after the second. <c>GST_VALUE_GREATER_THAN</c>.</summary>
    GreaterThan = 1,

    /// <summary>The two values cannot be ordered at all. <c>GST_VALUE_UNORDERED</c>.</summary>
    Unordered = 2,
}

/// <content>
/// The module level functions of <c>libgstreamer-1.0</c> that the generator
/// cannot emit, in the class it would have emitted them into.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_value_serialize</c> and <c>gst_value_compare</c> take a
/// <c>const GValue *</c>, and a plain <c>GValue</c> parameter is a signature
/// the function emitter does not cover, so both symbols sit in
/// <c>girs/skip-report.md</c> together with most of the <c>gst_value_*</c>
/// family. They are written by hand here rather than in a class of their own,
/// for the reason <see cref="Message.ParseError"/> is written on
/// <see cref="Message"/>: a hand written binding belongs where the generated
/// one would be, so that there is one place to look and one name to learn. The
/// generated <see cref="Global.ValueRegister"/> is their neighbour.
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

    /// <summary>
    /// Compares two values the way GStreamer does.
    /// </summary>
    /// <param name="first">The value on the left.</param>
    /// <param name="second">The value on the right.</param>
    /// <returns>How the two compare.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_value_compare</c>. It knows the types GStreamer adds on
    /// top of GObject — a fraction, an integer range, a list, a bitmask — and
    /// falls back to the comparison function GObject registered for anything
    /// else. Two values of different types never compare equal.
    /// </para>
    /// <para>
    /// <b>The answer is not always an ordering.</b>
    /// <see cref="ValueOrder.Unordered"/> is the honest fourth case and the
    /// reason this returns an enumeration rather than a number: a pair of
    /// overlapping ranges is neither smaller, nor larger, nor equal, and
    /// treating the C value 2 as "greater than" would be wrong in exactly the
    /// place the distinction matters.
    /// </para>
    /// <para>
    /// <b>This is how "the property still has its default" is decided.</b>
    /// Reading the same property off the object and off a bare element of the
    /// same factory and comparing the two is what a tool does before it prints
    /// a property as part of a pipeline description; a
    /// <see cref="ValueOrder.Equal"/> answer means the property says nothing
    /// the factory does not already say.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// One of the values holds nothing. An uninitialised value has no type to
    /// compare, and C answers that with an assertion failure on the console.
    /// </exception>
    public static ValueOrder ValueCompare(in Gst.GObject.Value first, in Gst.GObject.Value second)
    {
        if (first.IsEmpty)
        {
            throw new ArgumentException(
                "An empty value cannot be compared: it has no type to compare as.",
                nameof(first));
        }

        if (second.IsEmpty)
        {
            throw new ArgumentException(
                "An empty value cannot be compared: it has no type to compare as.",
                nameof(second));
        }

        int order = GstValueCompare(
            ref Unsafe.AsRef(in first).NativeValue,
            ref Unsafe.AsRef(in second).NativeValue);

        return order switch
        {
            -1 => ValueOrder.LessThan,
            0 => ValueOrder.Equal,
            1 => ValueOrder.GreaterThan,

            // GST_VALUE_UNORDERED is 2, and nothing else is defined. A value
            // the library grows later is reported as the case that promises
            // the least rather than as an ordering that was never given.
            _ => ValueOrder.Unordered,
        };
    }

    /// <summary>The <c>gst_value_serialize</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_value_serialize")]
    private static partial nint GstValueSerialize(ref Gst.GObject.GValueNative value);

    /// <summary>The <c>gst_value_compare</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_value_compare")]
    private static partial int GstValueCompare(
        ref Gst.GObject.GValueNative value1,
        ref Gst.GObject.GValueNative value2);
}
