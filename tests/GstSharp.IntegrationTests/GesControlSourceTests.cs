using GES;
using Gst;
using Gst.Controller;
using Xunit;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A control source of the <c>GstSharp.Net.Controller</c> module driving a
/// property of a GES track element, which is the crossing that the generated
/// hierarchy used to be closed to.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TrackElement.SetControlSource"/> takes a
/// <see cref="Gst.ControlSource"/>, and until the generated
/// <c>(nint, Transfer)</c> constructors became <c>protected</c> no wrapper
/// outside the generated assemblies could be one: the module attached at
/// <c>Gst.GObject.InitiallyUnowned</c> and its classes were flat with respect to
/// the generated hierarchy. This is the test that the shape works end to end —
/// a module with no <c>InternalsVisibleTo</c>, a generated base, and a
/// generated member of a third assembly that accepts the result.
/// </para>
/// <para>
/// The property under test is <c>volume</c> of the <c>audiotestsrc</c> behind a
/// test clip: a double, writable, controllable, not construct-only, and running
/// from 0 to 1 so that an absolute binding reads the control points back
/// unchanged.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesControlSourceTests
{
    /// <summary>Initialises one test.</summary>
    public GesControlSourceTests()
    {
        GstGES.Initialize();
        GstController.Initialize();
    }

    /// <summary>
    /// The wrapper of the module is a <see cref="Gst.ControlSource"/> and a
    /// <see cref="Gst.Object"/>, because the native type derives from both.
    /// </summary>
    [Fact]
    public void TheWrapperOfTheModuleIsAGeneratedControlSource()
    {
        InterpolationControlSource source = InterpolationControlSource.New();

        Assert.IsAssignableFrom<Gst.ControlSource>(source);
        Assert.IsAssignableFrom<Gst.Object>(source);
        Assert.IsAssignableFrom<Gst.GObject.InitiallyUnowned>(source);

        // The members of the generated ancestors are inherited rather than
        // bound a second time. TryGetValue is the module's own name for the
        // one below it.
        Assert.True(source.Set(ClockTime.Zero, 0.5));
        Assert.True(source.ControlSourceGetValue(ClockTime.Zero, out double value));
        Assert.Equal(0.5, value, 6);
        Assert.True(source.TryGetValue(ClockTime.Zero, out double sameValue));
        Assert.Equal(value, sameValue, 6);

        // GstObject.name, from two generated classes further up.
        Assert.True(source.SetName("named"));
        Assert.Equal("named", source.Name);
    }

    /// <summary>
    /// The acceptance: a source built by the module is handed to a generated
    /// GES member that takes a <see cref="Gst.ControlSource"/>, and the
    /// property it was bound to follows it.
    /// </summary>
    [Fact]
    public void AControlSourceOfTheModuleDrivesAGesTrackElement()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(2)));

            // Adding the clip is what builds the track elements behind it.
            Assert.True(layer.AddClip(clip));

            TrackElement track = AudioSourceOf(clip);

            InterpolationControlSource source = InterpolationControlSource.New();
            source.Mode = InterpolationMode.Linear;
            Assert.True(source.Set(ClockTime.Zero, 0.2));
            Assert.True(source.Set(ClockTime.FromSeconds(1), 0.8));

            // The compile time half of the assertion: the parameter is the
            // generated Gst.ControlSource and the module's wrapper is one, with
            // no conversion and no InternalsVisibleTo anywhere.
            Gst.ControlSource controlSource = source;
            Assert.True(track.SetControlSource(controlSource, "volume", "direct-absolute"));

            Assert.NotNull(track.GetControlBinding("volume"));

            // GES puts the binding on the element that really carries the
            // property, which for a test source is one element inside the bin
            // the track element is controlling.
            Gst.Element controlled = ControlledElement(track);

            Assert.Equal(0.2, VolumeAt(controlled, ClockTime.Zero), 6);
            Assert.Equal(0.5, VolumeAt(controlled, ClockTime.FromMilliseconds(500)), 6);
            Assert.Equal(0.8, VolumeAt(controlled, ClockTime.FromSeconds(1)), 6);
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

        throw new InvalidOperationException("The test clip built no audio source.");
    }

    /// <summary>
    /// Finds the element the control binding was actually installed on.
    /// </summary>
    /// <param name="track">The track element that was given the control source.</param>
    /// <returns>The element whose <c>volume</c> the binding drives.</returns>
    /// <remarks>
    /// A child property of a track element belongs to one of the elements
    /// behind it, and GES builds the binding on that element rather than on the
    /// facade. <c>ges_track_element_get_element</c> answers with the bin around
    /// them, so the one that carries the binding is found by walking it.
    /// </remarks>
    private static Gst.Element ControlledElement(TrackElement track)
    {
        Gst.Element element = Assert.IsAssignableFrom<Gst.Element>(track.GetElement());

        if (element.GetControlBinding("volume") is not null)
        {
            return element;
        }

        if (element is Gst.Bin bin)
        {
            using Iterator children = bin.IterateRecurse();
            foreach (Gst.Element child in children.Items<Gst.Element>())
            {
                if (child.GetControlBinding("volume") is not null)
                {
                    return child;
                }
            }
        }

        throw new InvalidOperationException("No element behind the track element carries the binding.");
    }

    /// <summary>
    /// Synchronises the controlled properties of an element to one timestamp
    /// and reads the one under test back.
    /// </summary>
    /// <param name="element">The element whose <c>volume</c> is bound.</param>
    /// <param name="time">The stream time to evaluate the binding at.</param>
    /// <returns>The value the binding wrote.</returns>
    private static double VolumeAt(Gst.Element element, ClockTime time)
    {
        Assert.True(element.SyncValues(time));

        using Value value = element.GetProperty("volume");
        return value.GetDouble();
    }
}
