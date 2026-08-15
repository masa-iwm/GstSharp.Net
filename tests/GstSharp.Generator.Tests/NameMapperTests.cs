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
    [InlineData("169TopAligned", "_169TopAligned")]
    [InlineData("", "_")]
    public void KeywordsAndLeadingDigitsAreEscaped(string candidate, string expected) =>
        Assert.Equal(expected, NameMapper.EscapeIdentifier(candidate));

    [Fact]
    public void EnumMemberNamesArePascalCased()
    {
        NameMapper mapper = new(Overlays.Empty);
        GirNamespace ns = GirFixture.Namespace("Gst");
        GirEnumeration messageType = Assert.IsType<GirEnumeration>(GirFixture.Symbol("Gst.MessageType").Declaration);

        GirEnumMember stateChanged = messageType.Members.Single(
            static member => string.Equals(member.Name, "state_changed", StringComparison.Ordinal));
        Assert.Equal("StateChanged", mapper.EnumMemberName(messageType, ns, stateChanged));
    }

    [Fact]
    public void EnumMemberNamesWithLeadingDigitsGetAnUnderscore()
    {
        NameMapper mapper = new(Overlays.Empty);
        GirNamespace ns = GirFixture.Namespace("GstVideo");
        GirEnumeration range = Assert.IsType<GirEnumeration>(GirFixture.Symbol("GstVideo.VideoColorRange").Declaration);

        GirEnumMember member = range.Members.Single(
            static m => string.Equals(m.Name, "16_235", StringComparison.Ordinal));
        Assert.Equal("_16235", mapper.EnumMemberName(range, ns, member));
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

            NameMapper mapper = new(Overlays.Load(directory));
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
        NameMapper mapper = new(Overlays.Empty);

        Assert.Equal("Element", mapper.TypeName(GirFixture.Symbol("Gst.Element")));
        Assert.Equal("MessageType", mapper.TypeName(GirFixture.Symbol("Gst.MessageType")));
    }
}
