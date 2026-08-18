using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Gir name to C# identifier mapping.
/// </summary>
public sealed class NameMapperTests
{
    [Theory]
    [InlineData("state_changed", "StateChanged")]
    [InlineData("child-added", "ChildAdded")]
    [InlineData("eos", "Eos")]
    [InlineData("MessageType", "MessageType")]
    [InlineData("uri_error", "UriError")]
    [InlineData("16_9_top_aligned", "169TopAligned")]
    [InlineData("", "")]
    public void PascalCasingFollowsTheGirName(string girName, string expected) =>
        Assert.Equal(expected, NameMapper.ToPascalCase(girName));

    [Theory]
    [InlineData("element", "element")]
    [InlineData("new_state", "newState")]
    public void CamelCasingFollowsTheGirName(string girName, string expected) =>
        Assert.Equal(expected, NameMapper.ToCamelCase(girName));

    [Theory]
    [InlineData("Element", "Element")]
    [InlineData("event", "@event")]
    [InlineData("object", "@object")]
    [InlineData("2d", "_2d")]
    [InlineData("", "_")]
    public void KeywordsAndLeadingDigitsAreEscaped(string candidate, string expected) =>
        Assert.Equal(expected, NameMapper.EscapeIdentifier(candidate));

    [Fact]
    public void EnumMemberNamesArePascalCased()
    {
        NameMapper mapper = new(Overlays.Empty, new DiagnosticBag());
        GirNamespace ns = GirFixture.Namespace("Gst");
        GirEnumeration messageType = Assert.IsType<GirEnumeration>(GirFixture.Symbol("Gst.MessageType").Declaration);

        GirEnumMember stateChanged = messageType.Members.Single(
            static member => string.Equals(member.Name, "state_changed", StringComparison.Ordinal));
        Assert.Equal("StateChanged", mapper.EnumMemberName(messageType, ns, stateChanged));
    }

    /// <summary>
    /// A member whose gir name starts with a digit has to be named from the
    /// overlays. Escaping it with an underscore produced names such as
    /// <c>_16235</c>, which say nothing about the [16..235] range they stand
    /// for, so the generator refuses the run instead of emitting one.
    /// </summary>
    [Fact]
    public void EnumMemberNamesWithLeadingDigitsFailTheRun()
    {
        DiagnosticBag diagnostics = new();
        NameMapper mapper = new(Overlays.Empty, diagnostics);
        GirNamespace ns = GirFixture.Namespace("GstVideo");
        GirEnumeration range = Assert.IsType<GirEnumeration>(GirFixture.Symbol("GstVideo.VideoColorRange").Declaration);

        GirEnumMember member = range.Members.Single(
            static m => string.Equals(m.Name, "16_235", StringComparison.Ordinal));

        // The escaped name is still returned, so that the run keeps working on
        // legal C#; the error is what stops it before anything is written.
        Assert.Equal("_16235", mapper.EnumMemberName(range, ns, member));
        Diagnostic error = Assert.Single(diagnostics.Items);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("GEN0016", error.Code);
        Assert.Contains("GstVideo.VideoColorRange.16_235", error.Message, StringComparison.Ordinal);
        Assert.Contains("girs/overlays/fixups.json", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rename from the overlays is what the real run uses, and it takes the
    /// member without any diagnostic.
    /// </summary>
    [Fact]
    public void EnumMemberNamesWithLeadingDigitsAcceptARename()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                """
                {
                  "rename": {
                    "GstVideo.VideoColorRange.16_235": "Range16_235"
                  }
                }
                """);

            DiagnosticBag diagnostics = new();
            NameMapper mapper = new(Overlays.Load(directory), diagnostics);
            GirNamespace ns = GirFixture.Namespace("GstVideo");
            GirEnumeration range =
                Assert.IsType<GirEnumeration>(GirFixture.Symbol("GstVideo.VideoColorRange").Declaration);

            GirEnumMember member = range.Members.Single(
                static m => string.Equals(m.Name, "16_235", StringComparison.Ordinal));

            Assert.Equal("Range16_235", mapper.EnumMemberName(range, ns, member));
            Assert.Empty(diagnostics.Items);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RenamesFromTheOverlaysWin()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                """
                {
                  "rename": {
                    "Gst.MessageType": "MessageKind",
                    "Gst.MessageType.state_changed": "StateSwitched"
                  }
                }
                """);

            NameMapper mapper = new(Overlays.Load(directory), new DiagnosticBag());
            GirNamespace ns = GirFixture.Namespace("Gst");
            GirSymbol symbol = GirFixture.Symbol("Gst.MessageType");
            GirEnumeration messageType = Assert.IsType<GirEnumeration>(symbol.Declaration);
            GirEnumMember stateChanged = messageType.Members.Single(
                static member => string.Equals(member.Name, "state_changed", StringComparison.Ordinal));

            Assert.Equal("MessageKind", mapper.TypeName(symbol));
            Assert.Equal("StateSwitched", mapper.EnumMemberName(messageType, ns, stateChanged));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TypeNamesKeepTheGirSpelling()
    {
        NameMapper mapper = new(Overlays.Empty, new DiagnosticBag());

        Assert.Equal("Element", mapper.TypeName(GirFixture.Symbol("Gst.Element")));
        Assert.Equal("MessageType", mapper.TypeName(GirFixture.Symbol("Gst.MessageType")));
    }
}
