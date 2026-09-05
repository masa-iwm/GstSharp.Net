using System.Runtime.InteropServices;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Building property descriptions from managed code: the specification a
/// <c>New</c> hands out owns exactly one reference, carries what it was given,
/// and refuses on the managed side everything the C constructors only assert —
/// an invalid name above all, which GObject answers with the null pointer that
/// every constructor then dereferences.
/// </summary>
[Collection(GstCollection.Name)]
public sealed unsafe class ParamSpecConstructionTests
{
    private const ParamFlags ReadWrite = ParamFlags.Readable | ParamFlags.Writable;

    /// <summary>
    /// An integer specification carries its name, its range and its default,
    /// and the wrapper holds the only reference to it.
    /// </summary>
    [Fact]
    public void AnIntegerSpecificationCarriesItsRange()
    {
        using ParamSpecInt spec = ParamSpecInt.New(
            "an-int",
            "An integer",
            "An integer out of a range",
            -5,
            10,
            3,
            ReadWrite);

        Assert.Equal("an-int", spec.Name);
        Assert.Equal("An integer", spec.Nick);
        Assert.Equal("An integer out of a range", spec.Blurb);
        Assert.Equal(-5, spec.Minimum);
        Assert.Equal(10, spec.Maximum);
        Assert.Equal(3, spec.Default);
        Assert.Equal(GType.Int, spec.ValueType);
        Assert.Equal(ReadWrite, spec.Flags);
        Assert.Equal(1u, RefCountOf(spec.Handle));
        Assert.Equal(3, spec.DefaultValue.GetInt());
    }

    /// <summary>
    /// A specification of every remaining scalar kind carries the three bounds
    /// it was built with. The C <c>long</c> ones travel through
    /// <see cref="CLong"/>, which is 32 bits wide on Windows.
    /// </summary>
    [Fact]
    public void EveryScalarKindCarriesItsBounds()
    {
        using ParamSpecBoolean boolean = ParamSpecBoolean.New("a-bool", null, null, true, ReadWrite);
        Assert.True(boolean.Default);

        using ParamSpecChar signedByte = ParamSpecChar.New("a-char", null, null, -3, 4, 0, ReadWrite);
        Assert.Equal(-3, signedByte.Minimum);
        Assert.Equal(4, signedByte.Maximum);
        Assert.Equal(0, signedByte.Default);

        using ParamSpecUChar unsignedByte = ParamSpecUChar.New("a-uchar", null, null, 1, 200, 7, ReadWrite);
        Assert.Equal(200, unsignedByte.Maximum);

        using ParamSpecUInt unsigned = ParamSpecUInt.New("a-uint", null, null, 0u, 99u, 9u, ReadWrite);
        Assert.Equal(9u, unsigned.Default);

        using ParamSpecInt64 wide = ParamSpecInt64.New(
            "an-int64",
            null,
            null,
            long.MinValue,
            long.MaxValue,
            1L,
            ReadWrite);
        Assert.Equal(long.MaxValue, wide.Maximum);

        using ParamSpecUInt64 wideUnsigned = ParamSpecUInt64.New(
            "a-uint64",
            null,
            null,
            0UL,
            ulong.MaxValue,
            8UL,
            ReadWrite);
        Assert.Equal(ulong.MaxValue, wideUnsigned.Maximum);

        using ParamSpecFloat single = ParamSpecFloat.New("a-float", null, null, 0f, 1f, 0.5f, ReadWrite);
        Assert.Equal(0.5f, single.Default);

        using ParamSpecDouble twice = ParamSpecDouble.New("a-double", null, null, -1d, 1d, 0d, ReadWrite);
        Assert.Equal(-1d, twice.Minimum);

        using ParamSpecUnichar codePoint = ParamSpecUnichar.New("a-unichar", null, null, 0x41u, ReadWrite);
        Assert.Equal(0x41u, codePoint.Default);

        using ParamSpecLong native = ParamSpecLong.New("a-long", null, null, -2L, 2L, 1L, ReadWrite);
        Assert.Equal(-2L, native.Minimum);
        Assert.Equal(1L, native.Default);

        using ParamSpecULong nativeUnsigned = ParamSpecULong.New("a-ulong", null, null, 0UL, 4UL, 2UL, ReadWrite);
        Assert.Equal(4UL, nativeUnsigned.Maximum);
    }

