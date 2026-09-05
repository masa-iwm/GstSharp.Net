using Gst.GObject;
using Gst.Interop;

namespace Gst;

/// <summary>
/// A <c>GstParamSpecFraction</c>: a property that carries a fraction out of a
/// range, which is how a frame rate or a pixel aspect ratio is declared.
/// </summary>
/// <remarks>
/// <para>
/// The six fields are read at the offsets of the public structure, because
/// GStreamer exposes them through the structure alone and declares no accessor
/// for any of them. Each bound is a numerator and a denominator of its own: the
/// range of <c>videotestsrc</c> is <c>0/1</c> to <c>2147483647/1</c>, and the
/// two halves are read separately because that is how C stores them.
/// </para>
/// <para>
/// The class lives in <c>Gst</c> rather than in <c>Gst.GObject</c> because the
/// type is GStreamer's: <c>Gst.ParamFlags</c>, the other half of what a
/// GStreamer property description carries, is beside it.
/// </para>
/// </remarks>
public sealed unsafe class ParamSpecFraction : ParamSpec
{
    internal ParamSpecFraction(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a fraction property, the way a frame rate or
    /// a pixel aspect ratio is declared.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimumNumerator">The numerator of the smallest accepted fraction.</param>
    /// <param name="minimumDenominator">The denominator of the smallest accepted fraction.</param>
    /// <param name="maximumNumerator">The numerator of the largest accepted fraction.</param>
    /// <param name="maximumDenominator">The denominator of the largest accepted fraction.</param>
    /// <param name="defaultNumerator">The numerator of the default.</param>
    /// <param name="defaultDenominator">The denominator of the default.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: GStreamer hands
    /// out a floating specification and the wrapper sinks it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A denominator is zero, a term is <see cref="int.MinValue"/>, the range is
    /// inverted, or the default lies outside it. GStreamer builds a fraction
    /// through <c>gst_value_set_fraction</c>, which refuses those terms and
    /// leaves the value it was to write at <c>0/1</c>, so the checks are made
    /// here rather than left to a validation that would run against the wrong
    /// value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// GStreamer refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecFraction New(
        string name,
        string? nick,
        string? blurb,
        int minimumNumerator,
        int minimumDenominator,
        int maximumNumerator,
        int maximumDenominator,
        int defaultNumerator,
        int defaultDenominator,
        ParamFlags flags)
    {
        CheckTerm(minimumNumerator, nameof(minimumNumerator), isDenominator: false);
        CheckTerm(minimumDenominator, nameof(minimumDenominator), isDenominator: true);
        CheckTerm(maximumNumerator, nameof(maximumNumerator), isDenominator: false);
        CheckTerm(maximumDenominator, nameof(maximumDenominator), isDenominator: true);
        CheckTerm(defaultNumerator, nameof(defaultNumerator), isDenominator: false);
        CheckTerm(defaultDenominator, nameof(defaultDenominator), isDenominator: true);

        if (!IsAtMost(minimumNumerator, minimumDenominator, maximumNumerator, maximumDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumNumerator),
                "The smallest accepted fraction is larger than the largest one.");
        }

        if (!IsAtMost(minimumNumerator, minimumDenominator, defaultNumerator, defaultDenominator)
            || !IsAtMost(defaultNumerator, defaultDenominator, maximumNumerator, maximumDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultNumerator),
                "The default fraction lies outside the range.");
        }

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GstNative.ParamSpecFraction(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimumNumerator,
            minimumDenominator,
            maximumNumerator,
            maximumDenominator,
            defaultNumerator,
            defaultDenominator,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecFraction(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>
    /// Refuses a term <c>gst_value_set_fraction</c> would refuse: a denominator
    /// of zero, and either half below <c>-G_MAXINT</c>, which on a 32 bit
    /// integer is <see cref="int.MinValue"/> alone.
    /// </summary>
    private static void CheckTerm(int value, string parameterName, bool isDenominator)
    {
        if (isDenominator && value == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A denominator must not be zero.");
        }

        if (value == int.MinValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A term of a fraction has to be larger than the negated largest integer.");
        }
    }

    /// <summary>
    /// Answers whether one fraction is at most as large as another. The two are
    /// compared by crossing the terms in 64 bits, after moving a negative sign
    /// out of the denominator, which is where the check above leaves it.
    /// </summary>
    private static bool IsAtMost(int leftNumerator, int leftDenominator, int rightNumerator, int rightDenominator)
    {
        (long leftTop, long leftBottom) = Normalize(leftNumerator, leftDenominator);
        (long rightTop, long rightBottom) = Normalize(rightNumerator, rightDenominator);

        return leftTop * rightBottom <= rightTop * leftBottom;
    }

    private static (long Numerator, long Denominator) Normalize(int numerator, int denominator) =>
        denominator < 0 ? (-(long)numerator, -(long)denominator) : (numerator, denominator);

    /// <summary>Gets the numerator of the smallest fraction the property accepts.</summary>
    public int MinimumNumerator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the denominator of the smallest fraction the property accepts.</summary>
    public int MinimumDenominator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 4);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the numerator of the largest fraction the property accepts.</summary>
    public int MaximumNumerator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the denominator of the largest fraction the property accepts.</summary>
    public int MaximumDenominator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 12);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the numerator of the fraction the property has when nothing was
    /// written to it.
    /// </summary>
    public int DefaultNumerator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 16);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the denominator of the fraction the property has when nothing was
    /// written to it.
    /// </summary>
    public int DefaultDenominator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 20);
            GC.KeepAlive(this);
            return value;
        }
    }
}
