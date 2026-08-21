using Gst;
using Xunit;
using Xunit.Abstractions;
using Stream = Gst.Stream;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated members whose argument the call takes over
/// (<c>transfer-ownership="full"</c> on an <c>in</c> parameter): the call is
/// handed a value minted for it and the wrapper is disposed when the member
/// returns. The hand written consuming members have tests of their own
/// (<see cref="EventSenderTests"/> among them); these cover one generated
/// member per interesting shape — a consumed mini object, a consumed GObject,
/// and a nullable consumed boxed value.
/// </summary>
/// <remarks>
/// Every member called here is available on the GStreamer 1.24 floor of the
/// Linux leg: <c>gst_caps_append</c> predates 1.0 versioning,
/// <c>gst_stream_collection_add_stream</c> is 1.10 and
/// <c>gst_caps_set_features_simple</c> is 1.20.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ConsumedArgumentTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public ConsumedArgumentTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// <c>gst_caps_append</c> moves the structures of its argument into the
    /// instance. The argument wrapper is spent when the call returns, and the
    /// instance carries both structures afterwards.
    /// </summary>
    [Fact]
    public void AppendConsumesTheCapsAndTheInstanceGainsItsStructures()
    {
        using Caps caps1 = Assert.IsType<Caps>(Caps.FromString("video/x-raw"));
        Caps caps2 = Assert.IsType<Caps>(Caps.FromString("audio/x-raw"));

        caps1.Append(caps2);

        // The call consumed the argument: the wrapper owns nothing now, which
        // is what its disposed state means, and disposing it again is a no-op.
        Assert.True(caps2.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => caps2.GetSize());
        caps2.Dispose();

        Assert.Equal(2u, caps1.GetSize());
        using Structure first = caps1.GetStructure(0);
        using Structure second = caps1.GetStructure(1);
        _output.WriteLine($"caps after append: {first.GetName()} + {second.GetName()}");

        Assert.Equal("video/x-raw", first.GetName());
        Assert.Equal("audio/x-raw", second.GetName());
    }

    /// <summary>
    /// <c>gst_stream_collection_add_stream</c> takes a GObject over. The
    /// interned wrapper is given up process-wide, and the way back to the
    /// stream is a fresh lookup on the collection.
    /// </summary>
    [Fact]
    public void AddStreamConsumesTheGObjectWrapperProcessWide()
    {
        using StreamCollection collection = StreamCollection.New(null);
        Stream stream = Stream.New("consumed-stream", null, StreamType.Video, StreamFlags.None);

        Assert.True(collection.AddStream(stream));

        // The collection holds the reference now and the wrapper is gone for
        // the whole process; GetStream builds a fresh one for the same object.
        Assert.True(stream.IsDisposed);
        Assert.Equal(1u, collection.GetSize());

        using Stream? fetched = collection.GetStream(0);
        Assert.NotNull(fetched);
        _output.WriteLine($"stream out of the collection: {fetched.GetStreamId()}");
        Assert.Equal("consumed-stream", fetched.GetStreamId());
    }

    /// <summary>
    /// A nullable consumed argument passed as <see langword="null"/> is the
    /// absence of a payload: nothing is minted, nothing is disposed, and the
    /// call sees <c>NULL</c>. Passed a value, the call consumes it as usual.
    /// </summary>
    [Fact]
    public void ANullConsumedArgumentLeavesNothingToConsume()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString("video/x-raw"));

        // NULL clears the features of every structure; there is nothing to
        // consume and nothing observable is disposed.
        caps.SetFeaturesSimple(null);
        Assert.Equal(1u, caps.GetSize());

        CapsFeatures any = CapsFeatures.NewAny();
        caps.SetFeaturesSimple(any);

        Assert.True(any.IsDisposed);
        using CapsFeatures? read = caps.GetFeatures(0);
        Assert.NotNull(read);
        _output.WriteLine($"features after set: any={read.IsAny()}");
        Assert.True(read.IsAny());
    }
}
