using GES;
using Gst;
using Gst.GObject;
using Xunit;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The child property members of a timeline element that carry a
/// <c>GParamSpec</c>, against the editing services that are installed.
/// </summary>
/// <remarks>
/// <para>
/// The GES half of the parameter specification shape differs from the core one
/// in exactly one place and deliberately so: <c>ges_timeline_element_lookup_child</c>
/// hands the caller a reference of its own (<c>g_param_spec_ref</c>), while
/// <c>gst_child_proxy_lookup</c> lends the one the class holds. Both girs
/// describe their own function correctly, and the wrapper ends up owning one
/// reference either way, which is what makes the two read alike here.
/// </para>
/// <para>
/// Everything called here has been in GES since 1.0, so these run on the 1.24
/// floor.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesParamSpecTests
{
    /// <summary>Initialises one test.</summary>
    public GesParamSpecTests() => GstGES.Initialize();

    /// <summary>
    /// A child property lookup names the child and the specification, and the
    /// pair round trips through the two <c>by_pspec</c> accessors.
    /// </summary>
    [Fact]
    public void AChildPropertyIsReadAndWrittenThroughItsSpecification()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));
            Assert.True(layer.AddClip(clip));

            Assert.True(clip.LookupChild("freq", out Gst.GObject.Object? child, out ParamSpec? pspec));
            Assert.NotNull(child);
            Assert.NotNull(pspec);

            // The call transferred a reference, which this wrapper adopted, so
            // disposing it is what gives it back.
            using (pspec)
            {
                Assert.Equal("freq", pspec.Name);
                Assert.Equal(GType.Double, pspec.ValueType);

                using (Value written = Value.New(GType.Double))
                {
                    written.SetDouble(660.0);
                    clip.SetChildPropertyByPspec(pspec, written);
                }

                clip.GetChildPropertyByPspec(pspec, out Value read);

                using (read)
                {
                    Assert.Equal(GType.Double, read.Type);
                    Assert.Equal(660.0, read.GetDouble());
                }
            }
        }
    }

    /// <summary>
    /// A lookup that finds nothing answers <see langword="false"/> and leaves
    /// both out parameters null, exactly as the core call does.
    /// </summary>
    [Fact]
    public void AChildPropertyLookupThatMissesAnswersNullForBoth()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));
            Assert.True(layer.AddClip(clip));

            Assert.False(clip.LookupChild("no-such-property", out Gst.GObject.Object? child, out ParamSpec? pspec));
            Assert.Null(child);
            Assert.Null(pspec);
        }
    }

    /// <summary>
    /// A child property is unregistered and registered again through the
    /// specification the lookup handed out, which is the pair the registration
    /// half of the family exists for.
    /// </summary>
    /// <remarks>
    /// The removal releases the reference GES held on the specification. The
    /// wrapper holds one of its own, so the value stays usable across the gap
    /// and can be handed straight back to the registration — which is the
    /// reason a borrowed specification is reference counted by the wrapper at
    /// all.
    /// </remarks>
    [Fact]
    public void AChildPropertyIsUnregisteredAndRegisteredAgain()
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));
            Assert.True(layer.AddClip(clip));

            Assert.True(clip.LookupChild("freq", out Gst.GObject.Object? child, out ParamSpec? pspec));
            Assert.NotNull(child);
            Assert.NotNull(pspec);

            using (pspec)
            {
                Assert.True(clip.RemoveChildProperty(pspec));

                // The registration is keyed on the specification, so removing
                // the same one twice is the second call answering that there is
                // nothing left to remove. A clip aggregates the properties of
                // every track element behind it, and more than one of them may
                // carry a "freq", so this is the exact statement rather than a
                // lookup by name.
                Assert.False(clip.RemoveChildProperty(pspec));

                Assert.True(clip.AddChildProperty(pspec, child));
                Assert.True(clip.RemoveChildProperty(pspec));

                // Put it back, so the clip is left as it was found.
                Assert.True(clip.AddChildProperty(pspec, child));
            }
        }
    }
}
