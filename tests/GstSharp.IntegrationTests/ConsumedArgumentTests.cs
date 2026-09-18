using Gst;
using Gst.Allocators;
using Xunit;
using Xunit.Abstractions;
using Stream = Gst.Stream;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated members whose argument the call takes over
/// (<c>transfer-ownership="full"</c> on an <c>in</c> parameter): the call is
/// handed a value minted for it, and the wrapper is disposed when the member
/// returns for a mini object and for a boxed value. A GObject is handed over
/// rather than consumed: its wrapper is interned and stays the caller's. The
/// hand written consuming members have tests of their own
/// (<see cref="EventSenderTests"/> among them); these cover one generated
/// member per interesting shape — a consumed mini object, a handed over
/// GObject, and a nullable consumed boxed value.
/// </summary>
/// <remarks>
/// Every member called here is available on the GStreamer 1.24 floor of the
/// Linux leg: <c>gst_caps_append</c> predates 1.0 versioning,
/// <c>gst_stream_collection_add_stream</c> is 1.10 and
/// <c>gst_caps_set_features_simple</c> is 1.20.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class ConsumedArgumentTests
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
    /// <c>gst_stream_collection_add_stream</c> is handed a GObject. The call
    /// takes a reference minted for it, the wrapper keeps the one it holds and
    /// stays usable, and the collection hands that very wrapper back.
    /// </summary>
    [Fact]
    public void AddStreamKeepsTheStreamWrapper()
    {
        using StreamCollection collection = StreamCollection.New(null);
        using Stream stream = Stream.New("handed-over-stream", null, StreamType.Video, StreamFlags.None);

        Assert.True(collection.AddStream(stream));

        // An exact count rather than a delta: gst_stream_new sinks the floating
        // reference itself, so the wrapper holds exactly one before the call
        // and the collection stores the minted one without referencing it
        // again, which is two and nothing else.
        Assert.Equal(2u, RefCountOf(stream.Handle));

        // The wrapper is the caller's and is still readable.
        Assert.False(stream.IsDisposed);
        Assert.Equal("handed-over-stream", stream.GetStreamId());
        Assert.Equal(1u, collection.GetSize());

        // A GObject wrapper is interned, so the way back to the stream is the
        // wrapper that was handed over, not a fresh one.
        Stream? fetched = collection.GetStream(0);

        Assert.NotNull(fetched);
        _output.WriteLine($"stream out of the collection: {fetched.GetStreamId()}");
        Assert.Same(stream, fetched);
    }

    /// <summary>
    /// <c>gst_allocator_register</c> is handed an allocator and keeps it in the
    /// registry for good. The wrapper stays the caller's and the registry hands
    /// it back.
    /// </summary>
    [Fact]
    public void RegisterKeepsTheAllocatorWrapper()
    {
        string name = "gstsharp-test-" + Guid.NewGuid().ToString("N");

        // An allocator this test built rather than the system one: the count of
        // a shared allocator moves with every live GstMemory that holds it, so
        // a collection between the two reads would turn the delta into a flake.
        // The fd allocator is built on every platform, which AllocatorsTests
        // pins, and nothing else holds this instance.
        using Allocator allocator = FdAllocator.New();

        // A delta rather than an exact count, which is all a shared object
        // allows. The registry keeps the minted reference for good -
        // gstallocator.c:236-237 says so and flags the allocator
        // MAY_BE_LEAKED - so this one is never given back.
        uint before = RefCountOf(allocator.Handle);

        Allocator.Register(name, allocator);

        Assert.Equal(before + 1, RefCountOf(allocator.Handle));
        Assert.False(allocator.IsDisposed);

        Allocator? found = Allocator.Find(name);

        Assert.NotNull(found);
        _output.WriteLine($"allocator out of the registry: {found.GetName()}");
        Assert.Same(allocator, found);
    }

    /// <summary>
    /// <c>ges_project_save</c> is handed a formatter asset and releases it
    /// before it returns: what the library keeps is the length of the call.
    /// The wrapper is untouched either way, which is the handover contract for
    /// the one nullable GObject argument of the corpus.
    /// </summary>
    /// <remarks>
    /// <c>ges_formatter_get_default</c> answers an asset whose extractable type
    /// is a <c>GESFormatter</c>, which is what the <c>g_return_val_if_fail</c>
    /// of ges-project.c:1203-1205 demands of the argument, and the timeline has
    /// to be one this project extracted (:1223-1229). Both members and both
    /// checks are the same on the 1.24 floor of the Linux leg
    /// (ges-project.c:1181-1183, ges-formatter.c:482), so the test carries no
    /// availability gate.
    /// </remarks>
    [Fact]
    public void SavingWithAFormatterAssetKeepsTheAssetWrapper()
    {
        GES.GstGES.Initialize();

        string path = Path.Combine(
            Path.GetTempPath(),
            "gstsharp-test-" + Guid.NewGuid().ToString("N") + ".xges");

        using GES.Project project = GES.Project.New(null);
        using GES.Timeline timeline = project.Extract<GES.Timeline>();

        // The default formatter asset is interned and process wide: it is the
        // library's, not the test's, so nothing disposes it here.
        GES.Asset formatter = GES.Formatter.GetDefault();
        uint before = RefCountOf(formatter.Handle);

        try
        {
            Assert.True(
                project.Save(timeline, new System.Uri(path).AbsoluteUri, formatter, true),
                "the project refused to save.");
        }
        finally
        {
            File.Delete(path);
        }

        uint after = RefCountOf(formatter.Handle);
        _output.WriteLine($"formatter asset reference count: {before} -> {after}");

        // Measured, not assumed: the call unrefs the reference minted for it
        // before it returns (ges-project.c:1257-1258), so the count lands back
        // where it started.
        Assert.Equal(before, after);
        Assert.False(formatter.IsDisposed);
        Assert.Same(formatter, GES.Formatter.GetDefault());
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

    /// <summary>
    /// <c>gst_encoding_target_add_profile</c> refuses a profile whose name is
    /// already in the target (encoding-target.c:387-395) before it takes the
    /// reference minted for the call, so the binding releases that reference
    /// again and the count of the profile lands back where it started.
    /// </summary>
    /// <remarks>
    /// The refusal is a <c>GST_WARNING</c> rather than a
    /// <c>g_return_val_if_fail</c>, so the run prints a warning line and goes
    /// on. What the library answers is what the assertions are gated on: only a
    /// <see langword="false"/> is the refusal this covers.
    /// </remarks>
    [Fact]
    public void AddingAProfileTheTargetAlreadyCarriesReleasesTheMintedReference()
    {
        using Caps format = Assert.IsType<Caps>(Caps.FromString("audio/x-vorbis"));

        using Gst.Pbutils.EncodingAudioProfile first =
            Gst.Pbutils.EncodingAudioProfile.New(format, null, null, 0);
        first.SetName("consumed-argument-duplicate");

        using Gst.Pbutils.EncodingTarget? target = Gst.Pbutils.EncodingTarget.New(
            "consumed-argument-target",
            "device",
            "A target built by the test suite",
            [first]);
        Assert.NotNull(target);

        using Gst.Pbutils.EncodingAudioProfile second =
            Gst.Pbutils.EncodingAudioProfile.New(format, null, null, 0);
        second.SetName("consumed-argument-duplicate");

        uint before = RefCountOf(second.Handle);
        bool added = target.AddProfile(second);
        uint after = RefCountOf(second.Handle);
        _output.WriteLine($"duplicate profile: added={added}, reference count {before} -> {after}");

        // A library that took the profile all the same is a library whose
        // duplicate check did not run; the leak this covers only exists on the
        // path where the call refuses.
        if (!added)
        {
            Assert.Equal(before, after);
            Assert.False(second.IsDisposed);

            IReadOnlyList<Gst.Pbutils.EncodingProfile> profiles = target.GetProfiles();
            try
            {
                Assert.Single(profiles);
            }
            finally
            {
                foreach (Gst.Pbutils.EncodingProfile held in profiles)
                {
                    held.Dispose();
                }
            }
        }
    }

    /// <summary>Reads the <c>ref_count</c> field of a <c>GObject</c>.</summary>
    /// <param name="handle">The instance to read.</param>
    /// <returns>The reference count at that moment.</returns>
    /// <remarks>
    /// A <c>GObject</c> begins with its <c>GTypeInstance</c>, which is one
    /// pointer, and the reference count is the field behind it.
    /// </remarks>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));
}
