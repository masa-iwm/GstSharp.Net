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
    [InlineData("BaseSink.cs", "STREAM_LOCK", "<see cref=\"OnRender\"/> or <see cref=\"OnPreroll\"/>")]
    [InlineData("BaseSrc.cs", "STREAM_LOCK and OBJECT_LOCK", "<see cref=\"OnCreate\"/> or <see cref=\"OnFill\"/>")]
    [InlineData("BaseTransform.cs", "STREAM_LOCK", "<see cref=\"OnTransform\"/> or <see cref=\"OnTransformIp\"/>")]
    [InlineData("BaseParse.cs", "STREAM_LOCK", "<see cref=\"OnHandleFrame\"/>")]
    public void TheRemarkNamesTheLockAndTheWindow(string fileName, string @lock, string window)
    {
        string source = Source(fileName);

        Assert.Contains(
            "/// The library rewrites the field under " + @lock + ", which managed code cannot\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains("/// " + window + ".\n", source, StringComparison.Ordinal);

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
        Assert.Equal(23, Generated.Census.ExposedFieldCount());

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
        Assert.Equal(0, Generated.Census.EmittedCount("Gst", "instance field mirror"));
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
