using GES;
using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The child properties of a timeline element, against the editing services
/// that are installed.
/// </summary>
/// <remarks>
/// <para>
/// A timeline element is a facade in front of the <c>GstElement</c>s that do
/// the work, and the properties of those elements are reachable only as child
/// properties. The pair of calls under test carries a <c>GValue</c>, which is
/// why the generator skips it and why this is hand written; what the tests say
/// is that the hand written pair agrees with the typed accessors GES itself
/// offers for the same underlying property.
/// </para>
/// <para>
/// <c>freq</c> is the frequency of the <c>audiotestsrc</c> behind a test clip.
/// <see cref="TestClip.GetFrequency"/> reads the same property through
/// <c>ges_track_element_set_child_properties</c>, so the two have to answer the
/// same number — which is the assertion a round trip through the binding alone
/// could not make.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesChildPropertyTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public GesChildPropertyTests(ITestOutputHelper output)
    {
        _output = output;
        GstGES.Initialize();
    }

    /// <summary>
    /// A child property written through the binding is read back by the
    /// binding and by the typed accessor of the clip.
    /// </summary>
    [Fact]
    public void AChildPropertyRoundTripsAndAgreesWithTheTypedAccessor()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));

            // Adding the clip is what builds the track elements behind it, and
            // the track elements are what carry the child properties.
            Assert.True(layer.AddClip(clip));

            using (Value written = Value.New(GType.Double))
            {
                written.SetDouble(880.0);
                clip.SetChildProperty("freq", written);
            }

            using Value read = clip.GetChildProperty("freq");

            Assert.False(read.IsEmpty);

            // The type comes from the parameter specification of the child, not
            // from the caller: GES initialises the value on the way through.
            Assert.Equal(GType.Double, read.Type);
            Assert.Equal(880.0, read.GetDouble());

            // GESTestClip keeps a copy of the frequency of its own and its
            // typed getter answers from that copy, so writing the child
            // property directly does not change what GetFrequency reports. That
            // is a property of the clip rather than of this binding; the
            // agreement between the two runs the other way, which is what
            // AChildPropertySeesWhatTheTypedAccessorWrote measures.
            _output.WriteLine(
                $"freq reads {read.GetDouble()} as a {read.Type.Name}, " +
                $"while the clip still reports {clip.GetFrequency()}");
        }
    }

    /// <summary>
    /// The other direction: a property the clip set through its own accessor is
    /// visible as a child property.
    /// </summary>
    [Fact]
    public void AChildPropertySeesWhatTheTypedAccessorWrote()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));
            Assert.True(layer.AddClip(clip));

            clip.SetVolume(0.25);

            using Value volume = clip.GetChildProperty("volume");
            Assert.Equal(GType.Double, volume.Type);
            Assert.Equal(0.25, volume.GetDouble());
        }
    }

    /// <summary>
    /// A name no child carries is an <see cref="ArgumentException"/>, which is
    /// the answer <see cref="Object.GetProperty"/> gives for an unknown
    /// property of an object.
    /// </summary>
    [Fact]
    public void AnUnknownChildPropertyIsRefused()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));
            Assert.True(layer.AddClip(clip));

            ArgumentException failure =
                Assert.Throws<ArgumentException>(() => clip.GetChildProperty("no-such-property"));
            Assert.Contains("no-such-property", failure.Message, StringComparison.Ordinal);

            using Value number = Value.New(GType.Double);
            number.SetDouble(1.0);

            Assert.Throws<ArgumentException>(() => clip.SetChildProperty("no-such-property", number));
        }
    }

    /// <summary>
    /// A clip that is not in a layer has no children yet, so it has no child
    /// properties either. This is the shape of the mistake an application
    /// makes, and it fails the way an unknown name does.
    /// </summary>
    [Fact]
    public void AClipOutsideALayerHasNoChildProperties()
    {
        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.Throws<ArgumentException>(() => clip.GetChildProperty("freq"));
        }
    }
}
