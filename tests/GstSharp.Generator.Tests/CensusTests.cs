using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Frozen counts of the vendored reference gir files. They fail loudly when a
/// gir refresh, a parser change or an accidental skip moves the surface.
/// </summary>
/// <remarks>
/// All counts are top level members of the <c>&lt;namespace&gt;</c> element.
/// Records nested inside a <c>&lt;union&gt;</c> or a <c>&lt;class&gt;</c> are
/// not counted; that is why <c>Gst</c> has 111 records here while a plain grep
/// over the file finds 115.
/// </remarks>
public sealed class CensusTests
{
    [Theory]
    [InlineData("Gst", 46, 111, 5, 47, 40, 79, 6, 195, 302)]
    [InlineData("GstBase", 10, 30, 0, 1, 3, 12, 0, 4, 22)]
    [InlineData("GstApp", 2, 8, 0, 2, 0, 8, 0, 0, 0)]
    [InlineData("GstAudio", 13, 40, 1, 16, 7, 5, 0, 39, 54)]
    [InlineData("GstVideo", 12, 65, 5, 34, 17, 5, 0, 79, 175)]
    [InlineData("GstPbutils", 13, 9, 0, 3, 2, 2, 7, 9, 70)]
    [InlineData("GstNet", 4, 10, 0, 0, 0, 1, 0, 6, 16)]
    [InlineData("GstSdp", 0, 21, 0, 13, 0, 0, 0, 7, 12)]
    [InlineData("GstRtsp", 0, 13, 1, 10, 5, 1, 0, 1, 32)]
    [InlineData("GstWebRTC", 9, 13, 0, 23, 0, 1, 0, 0, 2)]
    [InlineData("GES", 55, 111, 2, 9, 4, 10, 1, 19, 19)]
    [InlineData("GLib", 0, 79, 0, 39, 22, 56, 14, 131, 689)]
    [InlineData("GObject", 30, 29, 1, 0, 8, 34, 3, 15, 185)]
    [InlineData("GModule", 0, 1, 0, 1, 1, 2, 0, 0, 4)]
    [InlineData("Gio", 103, 213, 37, 43, 39, 30, 0, 129, 140)]
    public void NamespaceCensusIsStable(
        string name,
        int classes,
        int records,
        int interfaces,
        int enumerations,
        int bitfields,
        int callbacks,
        int aliases,
        int constants,
        int functions)
    {
        GirNamespace ns = GirFixture.Namespace(name);

        Assert.Equal(classes, ns.Classes.Count);
        Assert.Equal(records, ns.Records.Count);
        Assert.Equal(interfaces, ns.Interfaces.Count);
        Assert.Equal(enumerations, ns.Enumerations.Count);
        Assert.Equal(bitfields, ns.Bitfields.Count);
        Assert.Equal(callbacks, ns.Callbacks.Count);
        Assert.Equal(aliases, ns.Aliases.Count);
        Assert.Equal(constants, ns.Constants.Count);
        Assert.Equal(functions, ns.Functions.Count);
    }

    // Only the GStreamer girs are listed. The GLib stack - GLib, GObject,
    // GModule and Gio - is vendored for cross-namespace type resolution alone,
    // so none of its signals is ever bound.
    [Theory]
    [InlineData("Gst", 23)]
    [InlineData("GstBase", 4)]
    [InlineData("GstApp", 17)]
    [InlineData("GstAudio", 0)]
    [InlineData("GstVideo", 2)]
    [InlineData("GstPbutils", 5)]
    [InlineData("GstNet", 0)]
    [InlineData("GstSdp", 0)]
    [InlineData("GstRtsp", 1)]
    [InlineData("GstWebRTC", 12)]
    [InlineData("GES", 39)]
    public void SignalCensusIsStable(string name, int signals)
    {
        GirNamespace ns = GirFixture.Namespace(name);

        int count = 0;
        foreach (GirClass declaration in ns.Classes)
        {
            count += declaration.Signals.Count;
        }

        foreach (GirInterface declaration in ns.Interfaces)
        {
            count += declaration.Signals.Count;
        }

        Assert.Equal(signals, count);
    }

    [Fact]
    public void EveryReferenceGirIsLoaded()
    {
        // Ordinal order, which is the order Repository.Load returns: "Gio"
        // sorts after "GObject" and before "Gst" because the upper case
        // letters come first.
        string[] expected =
        [
            "GES", "GLib", "GModule", "GObject", "Gio", "Gst", "GstApp", "GstAudio", "GstBase", "GstNet",
            "GstPbutils", "GstRtsp", "GstSdp", "GstVideo", "GstWebRTC",
        ];

        Assert.Equal(expected, GirFixture.Repository.Namespaces.Select(static ns => ns.Name).ToArray());
    }

    [Fact]
    public void GstNamespaceCarriesTheExpectedVersion()
    {
        GirNamespace gst = GirFixture.Namespace("Gst");

        Assert.Equal("1.0", gst.Version);
        Assert.Equal("libgstreamer-1.0.so.0", gst.SharedLibrary);
    }

    [Fact]
    public void UnionsAreOnlyDeclaredInsideRecordsAndClasses()
    {
        // The GStreamer girs declare no namespace level unions; the four unions
        // of Gst are nested inside records.
        GirNamespace gst = GirFixture.Namespace("Gst");
        Assert.Empty(gst.Unions);

        int nested = gst.Records.Sum(static record => record.Unions.Count)
            + gst.Classes.Sum(static declaration => declaration.Unions.Count);
        Assert.Equal(4, nested);
    }

    [Fact]
    public void GstTypeKindCensusIsStable()
    {
        Repository repository = GirFixture.Repository;
        Classifier classifier = new(repository, Overlays.Empty, new DiagnosticBag());
        GirNamespace gst = GirFixture.Namespace("Gst");

        Dictionary<TypeKind, int> counts = [];
        foreach (GirRecord record in gst.Records)
        {
            TypeKind kind = classifier.Classify(record);
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        Assert.Equal(39, counts.GetValueOrDefault(TypeKind.GTypeStruct));
        Assert.Equal(12, counts.GetValueOrDefault(TypeKind.MiniObject));
        Assert.Equal(12, counts.GetValueOrDefault(TypeKind.Boxed));
        Assert.Equal(14, counts.GetValueOrDefault(TypeKind.PlainStruct));
        Assert.Equal(34, counts.GetValueOrDefault(TypeKind.OpaqueRecord));
        Assert.Equal(gst.Records.Count, counts.Values.Sum());

        int fundamentals = gst.Classes.Count(record => classifier.Classify(record) == TypeKind.Fundamental);
        Assert.Equal(12, fundamentals);
    }
}
