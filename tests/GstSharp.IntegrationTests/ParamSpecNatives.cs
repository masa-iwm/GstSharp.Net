using System.Runtime.InteropServices;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>g_param_spec_*</c> constructors, which the binding does not offer.
/// </summary>
/// <remarks>
/// A specification a test installs itself is the only way to probe the derived
/// classes exhaustively: the core elements of GStreamer declare properties of
/// most kinds but not of all of them, and one that is declared nowhere could
/// only be read against a value nothing wrote. Every constructor here hands out
/// a floating specification, which <c>Gst.GObject.ParamSpec.FromNative</c> with
/// <c>Transfer.None</c> sinks and then owns, so disposing the wrapper releases
/// it.
/// </remarks>
internal static partial class ParamSpecNatives
{
    /// <summary>Value of <c>G_PARAM_READWRITE</c>.</summary>
    internal const uint ReadWrite = 3;

    [LibraryImport("GObject", EntryPoint = "g_param_spec_boolean", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Boolean(string name, string nick, string blurb, int defaultValue, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_char", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Char(
        string name,
        string nick,
        string blurb,
        sbyte minimum,
        sbyte maximum,
        sbyte defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_uchar", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint UChar(
        string name,
        string nick,
        string blurb,
        byte minimum,
        byte maximum,
        byte defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_int", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Int(
        string name,
        string nick,
        string blurb,
        int minimum,
        int maximum,
        int defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_uint", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint UInt(
        string name,
        string nick,
        string blurb,
        uint minimum,
        uint maximum,
        uint defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_long", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Long(
        string name,
        string nick,
        string blurb,
        CLong minimum,
        CLong maximum,
        CLong defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_ulong", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint ULong(
        string name,
        string nick,
        string blurb,
        CULong minimum,
        CULong maximum,
        CULong defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_int64", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Int64(
        string name,
        string nick,
        string blurb,
        long minimum,
        long maximum,
        long defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_uint64", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint UInt64(
        string name,
        string nick,
        string blurb,
        ulong minimum,
        ulong maximum,
        ulong defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_float", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Float(
        string name,
        string nick,
        string blurb,
        float minimum,
        float maximum,
        float defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_double", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Double(
        string name,
        string nick,
        string blurb,
        double minimum,
        double maximum,
        double defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_unichar", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Unichar(string name, string nick, string blurb, uint defaultValue, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint String(string name, string nick, string blurb, string? defaultValue, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_enum", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Enum(
        string name,
        string nick,
        string blurb,
        nuint enumType,
        int defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_flags", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Flags(
        string name,
        string nick,
        string blurb,
        nuint flagsType,
        uint defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_gtype", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GType(string name, string nick, string blurb, nuint isAType, uint flags);

    /// <summary>
    /// Builds a specification of an untyped pointer, which the binding wraps in
    /// <c>Gst.GObject.ParamSpec</c> itself: <c>GParamSpecPointer</c> declares
    /// nothing beyond the base class, so there is no derived class for it.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_pointer", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Pointer(string name, string nick, string blurb, uint flags);

    /// <summary>
    /// Builds a specification that stands for another one. The result floats,
    /// as everything here does, and it takes a reference of its own on what it
    /// overrides, so the caller keeps owning that.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_override", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Override(string name, nint overridden);

    [LibraryImport("Gst", EntryPoint = "gst_param_spec_fraction", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Fraction(
        string name,
        string nick,
        string blurb,
        int minimumNumerator,
        int minimumDenominator,
        int maximumNumerator,
        int maximumDenominator,
        int defaultNumerator,
        int defaultDenominator,
        uint flags);

    /// <summary>
    /// Builds a specification of an array of values. The specification of the
    /// elements is consumed: the array takes the floating reference of what it
    /// is handed, so the caller must not wrap it.
    /// </summary>
    [LibraryImport("Gst", EntryPoint = "gst_param_spec_array", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Array(string name, string nick, string blurb, nint elementSpec, uint flags);
}
