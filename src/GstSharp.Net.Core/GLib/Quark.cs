using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// A GLib quark: the integer identifier of an interned string.
/// </summary>
public readonly struct Quark : IEquatable<Quark>
{
    /// <summary>
    /// Initialises a quark from its integer value.
    /// </summary>
    /// <param name="value">The value of the quark.</param>
    public Quark(uint value) => Value = value;

    /// <summary>
    /// Gets the quark that stands for no string at all.
    /// </summary>
    public static Quark Zero => default;

    /// <summary>
    /// Gets the integer value of the quark.
    /// </summary>
    public uint Value { get; }

    /// <summary>Compares two quarks for equality.</summary>
    /// <param name="left">The first quark.</param>
    /// <param name="right">The second quark.</param>
    /// <returns><see langword="true"/> when both are equal.</returns>
    public static bool operator ==(Quark left, Quark right) => left.Equals(right);

    /// <summary>Compares two quarks for inequality.</summary>
    /// <param name="left">The first quark.</param>
    /// <param name="right">The second quark.</param>
    /// <returns><see langword="true"/> when both differ.</returns>
    public static bool operator !=(Quark left, Quark right) => !left.Equals(right);

    /// <summary>
    /// Interns <paramref name="name"/> and returns its quark.
    /// </summary>
    /// <param name="name">The string to intern.</param>
    /// <returns>The quark of <paramref name="name"/>.</returns>
    public static unsafe Quark FromString(string? name)
    {
        if (name is null)
        {
            return default;
        }

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);
        return new Quark(GLibNative.QuarkFromString(scope.Pointer));
    }

    /// <inheritdoc/>
    public bool Equals(Quark other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Quark other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>
    /// Returns the interned string of the quark.
    /// </summary>
    /// <returns>
    /// The string the quark stands for, or an empty string when the quark is
    /// zero or unknown.
    /// </returns>
    public override string ToString() =>
        Value == 0 ? string.Empty : GMarshal.PtrToStringUtf8(GLibNative.QuarkToString(Value)) ?? string.Empty;
}
