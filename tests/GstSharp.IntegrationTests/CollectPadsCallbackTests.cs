using System.Runtime.InteropServices;
using Gst;
using Gst.Base;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The regression test of the <c>GstCollectPads</c> use after free.
/// </summary>
/// <remarks>
/// <para>
/// The gir annotates the function of every <c>gst_collect_pads_set_*_function</c>
/// setter <c>(scope call)</c>, which says the library is done with it when the
/// setter returns. It is not: the pointer is stored in the private data of the
/// object and invoked from the streaming thread for the life of the object. The
/// members shipped in 1.28.2 with a <c>finally</c> that released the
/// <c>GCHandle</c> as the setter returned, so the first collected buffer read a
/// handle that had been freed.
/// </para>
/// <para>
/// The test therefore installs the function, drops every managed reference to
/// it, forces a collection, and only then pushes the buffer that makes the
/// collection happen. Against the shipped members this reads freed state;
/// against the corrected <c>forever</c> scope it runs the delegate.
/// </para>
/// <para>
/// <c>gst_collect_pads_add_pad</c> is permanently skipped — its destroy
/// notification is handed the <c>GstCollectData</c> block rather than a user
/// data pointer, which a managed caller has no use for — so the test imports it
/// locally, the way the availability probe imports its own.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed partial class CollectPadsCallbackTests
{
    /// <summary>
    /// The size of the <c>GstCollectData</c> block the collection allocates.
    /// The C function refuses anything smaller than the structure it fills, and
    /// over allocating is what the parameter is for, so this is generously
    /// above the size of the structure on every platform.
    /// </summary>
    private const uint CollectDataSize = 512;

    [Fact]
    public void ACollectFunctionSurvivesACollectionAndIsStillInvoked()
    {
        using CollectPads pads = CollectPads.New();
        using Pad pad = Pad.New("sink", PadDirection.Sink);

        int calls = 0;
        Install(pads, () => Interlocked.Increment(ref calls));

        nint data = CollectPadsAddPad(pads.Handle, pad.Handle, CollectDataSize, 0, lockPad: 1);
        Assert.NotEqual(0, data);
        GC.KeepAlive(pads);
        GC.KeepAlive(pad);

        // The pad has to be active before it accepts an event or a buffer, and
        // a sink pad with no peer activates in push mode, which is the mode the
        // chain function of the collection is called in.
        Assert.True(pad.SetActive(true));
        pads.Start();
        try
        {
            Assert.True(pad.SendEvent(Event.NewStreamStart("collect-pads-test")));

            using Segment segment = Segment.New();
            segment.Init(Format.Time);
            Assert.True(pad.SendEvent(Event.NewSegment(segment)));

            // Nothing managed refers to the delegate from here on. Anything
            // that released its handle when the setter returned has left the
            // collection free to reclaim it.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // One collected pad means one buffer completes the collection. The
            // function answers a flow other than OK, which is how the library
            // is told to stop collecting rather than to loop over a buffer that
            // was never popped.
            Assert.Equal(FlowReturn.Eos, pad.Chain(Buffer.New()));
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            pads.Stop();
        }
    }

    [Fact]
    public void ABufferFunctionOwnsTheBufferAndAClipFunctionReplacesIt()
    {
        using CollectPads pads = CollectPads.New();
        using Pad pad = Pad.New("sink", PadDirection.Sink);

        int clipped = 0;
        int collected = 0;
        nint clippedIn = 0;

        // The clip function is handed the buffer that arrived, owns it, and
        // answers the one the collection keeps. Dropping the input and
        // answering a fresh buffer is what a real clipper does at a segment
        // boundary, and it exercises both halves of the ownership.
        pads.SetClipFunction((CollectPads _, CollectData _, Buffer inbuffer, out Buffer? outbuffer) =>
        {
            clipped++;
            clippedIn = inbuffer.Handle;
            outbuffer = Buffer.New();
            return FlowReturn.Ok;
        });

        pads.SetBufferFunction((_, _, buffer) =>
        {
            collected++;

            // A buffer completes the collection here; only the call that
            // reports the end of every stream carries none.
            Assert.NotNull(buffer);
            Assert.NotEqual(clippedIn, buffer.Handle);
            return FlowReturn.Eos;
        });

        nint data = CollectPadsAddPad(pads.Handle, pad.Handle, CollectDataSize, 0, lockPad: 1);
        Assert.NotEqual(0, data);
        GC.KeepAlive(pads);
        GC.KeepAlive(pad);

        Assert.True(pad.SetActive(true));
        pads.Start();
        try
        {
            Assert.True(pad.SendEvent(Event.NewStreamStart("collect-pads-clip-test")));

            using Segment segment = Segment.New();
            segment.Init(Format.Time);
            Assert.True(pad.SendEvent(Event.NewSegment(segment)));

            Assert.Equal(FlowReturn.Eos, pad.Chain(Buffer.New()));
            // The collection clips on the way in and again when it pops the
            // buffer it kept, so the clip function runs at least once per
            // buffer rather than exactly once.
            Assert.True(clipped >= 1, $"clip function ran {clipped} time(s)");
            Assert.Equal(1, collected);
        }
        finally
        {
            pads.Stop();
        }
    }

    [Fact]
    public void ABufferFunctionIsCalledWithNoDataAndNoBufferAtTheEndOfEveryStream()
    {
        using CollectPads pads = CollectPads.New();
        using Pad pad = Pad.New("sink", PadDirection.Sink);

        int calls = 0;
        bool sawEnd = false;

        // Once every pad has reached EOS the collection calls the function with
        // no collect data and no buffer (gstcollectpads.c:1540), which is how
        // the end of the collection is reported rather than an error.
        pads.SetBufferFunction((_, data, buffer) =>
        {
            calls++;
            sawEnd = data is null && buffer is null;
            return FlowReturn.Eos;
        });

        nint data = CollectPadsAddPad(pads.Handle, pad.Handle, CollectDataSize, 0, lockPad: 1);
        Assert.NotEqual(0, data);
        GC.KeepAlive(pads);
        GC.KeepAlive(pad);

        Assert.True(pad.SetActive(true));
        pads.Start();
        try
        {
            Assert.True(pad.SendEvent(Event.NewStreamStart("collect-pads-eos-test")));

            using Segment segment = Segment.New();
            segment.Init(Format.Time);
            Assert.True(pad.SendEvent(Event.NewSegment(segment)));
            Assert.True(pad.SendEvent(Event.NewEos()));

            Assert.Equal(1, calls);
            Assert.True(sawEnd, "the buffer function was not called with the end of the collection");
        }
        finally
        {
            pads.Stop();
        }
    }

    [Fact]
    public void AClipFunctionThatAnswersNoBufferDropsIt()
    {
        using CollectPads pads = CollectPads.New();
        using Pad pad = Pad.New("sink", PadDirection.Sink);

        int clipped = 0;
        int collected = 0;

        // A clip function that leaves no buffer has dropped the one it was
        // given, which the collection reads before it reads the answer: the
        // chain returns OK and nothing is ever collected.
        pads.SetClipFunction((CollectPads _, CollectData _, Buffer _, out Buffer? outbuffer) =>
        {
            clipped++;
            outbuffer = null;
            return FlowReturn.Ok;
        });

        pads.SetBufferFunction((_, _, _) =>
        {
            collected++;
            return FlowReturn.Eos;
        });

        nint data = CollectPadsAddPad(pads.Handle, pad.Handle, CollectDataSize, 0, lockPad: 1);
        Assert.NotEqual(0, data);
        GC.KeepAlive(pads);
        GC.KeepAlive(pad);

        Assert.True(pad.SetActive(true));
        pads.Start();
        try
        {
            Assert.True(pad.SendEvent(Event.NewStreamStart("collect-pads-drop-test")));

            using Segment segment = Segment.New();
            segment.Init(Format.Time);
            Assert.True(pad.SendEvent(Event.NewSegment(segment)));

            // The collection clips on the way in and again when it looks for a
            // buffer to collect, so the count is a floor rather than an exact
            // number; what matters is that nothing was ever collected.
            Assert.Equal(FlowReturn.Ok, pad.Chain(Buffer.New()));
            Assert.True(clipped >= 1, $"clip function ran {clipped} time(s)");
            Assert.Equal(0, collected);
        }
        finally
        {
            pads.Stop();
        }
    }

    /// <summary>
    /// Installs the collect function from a frame of its own, so that no local
    /// of the test keeps the delegate alive.
    /// </summary>
    /// <param name="pads">The collection to install into.</param>
    /// <param name="onCollected">What the function reports through.</param>
    private static void Install(CollectPads pads, Action onCollected)
    {
        pads.SetFunction(_ =>
        {
            onCollected();
            return FlowReturn.Eos;
        });
    }

    /// <summary>Adds a pad to a collection.</summary>
    /// <param name="pads">The collection.</param>
    /// <param name="pad">The sink pad to add.</param>
    /// <param name="size">The size of the <c>GstCollectData</c> to allocate.</param>
    /// <param name="destroyNotify">The notification, or <c>0</c>.</param>
    /// <param name="lockPad">Whether the pad is kept in the waiting state.</param>
    /// <returns>The <c>GstCollectData</c> the collection allocated, or <c>0</c>.</returns>
    [LibraryImport("GstBase", EntryPoint = "gst_collect_pads_add_pad")]
    private static partial nint CollectPadsAddPad(
        nint pads,
        nint pad,
        uint size,
        nint destroyNotify,
        int lockPad);
}
