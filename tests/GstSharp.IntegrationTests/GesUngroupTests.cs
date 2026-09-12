using System.Runtime.InteropServices;
using GES;
using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>GES.Container.Ungroup</c>: the hand written split, whose answer is not
/// uniformly owned and whose instance the C never consumes.
/// </summary>
/// <remarks>
/// Everything here runs on the thread of the test, which is what the editing
/// services assert for a timeline and its tracks.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class GesUngroupTests
{
    private static readonly ClockTime Length = ClockTime.FromSeconds(2);

    /// <summary>
    /// A clip with an audio and a video child splits into two clips, the
    /// original one and a new one in the same layer, and the layer holds both
    /// once the wrappers are gone.
    /// </summary>
    [Fact]
    public void AClipWithTwoTrackTypesSplitsIntoTwoClipsOfTheLayer()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip clip = TestClip.New() ?? throw new InvalidOperationException("The test clip was refused.");

        using (clip)
        {
            Assert.True(clip.SetDuration(Length));
            Assert.True(layer.AddClip(clip));

            System.Collections.Generic.IReadOnlyList<Container> parts = clip.Ungroup(recursive: false);
            Assert.Equal(2, parts.Count);

            // The instance is not consumed: the very wrapper that was split is
            // one of the answers, and it is still usable.
            Assert.Contains(parts, part => ReferenceEquals(part, clip));
            Assert.NotNull(clip.Name);

            Container other = Assert.Single(parts, part => !ReferenceEquals(part, clip));
            Clip split = Assert.IsAssignableFrom<Clip>(other);
            // The layer of the copy is the layer of the original, and the
            // wrapper of it is the interned one this test already holds, so it
            // is not disposed here.
            Layer? splitLayer = split.GetLayer();
            Assert.Same(layer, splitLayer);

            foreach (Container part in parts)
            {
                if (!ReferenceEquals(part, clip))
                {
                    part.Dispose();
                }
            }

            // Disposing the wrappers of the answer released the references the
            // split added and nothing more: the layer still holds both clips.
            System.Collections.Generic.IReadOnlyList<Clip> clips = layer.GetClips();
            Assert.Equal(2, clips.Count);
            foreach (Clip listed in clips)
            {
                if (!ReferenceEquals(listed, clip))
                {
                    listed.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// A clip with no children answers itself alone, with no reference added,
    /// and the wrapper stays usable.
    /// </summary>
    [Fact]
    public void AChildlessClipAnswersItselfAndStaysUsable()
    {
        GstGES.Initialize();

        TestClip clip = TestClip.New() ?? throw new InvalidOperationException("The test clip was refused.");

        using (clip)
        {
            Assert.Empty(clip.GetChildren(recursive: false));

            uint before = RefCountOf(clip.Handle);
            Container only = Assert.Single(clip.Ungroup(recursive: false));

            Assert.Same(clip, only);
            Assert.Equal(before, RefCountOf(clip.Handle));
            Assert.NotNull(clip.Name);
        }
    }

    /// <summary>
    /// A group answers its children and is left with none of them.
    /// </summary>
    [Fact]
    public void AGroupAnswersItsChildrenAndIsLeftEmpty()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip first = TestClip.New() ?? throw new InvalidOperationException("The test clip was refused.");
        TestClip second = TestClip.New() ?? throw new InvalidOperationException("The test clip was refused.");

        using (first)
        using (second)
        {
            Assert.True(first.SetStart(ClockTime.Zero));
            Assert.True(first.SetDuration(Length));
            Assert.True(second.SetStart(ClockTime.FromSeconds(4)));
            Assert.True(second.SetDuration(Length));
            Assert.True(layer.AddClip(first));
            Assert.True(layer.AddClip(second));

            Container grouped = Container.Group([first, second])
                ?? throw new InvalidOperationException("The clips could not be grouped.");

            Group group = Assert.IsType<Group>(grouped);

            using (group)
            {
                System.Collections.Generic.IReadOnlyList<Container> parts = group.Ungroup(recursive: false);

                Assert.Equal(2, parts.Count);
                Assert.Contains(parts, part => ReferenceEquals(part, first));
                Assert.Contains(parts, part => ReferenceEquals(part, second));
                Assert.Empty(group.GetChildren(recursive: false));
            }
        }
    }

    /// <summary>
    /// The copies a clip outside a layer answers are floating with the added
    /// reference on top, and the wrapper settles both: every answer is owned
    /// exactly once and nothing crashes when it is released.
    /// </summary>
    /// <remarks>
    /// A clip only grows children inside a layer, and it keeps them when it
    /// leaves one, which is the shape that reaches
    /// <c>ges_timeline_element_copy</c> with no layer to sink the copy into
    /// (ges-clip.c:2170-2177).
    /// </remarks>
    [Fact]
    public void TheCopiesOfAClipOutsideALayerAreOwnedExactlyOnce()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip clip = TestClip.New() ?? throw new InvalidOperationException("The test clip was refused.");

        using (clip)
        {
            Assert.True(clip.SetDuration(Length));
            Assert.True(layer.AddClip(clip));
            Assert.True(layer.RemoveClip(clip));
            Assert.NotEmpty(clip.GetChildren(recursive: false));

            System.Collections.Generic.IReadOnlyList<Container> parts = clip.Ungroup(recursive: false);
            Assert.Equal(2, parts.Count);

            foreach (Container part in parts)
            {
                Assert.Equal(0, ObjectIsFloating(part.Handle));

                if (!ReferenceEquals(part, clip))
                {
                    // The wrapper is the only owner of the copy: the floating
                    // reference was settled and the one the split added was
                    // released with it.
                    Assert.Equal(1u, RefCountOf(part.Handle));
                    part.Dispose();
                }
            }

            Assert.NotNull(clip.Name);
        }
    }

    /// <summary>Reads the reference count of a <c>GObject</c>.</summary>
    /// <param name="handle">The instance to inspect.</param>
    /// <returns>The current count.</returns>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    /// <summary>Reads the floating flag of a <c>GObject</c>.</summary>
    /// <param name="instance">The instance to inspect.</param>
    /// <returns>Non zero while the instance carries a floating reference.</returns>
    [LibraryImport("GObject", EntryPoint = "g_object_is_floating")]
    private static partial int ObjectIsFloating(nint instance);
}
