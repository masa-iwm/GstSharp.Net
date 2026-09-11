using GES;
using Gst;
using Gst.Controller;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="TrackElement.GetAllControlBindings"/>, the one member of the
/// corpus that answers a <c>GHashTable</c> of GObjects, against the library
/// that is installed.
/// </summary>
/// <remarks>
/// The member has no <c>Since:</c> at all — the table is created in the
/// instance initializer of every <c>GESTrackElement</c> — so nothing here is
/// gated on <c>NativeAvailability</c>. What the test is about is the two halves
/// the gir cannot state: the table is borrowed and its values are unowned, so
/// the binding copies the entries out and interns each value, which makes the
/// wrapper in the dictionary the very one the single key member answers.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesControlBindingTableTests
{
    /// <summary>Initialises one test.</summary>
    public GesControlBindingTableTests()
    {
        GstGES.Initialize();
        GstController.Initialize();
    }

    /// <summary>
    /// A track element that was never bound answers an empty dictionary rather
    /// than nothing: C creates the table in its initializer and never clears
    /// the pointer.
    /// </summary>
    [Fact]
    public void AnElementWithNoBindingAnswersNoEntries()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(2)));
            Assert.True(layer.AddClip(clip));

            TrackElement track = AudioSourceOf(clip);

            System.Collections.Generic.Dictionary<string, Gst.ControlBinding> bindings =
                track.GetAllControlBindings();

            Assert.Empty(bindings);
        }
    }

    /// <summary>
    /// One binding, keyed by the property name that was passed to
    /// <see cref="TrackElement.SetControlSource"/> and carrying the very
    /// wrapper <see cref="TrackElement.GetControlBinding"/> answers, and an
    /// empty dictionary again once the binding is taken off.
    /// </summary>
    [Fact]
    public void AControlBindingAppearsUnderTheNameItWasBoundWith()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(2)));
            Assert.True(layer.AddClip(clip));

            TrackElement track = AudioSourceOf(clip);

            InterpolationControlSource source = InterpolationControlSource.New();
            source.Mode = InterpolationMode.Linear;
            Assert.True(source.Set(ClockTime.Zero, 0.2));
            Assert.True(source.Set(ClockTime.FromSeconds(1), 0.8));

            Assert.True(track.SetControlSource(source, "volume", "direct-absolute"));

            System.Collections.Generic.Dictionary<string, Gst.ControlBinding> bindings =
                track.GetAllControlBindings();

            // The key is the string that was passed, which GES stores without
            // normalising it.
            Gst.ControlBinding binding = Assert.Contains("volume", bindings);
            Assert.Single(bindings);

            // The interning half: the value is the wrapper the single key
            // member answers, not a second one over the same pointer, which is
            // also why nothing here disposes it.
            Assert.Same(track.GetControlBinding("volume"), binding);

            Assert.True(track.RemoveControlBinding("volume"));
            Assert.Empty(track.GetAllControlBindings());
        }
    }

    /// <summary>
    /// Finds the audio source that a test clip built for itself.
    /// </summary>
    /// <param name="clip">The clip, which has to be in a layer already.</param>
    /// <returns>The track element that feeds the audio track.</returns>
    private static TrackElement AudioSourceOf(Clip clip)
    {
        foreach (TimelineElement child in clip.GetChildren(recursive: false))
        {
            if (child is TrackElement element && element.GetTrackType().HasFlag(TrackType.Audio))
            {
                return element;
            }
        }

        throw new InvalidOperationException("A test clip in a layer of an audio video timeline has an audio source.");
    }
}
