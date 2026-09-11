using System.Globalization;

namespace Gst;

/// <summary>
/// An integer numerator over an integer denominator, which is what a
/// <c>GST_TYPE_FRACTION</c> value holds: a frame rate, a pixel aspect ratio or
/// the block size of an audio mixer.
/// </summary>
/// <remarks>
/// <para>
/// The type is the pair and nothing else: it neither reduces nor validates what
/// it is given. GStreamer reduces the pair and moves the sign onto the numerator
/// when it stores one, so a fraction read back out of a value is the reduced
/// form of the one that was written — <c>2/100</c> comes back as <c>1/50</c>
/// and <c>1/-2</c> as <c>-1/2</c>.
/// </para>
/// <para>
/// A denominator of zero and a term of <see cref="int.MinValue"/> are the
/// three pairs GStreamer refuses. They are reported by
/// <see cref="Gst.GObject.Value.SetFraction"/> rather than here, because a pair
/// only has to be one GStreamer accepts at the moment it is written.
/// </para>
/// </remarks>
/// <param name="Numerator">The numerator of the fraction.</param>
/// <param name="Denominator">The denominator of the fraction.</param>
public readonly record struct Fraction(int Numerator, int Denominator)
{
    /// <summary>
    /// Formats the fraction as <c>numerator/denominator</c>.
    /// </summary>
    /// <returns>The formatted fraction, for example <c>30/1</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");

    /// <summary>
    /// Refuses a pair <c>gst_value_set_fraction</c> would refuse: a denominator
    /// of zero, and either term below <c>-G_MAXINT</c>, which on a 32 bit
    /// integer is <see cref="int.MinValue"/> alone. GStreamer answers all three
    /// with a critical and a write that never happens, so the writers of the
    /// runtime ask here instead.
    /// </summary>
    /// <param name="parameterName">The name of the argument being checked.</param>
    /// <exception cref="ArgumentOutOfRangeException">The pair is one of the three.</exception>
    internal void RequireStorable(string parameterName)
    {
        if (Denominator == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, this, "A denominator must not be zero.");
        }

        if (Numerator == int.MinValue || Denominator == int.MinValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                this,
                "A term of a fraction has to be larger than the negated largest integer.");
        }
    }
}
