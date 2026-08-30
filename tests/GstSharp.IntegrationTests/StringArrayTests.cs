using Gst;
using Gst.GLib;
using Gst.GObject;
using Gst.Sdp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The string array projection against the library that is installed: what a
/// <c>NULL</c> terminated vector carries into the library, what one carries
/// back out, and the two <c>NULL</c> answers the gir denies.
/// </summary>
/// <remarks>
/// The <c>in</c> direction is the half that has a native allocation behind it —
/// a vector the binding builds for the length of one call — so every test here
/// that passes an array is measuring that the callee saw what the caller wrote,
/// which is the only observation that tells a correct vector from a truncated
/// one.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class StringArrayTests
{
    /// <summary>
    /// Registering an api with two tags and reading them back is the encode and
    /// the decode in one call pair: the tags the register call was handed are
    /// the ones the library answers with, in order.
    /// </summary>
    [Fact]
    public void TagsRoundTripThroughARegisteredMetaApi()
    {
        GType api = Meta.ApiTypeRegister("GstSharpProbeApi", ["memory", "size"]);

        Assert.NotEqual(GType.Invalid, api);

        string[]? tags = Meta.ApiTypeGetTags(api);

        Assert.NotNull(tags);
        Assert.Equal(["memory", "size"], tags);
    }

    /// <summary>
    /// The 1.28 half of the same surface, which reads the vector it is handed
    /// without keeping it.
    /// </summary>
    [RequiresGStreamerFact(28)]
    public void TagsAreCheckedAgainstAVectorTheCallReads()
    {
        GType api = Meta.ApiTypeRegister("GstSharpProbeContainsApi", ["memory"]);

        Assert.True(Meta.ApiTypeTagsContainOnly(api, ["memory", "size"]));
        Assert.False(Meta.ApiTypeTagsContainOnly(api, ["size"]));
    }

    /// <summary>
    /// The pipeline parser reads the vector element by element, so a pipeline
    /// that builds proves every element arrived; the failing description proves
    /// the vector is released on the throwing path as well as on the returning
    /// one.
    /// </summary>
    [Fact]
    public void APipelineIsParsedFromAnArgumentVector()
    {
        using Element pipeline = Global.ParseLaunchv(["fakesrc", "!", "fakesink"]);

        Assert.NotNull(pipeline);

        Assert.Throws<GException>(static () => Global.ParseLaunchv(["nosuchelement-gstsharp"]));
    }

    /// <summary>
    /// A borrowed return: the options of a fresh pool are an empty vector the
    /// pool owns, which decodes to an empty array rather than to
    /// <see langword="null"/>.
    /// </summary>
    [Fact]
    public void AFreshBufferPoolAnswersAnEmptyOptionArray()
    {
        using BufferPool pool = BufferPool.New();

        string[]? options = pool.GetOptions();

        Assert.NotNull(options);
        Assert.Empty(options);
    }

    /// <summary>
    /// The nullable correction of the overlay, both ways: the repeat list is
    /// optional and a vector is accepted where the gir promised there would
    /// always be one.
    /// </summary>
    [Fact]
    public void ATimeIsAddedWithAndWithoutARepeatList()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.New(out SDPMessage? message));

        using (message)
        {
            Assert.NotNull(message);
            Assert.Equal(SDPResult.Ok, message.AddTime("0", "0", null));
            Assert.Equal(SDPResult.Ok, message.AddTime("0", "0", ["1d", "1h"]));
            Assert.Equal(2u, message.TimesLen());
        }
    }

    /// <summary>
    /// An empty array is a vector of one <c>NULL</c> slot and not a <c>NULL</c>
    /// vector: the call walks it, finds nothing and answers that, which is the
    /// contract the encode is written to.
    /// </summary>
    [Fact]
    public void AnEmptyArrayReachesTheLibraryAsAnEmptyVector()
    {
        Assert.Null(Global.ProtectionSelectSystem([]));
    }

    /// <summary>
    /// The other nullable correction: <c>gst_element_factory_get_uri_protocols</c>
    /// hands back the protocol vector of the factory, which is <c>NULL</c> for
    /// every factory that is not a URI handler, whatever the gir says.
    /// </summary>
    [Fact]
    public void TheUriProtocolsOfANonHandlerAreNull()
    {
        using ElementFactory? identity = ElementFactory.Find("identity");

        Assert.NotNull(identity);
        Assert.Null(identity.GetUriProtocols());

        using ElementFactory? fileSrc = ElementFactory.Find("filesrc");

        Assert.NotNull(fileSrc);

        string[]? protocols = fileSrc.GetUriProtocols();

        Assert.NotNull(protocols);
        Assert.Contains("file", protocols);
    }
}
