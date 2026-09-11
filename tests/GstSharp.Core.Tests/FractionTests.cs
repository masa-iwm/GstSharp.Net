using Gst;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The parts of <see cref="Fraction"/> that hold without a GStreamer
/// installation: the pair it keeps, the text it formats and the terms it
/// refuses to be written with.
/// </summary>
/// <remarks>
/// Storing and reading one needs a <c>GST_TYPE_FRACTION</c> value, which only
/// a loaded library can register, so the round trip lives in the integration
/// suite.
/// </remarks>
public class FractionTests
{
    /// <summary>
    /// Nothing is reduced on the way in: the type is the pair it was given, and
    /// the reduction is GStreamer's, at the moment a value is written.
    /// </summary>
    [Fact]
    public void AFractionKeepsTheTermsItWasGiven()
    {
        Fraction fraction = new(2, 100);

        Assert.Equal(2, fraction.Numerator);
        Assert.Equal(100, fraction.Denominator);
    }

    [Fact]
    public void TwoFractionsWithTheSameTermsAreEqual()
    {
        Assert.Equal(new Fraction(30, 1), new Fraction(30, 1));

        // The unreduced pair is another pair, because nothing here reduces.
        Assert.NotEqual(new Fraction(1, 50), new Fraction(2, 100));
    }

    [Fact]
    public void AFractionFormatsAsNumeratorOverDenominator()
    {
        Assert.Equal("30/1", new Fraction(30, 1).ToString());
        Assert.Equal("-1/2", new Fraction(-1, 2).ToString());
    }

    /// <summary>
    /// The three terms <c>gst_value_set_fraction</c> answers with a critical and
    /// a write that never happens are refused before the call instead.
    /// </summary>
    [Fact]
    public void TheTermsGStreamerRefusesAreRefusedBeforeTheCall()
    {
        // The name the caller passed is what the exception carries: the check
        // runs below the member that took the argument.
        ArgumentOutOfRangeException zero = Assert.Throws<ArgumentOutOfRangeException>(
            static () => new Fraction(1, 0).RequireStorable("content"));
        Assert.Equal("content", zero.ParamName);

        Assert.Throws<ArgumentOutOfRangeException>(
            static () => new Fraction(int.MinValue, 1).RequireStorable("content"));
        Assert.Throws<ArgumentOutOfRangeException>(
            static () => new Fraction(1, int.MinValue).RequireStorable("content"));

        // A negative denominator is accepted: GStreamer moves the sign onto the
        // numerator rather than refusing the pair.
        new Fraction(1, -2).RequireStorable("content");
    }
}
