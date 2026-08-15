using System.Globalization;
using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// The C# underlying type of a generated enumeration.
/// </summary>
internal enum EnumUnderlyingType
{
    /// <summary><see cref="int"/>, the C# default.</summary>
    Int32,

    /// <summary><see cref="uint"/>, needed when a member sets bit 31.</summary>
    UInt32,
}

/// <summary>
/// Facts derived from the members of an <c>&lt;enumeration&gt;</c> or a
/// <c>&lt;bitfield&gt;</c>.
/// </summary>
internal static class EnumFacts
{
    /// <summary>
    /// Computes the underlying type. C enumerations are <c>int</c> unless a
    /// member does not fit, which happens for bitfields that use bit 31 such as
    /// <c>GstMessageType</c>.
    /// </summary>
    /// <param name="enumeration">The enumeration to inspect.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    /// <returns>The underlying type.</returns>
    internal static EnumUnderlyingType GetUnderlyingType(GirEnumeration enumeration, DiagnosticBag diagnostics)
    {
        bool negative = false;
        bool aboveInt32 = false;

        foreach (GirEnumMember member in enumeration.Members)
        {
            if (!TryParseValue(member.Value, out long value))
            {
                diagnostics.Warn(
                    "GEN0002",
                    $"Enumeration member '{enumeration.Name}.{member.Name}' has the unparsable value '{member.Value}'.");
                continue;
            }

            negative |= value < 0;
            aboveInt32 |= value > int.MaxValue;
        }

        if (negative && aboveInt32)
        {
            diagnostics.Error(
                "GEN0003",
                $"Enumeration '{enumeration.Name}' mixes negative members with members above int.MaxValue.");
            return EnumUnderlyingType.Int32;
        }

        return aboveInt32 ? EnumUnderlyingType.UInt32 : EnumUnderlyingType.Int32;
    }

    /// <summary>Parses the verbatim <c>value</c> attribute of a member.</summary>
    /// <param name="raw">The attribute value.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when the value could be parsed.</returns>
    internal static bool TryParseValue(string raw, out long value) =>
        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>Returns the C# keyword of an underlying type.</summary>
    /// <param name="underlyingType">The underlying type.</param>
    /// <returns>The C# keyword.</returns>
    internal static string ToKeyword(EnumUnderlyingType underlyingType) =>
        underlyingType == EnumUnderlyingType.UInt32 ? "uint" : "int";
}