    /// <summary>
    /// A bound that does not fit the C <c>long</c> of the platform is refused
    /// rather than truncated. It only can fail to fit on a platform whose C
    /// <c>long</c> is 32 bits wide, which is Windows.
    /// </summary>
    [Fact]
    public void ALongBoundThatDoesNotFitThePlatformIsRefused()
    {
        if (sizeof(CLong) != 4)
        {
            using ParamSpecLong wide = ParamSpecLong.New(
                "a-wide-long",
                null,
                null,
                0L,
                long.MaxValue,
                0L,
                ReadWrite);
            Assert.Equal(long.MaxValue, wide.Maximum);
            return;
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecLong.New("a-wide-long", null, null, 0L, long.MaxValue, 0L, ReadWrite));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecULong.New("a-wide-ulong", null, null, 0UL, ulong.MaxValue, 0UL, ReadWrite));
    }

    /// <summary>
    /// A string specification copies its default, and a null default comes back
    /// as <see langword="null"/> rather than as an empty string.
    /// </summary>
    [Fact]
    public void AStringSpecificationCopiesItsDefault()
    {
        using ParamSpecString withDefault = ParamSpecString.New(
            "a-string",
            "A string",
            "A string with a default",
            "the default",
            ReadWrite);

        Assert.Equal("the default", withDefault.Default);
        Assert.Equal(GType.String, withDefault.ValueType);

        using ParamSpecString without = ParamSpecString.New("no-default", null, null, null, ReadWrite);
        Assert.Null(without.Default);
    }

    /// <summary>
    /// An enumeration specification is built over a real GStreamer enumeration
    /// and lists its members.
    /// </summary>
    [Fact]
    public void AnEnumerationSpecificationIsBuiltOverARealEnumeration()
    {
        GType state = GType.FromName("GstState");

        using ParamSpecEnum spec = ParamSpecEnum.New(
            "a-state",
            "A state",
            "The state of an element",
            state,
            (int)State.Paused,
            ReadWrite);

        Assert.Equal(state, spec.ValueType);
        Assert.Equal((int)State.Paused, spec.Default);
        Assert.NotEmpty(spec.Values);
    }

    /// <summary>
    /// A flags specification is built over a real set of flags.
    /// </summary>
    [Fact]
    public void AFlagsSpecificationIsBuiltOverARealSet()
    {
        GType seekFlags = GType.FromName("GstSeekFlags");

        using ParamSpecFlags spec = ParamSpecFlags.New(
            "some-flags",
            null,
            null,
            seekFlags,
            (uint)SeekFlags.Flush,
            ReadWrite);

        Assert.Equal(seekFlags, spec.ValueType);
        Assert.Equal((uint)SeekFlags.Flush, spec.Default);
        Assert.NotEmpty(spec.Values);
    }

    /// <summary>
    /// A boxed specification is built over a boxed type a value can carry, such
    /// as <c>GstCaps</c>.
    /// </summary>
    [Fact]
    public void ABoxedSpecificationIsBuiltOverABoxedType()
    {
        GType caps = GType.FromName("GstCaps");

        using ParamSpecBoxed spec = ParamSpecBoxed.New("some-caps", "Caps", "Any caps", caps, ReadWrite);

        Assert.Equal(caps, spec.ValueType);
        Assert.Equal("GParamBoxed", spec.NativeType.Name);
        Assert.Equal(1u, RefCountOf(spec.Handle));
    }

    /// <summary>
    /// An object specification is built over the type of an element, and a
    /// pointer and a parameter specification are built over nothing further.
    /// </summary>
    [Fact]
    public void ObjectPointerAndParameterSpecificationsAreBuilt()
    {
        GType element = GType.FromName("GstElement");

        using ParamSpecObject spec = ParamSpecObject.New("an-element", null, null, element, ReadWrite);
        Assert.Equal(element, spec.ValueType);

        using ParamSpecPointer pointer = ParamSpecPointer.New("a-pointer", null, null, ReadWrite);
        Assert.Equal(GType.Pointer, pointer.ValueType);

        using ParamSpecParam parameter = ParamSpecParam.New(
            "a-spec",
            null,
            null,
            GType.FromName("GParamInt"),
            ReadWrite);
        Assert.Equal("GParamInt", parameter.ValueType.Name);
    }

    /// <summary>
    /// A type specification bounds the types it accepts, and the invalid type
    /// stands for "any type": it is mapped onto <c>G_TYPE_NONE</c>, which is how
    /// GObject spells that, rather than passed on as a bound nothing satisfies.
    /// </summary>
    [Fact]
    public void ATypeSpecificationBoundsWhatItAccepts()
    {
        GType element = GType.FromName("GstElement");

        using ParamSpecGType bounded = ParamSpecGType.New("a-type", null, null, element, ReadWrite);
        Assert.Equal(element, bounded.IsAType);

        using ParamSpecGType any = ParamSpecGType.New("any-type", null, null, GType.Invalid, ReadWrite);
        Assert.Equal(GType.None, any.IsAType);
    }

    /// <summary>
    /// A fraction specification carries its six terms, and the range may begin
    /// at <c>0/1</c>, which is what a frame rate does.
    /// </summary>
    [Fact]
    public void AFractionSpecificationCarriesItsSixTerms()
    {
        using ParamSpecFraction spec = ParamSpecFraction.New(
            "a-fraction",
            "A fraction",
            "A frame rate",
            0,
            1,
            int.MaxValue,
            1,
            30,
            1,
            ReadWrite);

        Assert.Equal(0, spec.MinimumNumerator);
        Assert.Equal(1, spec.MinimumDenominator);
        Assert.Equal(int.MaxValue, spec.MaximumNumerator);
        Assert.Equal(1, spec.MaximumDenominator);
        Assert.Equal(30, spec.DefaultNumerator);
        Assert.Equal(1, spec.DefaultDenominator);
        Assert.Equal(1u, RefCountOf(spec.Handle));
    }

    /// <summary>
    /// An array specification takes a reference of its own on the description of
    /// its elements, so the wrapper that was passed in keeps working and is
    /// disposed by its owner.
    /// </summary>
    [Fact]
    public void AnArraySpecificationReferencesTheDescriptionOfItsElements()
    {
        using ParamSpecInt element = ParamSpecInt.New("an-element", null, null, 0, 100, 0, ReadWrite);
        Assert.Equal(1u, RefCountOf(element.Handle));

        ParamSpecArray spec = ParamSpecArray.New("an-array", "An array", "An array of integers", element, ReadWrite);

        try
        {
            Assert.Equal(2u, RefCountOf(element.Handle));

            using ParamSpec? described = spec.ElementSpec;
            Assert.NotNull(described);
            Assert.Equal(element.Handle, described.Handle);
        }
        finally
        {
            spec.Dispose();
        }

        // The array released the reference it took when it was finalised.
        Assert.Equal(1u, RefCountOf(element.Handle));
    }

    /// <summary>
    /// A name GObject would refuse is refused here, because GObject answers the
    /// null pointer for it and every constructor dereferences that answer.
    /// </summary>
    [Theory]
    [InlineData("1-leading-digit")]
    [InlineData("-leading-dash")]
    [InlineData("")]
    [InlineData("a space")]
    [InlineData("a.dot")]
    public void AnInvalidNameIsRefused(string name) =>
        Assert.Throws<ArgumentException>(() => ParamSpecInt.New(name, null, null, 0, 1, 0, ReadWrite));

    /// <summary>
    /// No name at all is refused as a null argument.
    /// </summary>
    [Fact]
    public void NoNameAtAllIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => ParamSpecInt.New(null!, null, null, 0, 1, 0, ReadWrite));

    /// <summary>
    /// An inverted range and a default outside the range are both refused,
    /// which C only asserts for the second of the two.
    /// </summary>
    [Fact]
    public void AnImpossibleRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecInt.New("inverted", null, null, 10, -10, 0, ReadWrite));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecInt.New("outside", null, null, 0, 10, 11, ReadWrite));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecDouble.New("outside-too", null, null, 0d, 1d, -0.5d, ReadWrite));
    }

    /// <summary>
    /// A type of the wrong kind is refused before the call, because the C
    /// constructors only assert it and answer the null pointer.
    /// </summary>
    [Fact]
    public void ATypeOfTheWrongKindIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => ParamSpecEnum.New("not-an-enum", null, null, GType.Int, 0, ReadWrite));

        Assert.Throws<ArgumentException>(
            () => ParamSpecFlags.New("not-a-set", null, null, GType.FromName("GstState"), 0u, ReadWrite));

        Assert.Throws<ArgumentException>(
            () => ParamSpecBoxed.New("not-boxed", null, null, GType.Int, ReadWrite));

        Assert.Throws<ArgumentException>(
            () => ParamSpecObject.New("not-an-object", null, null, GType.FromName("GstCaps"), ReadWrite));

        Assert.Throws<ArgumentException>(
            () => ParamSpecParam.New("not-a-spec", null, null, GType.Int, ReadWrite));
    }

    /// <summary>
    /// A bound that is not a number is refused as the argument it is. NaN
    /// compares false against everything, so it passes an ordinary range check
    /// and the C constructor is the one that would refuse it — by answering
    /// nothing at all.
    /// </summary>
    [Fact]
    public void ABoundThatIsNotANumberIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecFloat.New("nan-minimum", null, null, float.NaN, 1f, 0f, ReadWrite));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecFloat.New("nan-default", null, null, 0f, 1f, float.NaN, ReadWrite));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecDouble.New("nan-maximum", null, null, 0d, double.NaN, 0d, ReadWrite));

        // An infinity is a number C accepts and orders like any other.
        using ParamSpecDouble unbounded = ParamSpecDouble.New(
            "unbounded", null, null, double.NegativeInfinity, double.PositiveInfinity, 0d, ReadWrite);

        Assert.Equal(double.PositiveInfinity, unbounded.Maximum);
    }

    /// <summary>
    /// A term of a fraction GStreamer would refuse is refused here: a zero
    /// denominator makes <c>gst_value_set_fraction</c> leave the value it was to
    /// write at <c>0/1</c>, so the validation that follows it in C runs against
    /// the wrong fraction.
    /// </summary>
    [Fact]
    public void AnImpossibleFractionIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecFraction.New("zero-denominator", null, null, 0, 0, 1, 1, 0, 1, ReadWrite));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecFraction.New("zero-default-denominator", null, null, 0, 1, 1, 1, 1, 0, ReadWrite));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecFraction.New("too-negative", null, null, int.MinValue, 1, 1, 1, 0, 1, ReadWrite));

        // 2/1 is larger than 1/1.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecFraction.New("inverted", null, null, 2, 1, 1, 1, 1, 1, ReadWrite));

        // 3/2 lies outside 0/1 to 1/1.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpecFraction.New("outside", null, null, 0, 1, 1, 1, 3, 2, ReadWrite));
    }

    /// <summary>
    /// An array without a description of its elements is refused.
    /// </summary>
    [Fact]
    public void AnArrayWithoutADescriptionOfItsElementsIsRefused() =>
        Assert.Throws<ArgumentNullException>(
            () => ParamSpecArray.New("no-elements", null, null, null!, ReadWrite));

    /// <summary>
    /// The three flags that would make GObject keep the caller's strings are
    /// dropped silently, because the strings are encoded into buffers that are
    /// released as soon as the constructor has returned.
    /// </summary>
    [Fact]
    public void TheStaticStringFlagsAreDropped()
    {
        using ParamSpecInt spec = ParamSpecInt.New(
            "static-strings",
            "A nickname",
            "A description",
            0,
            1,
            0,
            ReadWrite | ParamFlags.StaticStrings | ParamFlags.ExplicitNotify);

        Assert.Equal(ReadWrite | ParamFlags.ExplicitNotify, spec.Flags);
        Assert.Equal(ParamFlags.None, spec.Flags & ParamFlags.StaticStrings);

        // The strings survive the buffers they were encoded into.
        GC.Collect();
        Assert.Equal("static-strings", spec.Name);
        Assert.Equal("A nickname", spec.Nick);
        Assert.Equal("A description", spec.Blurb);
    }

    /// <summary>
    /// A specification a <c>New</c> handed out owns exactly one reference, and
    /// disposing the wrapper releases it. The count is read through a second
    /// wrapper, which holds a reference of its own, so that nothing reads a
    /// specification that was already freed.
    /// </summary>
    [Fact]
    public void TheWrapperOwnsTheOnlyReferenceAndReleasesIt()
    {
        ParamSpecInt spec = ParamSpecInt.New("counted", null, null, 0, 1, 0, ReadWrite);
        Assert.Equal(1u, RefCountOf(spec.Handle));

        using ParamSpec second = ParamSpec.FromNative(spec.Handle, Transfer.None);
        Assert.Equal(2u, RefCountOf(second.Handle));

        spec.Dispose();
        Assert.Equal(1u, RefCountOf(second.Handle));

        // Disposing twice is harmless: the handle is exchanged for zero.
        spec.Dispose();
        Assert.Equal(1u, RefCountOf(second.Handle));
    }

    /// <summary>
    /// Reads <c>ref_count</c> out of a <c>GParamSpec</c>. Eight pointer sized
    /// slots — the type instance, the name, the padded flags, the type of the
    /// values, the owner type, the nickname, the description and the data list —
    /// come before it, and <c>param_id</c> shares the ninth with it.
    /// </summary>
    private static uint RefCountOf(nint handle) => *(uint*)((byte*)handle + (8 * sizeof(nint)));
}
