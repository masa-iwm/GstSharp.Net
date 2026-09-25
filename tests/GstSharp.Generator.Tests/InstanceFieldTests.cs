using GstSharp.Generator.Emit;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What the <c>instanceFields</c> allowlist makes of the real girs: the four
/// accessors it exposes, the mirrors the offsets are measured from, and the
/// ledger lines the four fields left.
/// </summary>
/// <remarks>
/// The allowlist is the one key that adds public surface out of a structure the
/// generator otherwise never looks at, so the frozen part of it is asserted
/// here: the signature, the remark that says which lock the library holds and
/// which overrides form the window, and the fact that the mirror lays out the
/// private tail as well - a mirror that stopped at the exposed field would
/// still measure the right offset and would break the size probe of the
/// integration tests, which is the only thing that pins the offset to the
/// running library.
/// </remarks>
public sealed class InstanceFieldTests
{
    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GirFixture.RunWithoutErrors(),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    [Theory]
    [InlineData("BaseSink.cs")]
    [InlineData("BaseSrc.cs")]
    [InlineData("BaseTransform.cs")]
    [InlineData("BaseParse.cs")]
    public void EveryAllowlistedFieldCarriesACopyReturningMethod(string fileName)
    {
        string source = Source(fileName);

        // A boxed value read out of embedded storage comes back owning a
        // reference of its own, so it is a Get method and not a property, and it
        // is never null: the storage is part of the instance.
        Assert.Contains("    public Gst.Segment GetSegment()\n", source, StringComparison.Ordinal);
        Assert.Contains("Gst.Interop.Transfer.None)", source, StringComparison.Ordinal);
        Assert.Contains("System.GC.KeepAlive(this);", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "BaseSink.cs",
        "STREAM_LOCK and, for an instant rate change, PREROLL_LOCK",
        "<see cref=\"OnRender\"/> or <see cref=\"OnPreroll\"/>")]
    [InlineData("BaseSrc.cs", "STREAM_LOCK and OBJECT_LOCK", "<see cref=\"OnCreate\"/> or <see cref=\"OnFill\"/>")]
    [InlineData("BaseTransform.cs", "STREAM_LOCK", "<see cref=\"OnTransform\"/> or <see cref=\"OnTransformIp\"/>")]
    [InlineData("BaseParse.cs", "STREAM_LOCK", "<see cref=\"OnHandleFrame\"/>")]
    public void TheRemarkNamesTheLockAndTheWindow(string fileName, string @lock, string window)
    {
        string source = Source(fileName);

        // The sentence is wrapped, because the lock is free text of an overlay
        // entry: what the file carries is the words of it, in order, with a
        // documentation prefix wherever the wrap broke the line.
        string wrapped = string.Join(
            " ",
            ("The library rewrites the field under " + @lock + ", which managed code cannot take, so the "
            + "copy is only guaranteed consistent when it is read on the streaming thread, inside")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        string written = string.Join(
            " ",
            source.Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => line.StartsWith("/// ", StringComparison.Ordinal))
                .SelectMany(static line => line[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries)));

        Assert.Contains(wrapped, written, StringComparison.Ordinal);
        Assert.Contains("/// " + window + ".\n", source, StringComparison.Ordinal);

        // No line of the remark is wider than an editor of this repository
        // leaves alone.
        Assert.All(
            source.Split('\n').Where(static line => line.Contains("rewrites the field under", StringComparison.Ordinal)),
            static line => Assert.True(line.Length <= 120, line));

        // The window is worth nothing if the reader cannot tell what a read
        // outside it costs, and the answer is a torn value rather than a crash.
        Assert.Contains("may mix the fields of two segments", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMirrorLaysOutEveryFieldTheClassDeclares()
    {
        string source = Source("InstanceFields/BaseSinkOwnFieldsRaw.cs", "GstSharp.Net.Base");

        // The offset of the exposed field is measured from the mirror rather
        // than written out, and the two locks in front of it are what makes the
        // measurement right: a byte blob would be aligned on 1.
        Assert.Contains("internal Gst.GLib.MutexRaw PrerollLock;", source, StringComparison.Ordinal);
        Assert.Contains("internal Gst.GLib.CondRaw PrerollCond;", source, StringComparison.Ordinal);
        Assert.Contains("internal Gst.SegmentRaw Segment;", source, StringComparison.Ordinal);
        Assert.Contains(
            "SegmentOffset = Gst.GObject.InstanceLayout.OffsetOf(ref probe, ref probe.Segment);",
            source,
            StringComparison.Ordinal);

        // Everything behind the exposed field is laid out too, because the total
        // size of the mirror is what the integration tests compare with the
        // instance size the library reports.
        Assert.Contains("internal nint ClockId;", source, StringComparison.Ordinal);
        Assert.Contains("internal long MaxLateness;", source, StringComparison.Ordinal);
        Assert.Contains("internal nint Priv;", source, StringComparison.Ordinal);
        Assert.Contains("InlineArray(20)", source, StringComparison.Ordinal);

        // The parent term is read from the running library, not mirrored: it is
        // the one that differs between ABIs.
        Assert.Contains(
            "offset = Gst.GObject.InstanceLayout.SizeOf(Gst.Element.GetGType());",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheRegistryNamesEveryExposedField()
    {
        string source = Source("InstanceFields/InstanceFieldRegistry.cs", "GstSharp.Net.Base");

        foreach (string cName in new[] { "GstBaseParse", "GstBaseSink", "GstBaseSrc", "GstBaseTransform" })
        {
            Assert.Contains("\"" + cName + "\",", source, StringComparison.Ordinal);
        }

        Assert.Contains("&Gst.Element.GetGType,", source, StringComparison.Ordinal);
        Assert.Contains(
            "new Gst.GObject.InstanceFieldProbe(\"segment\", Gst.Base.BaseSinkOwnFieldsRaw.SegmentOffset),",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnExposedFieldLeavesTheClassFieldLedger()
    {
        string report = Generated.SkipReport;

        // A field with a generated accessor is not a gap any more, so it leaves
        // the section the way a record field with one does. It does not move
        // into 'Fields exposed elsewhere' either: that section is for the fields
        // something other than an accessor answers.
        Assert.DoesNotContain("- `BaseSink.segment`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `BaseSrc.segment`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `BaseTransform.segment`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `BaseParse.segment`", report, StringComparison.Ordinal);

        // Wave 2 of the allowlist. The two Video classes move no line, because
        // the gir marks every field of them private and the ledger never
        // counted one of those.
        Assert.DoesNotContain("- `AudioDecoder.input_segment`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `AudioDecoder.output_segment`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `AudioEncoder.input_segment`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `AudioEncoder.output_segment`", report, StringComparison.Ordinal);
        // The preroll lock of a base sink is one more class field a hand written
        // member answers instead, so it counts among the exposed ones.
        Assert.Equal(24, Generated.Census.ExposedFieldCount());

        // The one field of the same shape that wave 1 refused stays on it: no
        // override of an aggregator runs under the lock its writer takes.
        Assert.Contains("- `AggregatorPad.segment` — EmbeddedStruct\n", report, StringComparison.Ordinal);

        // The preamble used to call the shape of an exposure an open question.
        Assert.Contains(
            "A field the\noverlays register under `instanceFields` is generated after all",
            report,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheCensusCountsTheMirrorsAndTheAccessors()
    {
        Assert.Equal(4, Generated.Census.EmittedCount("GstBase", "instance field"));
        Assert.Equal(4, Generated.Census.EmittedCount("GstBase", "instance field mirror"));
        Assert.Equal(4, Generated.Census.EmittedCount("GstAudio", "instance field"));
        Assert.Equal(2, Generated.Census.EmittedCount("GstAudio", "instance field mirror"));
        Assert.Equal(4, Generated.Census.EmittedCount("GstVideo", "instance field"));
        Assert.Equal(2, Generated.Census.EmittedCount("GstVideo", "instance field mirror"));
        Assert.Equal(0, Generated.Census.EmittedCount("Gst", "instance field mirror"));
    }

    /// <summary>
    /// The name collision check reads the member keys of the class, which spell a
    /// method with its signature and a property with a prefix, so a bare name
    /// would match neither and the check would never fire.
    /// </summary>
    [Theory]
    [InlineData("GetSegment", true)]
    [InlineData("M:GetSegment()", true)]
    [InlineData("M:GetSegment(Gst.Format)", true)]
    [InlineData("P:GetSegment", true)]
    [InlineData("M:GetSegmentDone()", false)]
    [InlineData("P:Segment", false)]
    public void ACollisionIsReadOutOfTheMemberKeysAndNotOutOfABareName(string key, bool collides)
    {
        Assert.Equal(collides, SurfaceBuilder.KeyNames(key, "GetSegment"));
    }

    /// <summary>
    /// The accessor joins the inherited table as a method rather than as a bare
    /// name, so a descendant that declares one of the same name hides it.
    /// </summary>
    [Fact]
    public void TheAccessorJoinsTheInheritedTableAsAMethod()
    {
        Assert.Equal("M:GetSegment()", SurfaceBuilder.ParameterlessMethodKey("GetSegment"));
        Assert.True(SurfaceBuilder.KeyNames(SurfaceBuilder.ParameterlessMethodKey("GetSegment"), "GetSegment"));
    }

    private static string Source(string fileName, string project = "GstSharp.Net.Base")
    {
        string path = project + "/Generated/" + fileName;
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException($"The run produced no '{path}'.");
    }
}
