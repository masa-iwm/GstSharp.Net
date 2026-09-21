using System.Runtime.InteropServices;
using GES;
using Gst;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand-written <c>ungroup</c> slot a managed clip takes over: what the
/// list it answers carries when a C caller takes it over, and what the forward
/// member sees when the slot below it is the managed one rather than
/// <c>GESClip::ungroup</c>.
/// </summary>
/// <remarks>
/// Everything here runs on the thread of the test, which is what the editing
/// services assert for a timeline and its tracks.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class ManagedClipUngroupTests
{
    private static readonly ClockTime Length = ClockTime.FromSeconds(2);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public ManagedClipUngroupTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A managed clip whose override chains up splits the way a native clip of
    /// the same shape does, and the reference it began with is the one it is
    /// left with once the answer is released.
    /// </summary>
    [Fact]
    public void AChainingOverrideSplitsLikeTheClipBelowIt()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        // What "the way a native clip of the same shape does" means is read off
        // one: a native clip of its own layer, so that the two splits do not
        // meet, is what the counts below are compared against.
        using Layer nativeLayer = timeline.AppendLayer();
        using TestClip native = TestClip.New() ?? throw new InvalidOperationException("The test clip was refused.");
        Assert.True(native.SetDuration(Length));
        Assert.True(nativeLayer.AddClip(native));

        int nativeChildren = native.GetChildren(recursive: false).Count;
        IReadOnlyList<Container> nativeParts = native.Ungroup(recursive: false);

        // The comparisons below are equalities against what the native clip
        // did, so they need a floor of their own: a native clip that grew no
        // children, or one that was handed back whole, would make them true
        // without the managed clip having split at all. A test clip of the
        // editing services carries an audio child and a video child, and
        // splits into the two clips those go to.
        Assert.True(nativeChildren >= 2, $"The native clip has {nativeChildren} children.");
        Assert.True(nativeParts.Count >= 2, $"The native clip split into {nativeParts.Count} parts.");

        foreach (Container part in nativeParts)
        {
            if (!ReferenceEquals(part, native))
            {
                part.Dispose();
            }
        }

        ProbeUngroupSourceClip clip = ProbeUngroupSourceClip.New();

        using (clip)
        {
            // A ChainUpUngroup() that threw would be reported to the trap and
            // answer the clip alone, so the trap is watched while the split
            // runs rather than left to report at teardown, where nothing reads
            // it.
            List<Exception> failures = Watch(() =>
            {
                Assert.True(clip.SetDuration(Length));
                Assert.True(layer.AddClip(clip));
                Assert.Equal(nativeChildren, clip.GetChildren(recursive: false).Count);

                uint before = RefCountOf(clip.Handle);
                IReadOnlyList<Container> parts = clip.Ungroup(recursive: false);

                Assert.Equal(1, clip.Ungrouped);
                Assert.Equal(nativeParts.Count, parts.Count);
                Assert.Contains(parts, part => ReferenceEquals(part, clip));

                foreach (Container part in parts)
                {
                    if (!ReferenceEquals(part, clip))
                    {
                        part.Dispose();
                    }
                }

                Assert.Equal(before, RefCountOf(clip.Handle));
                Assert.NotNull(clip.Name);
            });

            Assert.Empty(failures);
        }
    }

    /// <summary>
    /// A childless managed clip that declares the slot: the chain-up adopts the
    /// clip the way <c>GESClip::ungroup</c> hands it back, without a reference,
    /// and the trampoline adds the one the forward member takes over again. The
    /// count is where it began, three times in a row.
    /// </summary>
    [Fact]
    public void AChildlessManagedClipWithTheOverrideKeepsItsCount()
    {
        GstGES.Initialize();

        ProbeUngroupSourceClip clip = ProbeUngroupSourceClip.New();

        using (clip)
        {
            Assert.Empty(clip.GetChildren(recursive: false));

            uint before = RefCountOf(clip.Handle);
            for (int round = 0; round < 3; round++)
            {
                Container only = Assert.Single(clip.Ungroup(recursive: false));

                Assert.Same(clip, only);
                Assert.Equal(before, RefCountOf(clip.Handle));
                Assert.NotNull(clip.Name);
            }

            Assert.Equal(3, clip.Ungrouped);
        }
    }

    /// <summary>
    /// The twin of the case above: a managed clip that leaves the slot alone
    /// inherits the pointer of <c>GESClip</c> and with it the childless quirk,
    /// which is why the forward member reads the runtime slot rather than the
    /// managed type of the wrapper.
    /// </summary>
    [Fact]
    public void AChildlessManagedClipWithoutTheOverrideKeepsItsCount()
    {
        GstGES.Initialize();

        ProbeSourceClip clip = ProbeSourceClip.New();

        using (clip)
        {
            Assert.Empty(clip.GetChildren(recursive: false));

            uint before = RefCountOf(clip.Handle);
            for (int round = 0; round < 3; round++)
            {
                Container only = Assert.Single(clip.Ungroup(recursive: false));

                Assert.Same(clip, only);
                Assert.Equal(before, RefCountOf(clip.Handle));
                Assert.NotNull(clip.Name);
            }
        }
    }

    /// <summary>
    /// The list a C caller receives from the managed slot: one added reference
    /// per container, the clip itself included, and releasing what the caller
    /// owns puts every count back.
    /// </summary>
    [Fact]
    public void UngroupHandsAConsumingCallerOneReferencePerContainer()
    {
        GstGES.Initialize();

        ProbeUngroupSourceClip clip = ProbeUngroupSourceClip.New();
        ProbeUngroupSourceClip other = ProbeUngroupSourceClip.New();

        using (clip)
        using (other)
        {
            clip.Answer = [clip, other];

            uint clipBefore = RefCountOf(clip.Handle);
            uint otherBefore = RefCountOf(other.Handle);

            nint head = ContainerUngroup(clip.Handle, 0);
            try
            {
                nint[] items = GListMarshal.Collect(head);
                Assert.Equal(new[] { clip.Handle, other.Handle }, items);
                Assert.Equal(clipBefore + 1, RefCountOf(clip.Handle));
                Assert.Equal(otherBefore + 1, RefCountOf(other.Handle));

                foreach (nint item in items)
                {
                    ObjectUnref(item);
                }
            }
            finally
            {
                ListFree(head);
            }

            Assert.Equal(clipBefore, RefCountOf(clip.Handle));
            Assert.Equal(otherBefore, RefCountOf(other.Handle));

            clip.Answer = null;
            clip.AnswersANullEntry = true;

            List<Exception> failures = Watch(() => Assert.Empty(clip.Ungroup(recursive: false)));

            Exception reported = Assert.Single(failures);
            Assert.IsType<InvalidOperationException>(reported);
            _output.WriteLine($"refused: {reported.Message}");
            Assert.Contains("OnUngroup", reported.Message, StringComparison.Ordinal);
            Assert.Equal(clipBefore, RefCountOf(clip.Handle));
        }
    }

    /// <summary>
    /// A managed clip outside any layer with children of two track types: the
    /// copies the chain-up answers are floating with the added reference on top,
    /// and the adoption settles both, so every copy is owned exactly once.
    /// </summary>
    [Fact]
    public void TheCopiesAManagedClipOutsideALayerAnswersAreOwnedExactlyOnce()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        ProbeUngroupSourceClip clip = ProbeUngroupSourceClip.New();

        using (clip)
        {
            Assert.True(clip.SetDuration(Length));
            Assert.True(layer.AddClip(clip));
            Assert.True(layer.RemoveClip(clip));
            Assert.NotEmpty(clip.GetChildren(recursive: false));

            uint before = RefCountOf(clip.Handle);
            IReadOnlyList<Container> parts = clip.Ungroup(recursive: false);
            Assert.Equal(2, parts.Count);

            foreach (Container part in parts)
            {
                Assert.Equal(0, GObjectNative.ObjectIsFloating(part.Handle));

                if (!ReferenceEquals(part, clip))
                {
                    Assert.Equal(1u, RefCountOf(part.Handle));
                    part.Dispose();
                }
            }

            // The clip itself is in the answer too, and the reference the
            // trampoline added for it is the one the forward member took over,
            // so it stands where it stood.
            Assert.Equal(before, RefCountOf(clip.Handle));
            Assert.NotNull(clip.Name);
        }
    }

    /// <summary>Reads the reference count of a <c>GObject</c>.</summary>
    /// <param name="handle">The instance to inspect.</param>
    /// <returns>The current count.</returns>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    /// <summary>Runs an action with the exception trap listening.</summary>
    /// <param name="action">What to run.</param>
    /// <returns>What the trap reported while it ran.</returns>
    private static List<Exception> Watch(Action action)
    {
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            action();
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        return failures;
    }

    /// <summary>
    /// Calls the split the way a C caller does, which is what the forward
    /// member cannot be measured against: it adopts the references the list
    /// carries, and the state of the answer before that is what this pins.
    /// </summary>
    /// <param name="container">The container to split.</param>
    /// <param name="recursive">Whether to split the children of the children.</param>
    /// <returns>The list, which the caller owns together with one reference per node.</returns>
    [LibraryImport("GES", EntryPoint = "ges_container_ungroup")]
    private static partial nint ContainerUngroup(nint container, int recursive);

    /// <summary>Releases one reference of a <c>GObject</c>.</summary>
    /// <param name="instance">The instance to release.</param>
    [LibraryImport("GObject", EntryPoint = "g_object_unref")]
    private static partial void ObjectUnref(nint instance);

    /// <summary>Releases the nodes of a <c>GList</c>, and nothing else.</summary>
    /// <param name="list">The first node.</param>
    [LibraryImport("GLib", EntryPoint = "g_list_free")]
    private static partial void ListFree(nint list);
}
