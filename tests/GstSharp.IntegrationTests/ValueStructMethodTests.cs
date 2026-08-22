using Gst;
using Gst.GLib;
using Gst.Rtsp;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The methods of the value projected structures, against the library that is
/// installed.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these used to be dropped: the record emitter bound no member of
/// a plain struct, so the structures shipped as bare fields with no producer and
/// no reader. The instance travels as the pinned address of <c>this</c>, which
/// is what these tests measure — a mutator has to be visible in the variable it
/// was called on, and a reader has to see the fields that variable holds.
/// </para>
/// <para>
/// The 1.28 members are behind <see cref="RequiresGst128FactAttribute"/>: the
/// Linux leg of the matrix runs the 1.24 floor, where the entry point does not
/// exist.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ValueStructMethodTests
{
    /// <summary>
    /// A colorimetry parsed out of its string form describes the same thing
    /// again when it is written back out. The parse writes through the instance,
    /// so a member that handed the library a copy would leave the value unknown
    /// and the round trip would come back empty.
    /// </summary>
    [Fact]
    public void AColorimetryRoundTripsThroughItsStringForm()
    {
        VideoColorimetry colorimetry = default;

        Assert.True(colorimetry.FromString("bt709"));

        Assert.Equal(VideoColorRange.Range16_235, colorimetry.Range);
        Assert.Equal(VideoColorMatrix.Bt709, colorimetry.Matrix);
        Assert.Equal("bt709", colorimetry.ToString());
    }

    /// <summary>
    /// The <c>ToString</c> of a structure the C side cannot describe is the
    /// empty string. A struct has a default value that every caller can reach,
    /// the C function answers <c>NULL</c> for it, and an override that threw
    /// there would make the debugger unusable.
    /// </summary>
    [Fact]
    public void AnUnknownColorimetryDescribesItselfAsTheEmptyString()
    {
        VideoColorimetry unknown = default;

        Assert.Equal(string.Empty, unknown.ToString());
    }

    /// <summary>
    /// Two colorimetries parsed from the same string are equal, and one parsed
    /// from another string is not. <c>IsEqual</c> is a <c>readonly</c> member,
    /// so it may be called on a value the caller cannot write to.
    /// </summary>
    [Fact]
    public void ColorimetriesCompareByContent()
    {
        VideoColorimetry left = default;
        VideoColorimetry right = default;
        VideoColorimetry other = default;

        Assert.True(left.FromString("bt709"));
        Assert.True(right.FromString("bt709"));
        Assert.True(other.FromString("bt601"));

        Assert.True(left.IsEqual(right));
        Assert.False(left.IsEqual(other));
        Assert.True(left.Matches("bt709"));
        Assert.False(left.Matches("bt601"));
    }

    /// <summary>
    /// Mastering display metadata survives a trip through its string form and
    /// through a caps structure. <c>FromCaps</c> and the static
    /// <c>FromString</c> are the two shapes a filled structure comes back in.
    /// </summary>
    [Fact]
    public void MasteringDisplayInfoRoundTripsThroughStringAndCaps()
    {
        VideoMasteringDisplayInfo info = default;
        info.Init();

        info.DisplayPrimaries[0].X = 13250;
        info.DisplayPrimaries[0].Y = 34500;
        info.DisplayPrimaries[1].X = 7500;
        info.DisplayPrimaries[1].Y = 3000;
        info.DisplayPrimaries[2].X = 34000;
        info.DisplayPrimaries[2].Y = 16000;
        info.WhitePoint.X = 15635;
        info.WhitePoint.Y = 16450;
        info.MaxDisplayMasteringLuminance = 10000000;
        info.MinDisplayMasteringLuminance = 500;

        string text = info.ToString();
        Assert.NotEmpty(text);

        Assert.True(VideoMasteringDisplayInfo.FromString(out VideoMasteringDisplayInfo parsed, text));
        Assert.True(info.IsEqual(parsed));

        using Caps caps = Caps.NewEmptySimple("video/x-raw");
        Assert.True(info.AddToCaps(caps));

        VideoMasteringDisplayInfo fromCaps = default;
        Assert.True(fromCaps.FromCaps(caps));
        Assert.True(info.IsEqual(fromCaps));
    }

    /// <summary>
    /// A string the parser rejects answers <see langword="false"/> and leaves
    /// the destination zeroed, which is what the documentation of the member
    /// states.
    /// </summary>
    [Fact]
    public void AFailedMasteringDisplayParseLeavesTheDestinationZeroed()
    {
        Assert.False(VideoMasteringDisplayInfo.FromString(
            out VideoMasteringDisplayInfo parsed,
            "not a mastering display"));

        Assert.Equal(0u, parsed.MaxDisplayMasteringLuminance);
        Assert.Equal(0u, parsed.MinDisplayMasteringLuminance);
    }

    /// <summary>
    /// The content light level travels the same three ways, and every one of
    /// them writes through the instance.
    /// </summary>
    [Fact]
    public void ContentLightLevelRoundTripsThroughStringAndCaps()
    {
        VideoContentLightLevel level = default;
        level.Init();
        level.MaxContentLightLevel = 1000;
        level.MaxFrameAverageLightLevel = 300;

        string text = level.ToString();
        Assert.NotEmpty(text);

        VideoContentLightLevel parsed = default;
        Assert.True(parsed.FromString(text));
        Assert.True(level.IsEqual(parsed));

        using Caps caps = Caps.NewEmptySimple("video/x-raw");
        Assert.True(level.AddToCaps(caps));

        VideoContentLightLevel fromCaps = default;
        Assert.True(fromCaps.FromCaps(caps));
        Assert.True(level.IsEqual(fromCaps));
    }

    /// <summary>
    /// <c>Init</c> is a mutator with no argument and no result: the only thing
    /// it can be measured by is the variable it was called on.
    /// </summary>
    [Fact]
    public void InitializingAContentLightLevelClearsTheVariable()
    {
        VideoContentLightLevel level = default;
        level.MaxContentLightLevel = 4000;
        level.MaxFrameAverageLightLevel = 2000;

        level.Init();

        Assert.Equal(0, level.MaxContentLightLevel);
        Assert.Equal(0, level.MaxFrameAverageLightLevel);
    }

    /// <summary>
    /// Resetting an alignment clears both the scalar fields and the inline
    /// stride array of the variable it was called on.
    /// </summary>
    [Fact]
    public void ResettingAnAlignmentClearsTheVariable()
    {
        VideoAlignment alignment = default;
        alignment.PaddingTop = 1;
        alignment.PaddingBottom = 2;
        alignment.PaddingLeft = 4;
        alignment.PaddingRight = 8;
        alignment.StrideAlign[0] = 127;

        alignment.Reset();

        Assert.Equal(0u, alignment.PaddingTop);
        Assert.Equal(0u, alignment.PaddingBottom);
        Assert.Equal(0u, alignment.PaddingLeft);
        Assert.Equal(0u, alignment.PaddingRight);
        Assert.Equal(0u, alignment.StrideAlign[0]);
    }

    /// <summary>
    /// <c>GST_POLL_FD_INIT</c> sets the descriptor to -1, which is the state a
    /// default constructed value does not have: the field starts at zero, which
    /// is a real descriptor. Binding the initializer is what closes that gap.
    /// </summary>
    [Fact]
    public void InitializingAPollDescriptorSetsItToMinusOne()
    {
        PollFD descriptor = default;
        Assert.Equal(0, descriptor.Fd);

        descriptor.Init();

        Assert.Equal(-1, descriptor.Fd);
    }

    /// <summary>
    /// The quark of the scale transform is a registered string, so it is not
    /// zero and it is the same on every call.
    /// </summary>
    [Fact]
    public void TheScaleTransformHasAQuark()
    {
        Quark quark = VideoMetaTransform.ScaleGetQuark();

        Assert.NotEqual(Quark.Zero, quark);
        Assert.Equal(quark, VideoMetaTransform.ScaleGetQuark());
    }

    /// <summary>
    /// A range built out of its fields is converted to clock times and written
    /// back out as a string. Nothing constructs a <c>GstRTSPTimeRange</c> on the
    /// managed side — the parser is overlay-skipped, because it allocates on the
    /// heap — so the value is spelled out field by field, which a plain struct
    /// makes possible.
    /// </summary>
    [Fact]
    public void ARangeBuiltFromItsFieldsReadsBackAsTimesAndText()
    {
        RTSPTimeRange range = default;
        range.Unit = RTSPRangeUnit.Npt;
        range.Min.Type = RTSPTimeType.Seconds;
        range.Min.Seconds = 10.0;
        range.Max.Type = RTSPTimeType.Seconds;
        range.Max.Seconds = 20.0;

        Assert.True(RTSPRange.GetTimes(range, out ClockTime min, out ClockTime max));
        Assert.Equal(10 * ClockTime.NanosecondsPerSecond, min.Nanoseconds);
        Assert.Equal(20 * ClockTime.NanosecondsPerSecond, max.Nanoseconds);

        string? text = RTSPRange.ToString(range);
        Assert.NotNull(text);
        Assert.StartsWith("npt=", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Converting a range into another unit rewrites the caller's own variable.
    /// The parameter is a <c>ref</c> because the C function converts in place,
    /// which the gir does not say; converting into a different unit is what
    /// measures that the <c>ref</c> reaches the caller, because the unit and
    /// the type of both ends come back changed. Converting into the unit the
    /// range already carries would answer <see langword="true"/> before the C
    /// function writes anything, so it cannot tell a write-back from a copy.
    /// </summary>
    [Fact]
    public void ConvertingARangeIntoAnotherUnitRewritesTheCallersVariable()
    {
        RTSPTimeRange range = default;
        range.Unit = RTSPRangeUnit.Npt;
        range.Min.Type = RTSPTimeType.Seconds;
        range.Min.Seconds = 10.0;
        range.Max.Type = RTSPTimeType.Seconds;
        range.Max.Seconds = 20.0;

        Assert.True(RTSPRange.ConvertUnits(ref range, RTSPRangeUnit.Clock));

        Assert.Equal(RTSPRangeUnit.Clock, range.Unit);
        Assert.Equal(RTSPTimeType.Utc, range.Min.Type);
        Assert.Equal(RTSPTimeType.Utc, range.Max.Type);

        // The same conversion into the unit the range already carries is the
        // early answer of the C function, so it leaves the variable alone.
        Assert.True(RTSPRange.ConvertUnits(ref range, RTSPRangeUnit.Clock));
        Assert.Equal(RTSPRangeUnit.Clock, range.Unit);
    }

    /// <summary>
    /// The mapping helpers of 1.28: <c>Init</c> clears the info,
    /// <c>GetData</c> hands out the mapped bytes and <c>Clear</c> is the full
    /// unmap.
    /// </summary>
    [RequiresGst128Fact]
    public void AMapInfoInitializesReadsAndClears()
    {
        MapInfo info = default;
        info.Size = 7;
        info.Init();

        Assert.Equal((nuint)0, info.Size);
        Assert.Null(info.GetData());

        byte[] payload = [1, 2, 3, 4];
        using Buffer buffer = Buffer.NewMemdup(payload);
        using Memory memory = buffer.GetAllMemory()
            ?? throw new InvalidOperationException("The buffer carries no memory.");

        Assert.True(memory.Map(out MapInfo mapped, MapFlags.Read));
        Assert.Equal((nuint)payload.Length, mapped.Size);
        Assert.Equal(payload, mapped.GetData());

        // The full unmap; never call it on the mapping a Buffer.MapScope holds.
        // It releases the mapping without rewriting the info, so what says the
        // unmap happened is that the block maps again afterwards.
        mapped.Clear();

        Assert.True(memory.Map(out MapInfo again, MapFlags.Read));
        Assert.Equal(payload, again.GetData());
        memory.Unmap(again);
    }

    /// <summary>
    /// The transform matrix of 1.28 maps a point and a rectangle from the input
    /// rectangle onto the output one. The instance is <c>readonly</c> for both
    /// reads and the two coordinates come back through <c>ref</c> parameters.
    /// </summary>
    [RequiresGst128Fact]
    public void ATransformMatrixMapsPointsAndRectangles()
    {
        using VideoInfo inInfo = VideoInfo.New();
        Assert.True(inInfo.SetFormat(VideoFormat.Rgb, 100, 100));

        using VideoInfo outInfo = VideoInfo.New();
        Assert.True(outInfo.SetFormat(VideoFormat.Rgb, 200, 200));

        VideoRectangle inRectangle = default;
        inRectangle.W = 100;
        inRectangle.H = 100;

        VideoRectangle outRectangle = default;
        outRectangle.W = 200;
        outRectangle.H = 200;

        VideoMetaTransformMatrix matrix = default;
        matrix.Init(inInfo, inRectangle, outInfo, outRectangle);

        int x = 50;
        int y = 50;
        Assert.True(matrix.Point(ref x, ref y));
        Assert.Equal(100, x);
        Assert.Equal(100, y);

        VideoRectangle rectangle = default;
        rectangle.W = 50;
        rectangle.H = 50;
        Assert.True(matrix.Rectangle(ref rectangle));
        Assert.Equal(100, rectangle.W);
        Assert.Equal(100, rectangle.H);

        Assert.NotEqual(Quark.Zero, VideoMetaTransformMatrix.GetQuark());
    }
}
