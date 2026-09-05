using System.Runtime.InteropServices;
using Gst;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The eleven <c>gst_pad_set_*_function_full</c> setters, whose callbacks carry
/// no <c>user_data</c> of their own and are recovered from the pad they are
/// called with.
/// </summary>
/// <remarks>
/// Every test builds a bare <see cref="Pad"/> and drives it directly, which is
/// what these setters are for: the function of a pad is installed by whoever
/// created it, and a pad with no parent runs every one of them.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class PadFunctionTests
{
    [Fact]
    public void AChainFunctionOwnsTheBufferItIsHanded()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        int calls = 0;
        nint seen = 0;
        int refcount = 0;

        pad.SetChainFunction((_, _, buffer) =>
        {
            calls++;
            seen = buffer.Handle;
            refcount = Refcount(buffer.Handle);
            return FlowReturn.Ok;
        });

        Assert.True(pad.SetActive(true));
        using Buffer buf = Buffer.New();
        nint handle = buf.Handle;

        // The test keeps a reference of its own, so that the buffer outlives
        // the call whose only other reference the handler owns and releases.
        Gst.GstNative.MiniObjectRef(handle);
        try
        {
            Assert.Equal(FlowReturn.Ok, pad.Chain(buf));
            Assert.Equal(1, calls);
            Assert.Equal(handle, seen);

            // The wrapper of the test, the reference the test took and the one
            // gst_pad_chain handed the handler.
            Assert.Equal(3, refcount);
            Assert.Equal(1, Refcount(handle));
        }
        finally
        {
            Gst.GstNative.MiniObjectUnref(handle);
        }
    }

    [Fact]
    public void AChainFunctionThatThrowsAnswersErrorAndStillReleasesTheBuffer()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        pad.SetChainFunction((_, _, _) => throw new InvalidOperationException("chain"));

        Assert.True(pad.SetActive(true));
        using Buffer buf = Buffer.New();
        nint handle = buf.Handle;

        Gst.GstNative.MiniObjectRef(handle);
        try
        {
            // The handler threw before it could hand the buffer on, and the
            // scope of the trampoline released it all the same.
            Assert.Equal(FlowReturn.Error, pad.Chain(buf));
            Assert.Equal(1, Refcount(handle));
        }
        finally
        {
            Gst.GstNative.MiniObjectUnref(handle);
        }
    }

    [Fact]
    public void AChainListFunctionOwnsTheListItIsHanded()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        uint length = 0;

        pad.SetChainListFunction((_, _, list) =>
        {
            length = list.Length();
            return FlowReturn.Ok;
        });

        Assert.True(pad.SetActive(true));
        using BufferList list = BufferList.New();
        list.Insert(0, Buffer.New());
        Assert.Equal(1u, list.Length());
        nint handle = list.Handle;

        Gst.GstNative.MiniObjectRef(handle);
        try
        {
            Assert.Equal(FlowReturn.Ok, pad.ChainList(list));
            Assert.Equal(1u, length);
            Assert.Equal(1, Refcount(handle));
        }
        finally
        {
            Gst.GstNative.MiniObjectUnref(handle);
        }
    }

    [Fact]
    public void AnEventFunctionOwnsTheEventAndMayHandItToTheDefault()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        EventType seen = EventType.Unknown;

        pad.SetEventFunction((self, parent, @event) =>
        {
            seen = @event.Type;
            return self.EventDefault(parent, @event);
        });

        Assert.True(pad.SetActive(true));
        Assert.True(pad.SendEvent(Event.NewStreamStart("pad-function-test")));
        Assert.Equal(EventType.StreamStart, seen);
    }

    [Fact]
    public void AnEventFullFunctionReplacesTheEventFunctionOfThePad()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        int plain = 0;
        int full = 0;

        pad.SetEventFunction((self, parent, @event) =>
        {
            plain++;
            return self.EventDefault(parent, @event);
        });

        // Both setters write the same storage of the pad, so the later call
        // wins and the state of the earlier one is released.
        pad.SetEventFullFunction((self, parent, @event) =>
        {
            full++;
            return self.EventDefault(parent, @event) ? FlowReturn.Ok : FlowReturn.Error;
        });

        Assert.True(pad.SetActive(true));
        Assert.True(pad.SendEvent(Event.NewStreamStart("pad-function-test")));
        Assert.Equal(0, plain);
        Assert.Equal(1, full);
    }

    /// <summary>
    /// Unsetting the full event handler leaves the pad answering events the way
    /// a fresh pad of the same direction does, and a plain handler can be
    /// installed afterwards.
    /// </summary>
    /// <remarks>
    /// The C setter would leave its own event_wrap wrapper over a cleared full
    /// function pointer, so a pad in that state crashes on the next event; the
    /// hand written member puts gst_pad_event_default back instead.
    /// </remarks>
    [Fact]
    public void UnsettingTheEventFullFunctionRestoresTheDefaultHandler()
    {
        using Pad fresh = Pad.New("sink", PadDirection.Sink);
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        int full = 0;

        pad.SetEventFullFunction((self, parent, @event) =>
        {
            full++;
            return self.EventDefault(parent, @event) ? FlowReturn.Ok : FlowReturn.Error;
        });

        Assert.True(fresh.SetActive(true));
        Assert.True(pad.SetActive(true));
        Assert.True(pad.SendEvent(Event.NewStreamStart("pad-function-test")));
        Assert.Equal(1, full);

        pad.SetEventFullFunction(null);

        // The pad answers exactly as one that was never given a handler does,
        // rather than crashing in the wrapper the C setter left behind.
        Assert.Equal(
            fresh.SendEvent(Event.NewStreamStart("pad-function-test-fresh")),
            pad.SendEvent(Event.NewStreamStart("pad-function-test-unset")));
        Assert.Equal(
            fresh.SendEvent(Event.NewEos()),
            pad.SendEvent(Event.NewEos()));
        Assert.Equal(1, full);

        // The slot the two setters share is free again.
        int plain = 0;
        pad.SetEventFunction((self, parent, @event) =>
        {
            plain++;
            return self.EventDefault(parent, @event);
        });

        Assert.True(pad.SendEvent(Event.NewStreamStart("pad-function-test-again")));
        Assert.Equal(1, plain);
        Assert.Equal(1, full);
    }

    [Fact]
    public void AQueryFunctionIsHandedTheQueryBorrowed()
    {
        using Pad pad = Pad.New("src", PadDirection.Src);
        int calls = 0;
        pad.SetQueryFunction((_, _, query) =>
        {
            calls++;
            if (query.Type != QueryType.Duration)
            {
                return false;
            }

            query.SetDuration(Format.Time, 42);
            return true;
        });

        using Query query = Query.NewDuration(Format.Time);
        Assert.True(pad.Query(query));
        Assert.Equal(1, calls);
        query.ParseDuration(out Format format, out long duration);
        Assert.Equal(Format.Time, format);
        Assert.Equal(42, duration);
    }

    [Fact]
    public void AGetRangeFunctionProducesTheBufferThePullerReceives()
    {
        using Pad pad = Pad.New("src", PadDirection.Src);
        pad.SetGetRangeFunction((Pad _, Gst.Object? _, ulong offset, uint length, ref Buffer? buffer) =>
        {
            buffer = Buffer.New();
            _ = offset;
            _ = length;
            return FlowReturn.Ok;
        });

        pad.SetActivateModeFunction((_, _, mode, _) => mode == PadMode.Pull);
        Assert.True(pad.ActivateMode(PadMode.Pull, true));

        Assert.Equal(FlowReturn.Ok, pad.GetRange(0, 4, out Buffer? answered));
        using (answered)
        {
            Assert.NotNull(answered);
            Assert.Equal(1, Refcount(answered.Handle));
        }
    }

    [Fact]
    public void AGetRangeFunctionAnswersTheBufferItWasLent()
    {
        using Pad pad = Pad.New("src", PadDirection.Src);
        nint lent = 0;

        pad.SetGetRangeFunction((Pad _, Gst.Object? _, ulong _, uint _, ref Buffer? buffer) =>
        {
            lent = buffer is null ? 0 : buffer.Handle;

            // The buffer stays where it is: the caller supplied it and requires
            // the very same one back.
            return FlowReturn.Ok;
        });

        pad.SetActivateModeFunction((_, _, mode, _) => mode == PadMode.Pull);
        Assert.True(pad.ActivateMode(PadMode.Pull, true));

        using Buffer mine = Buffer.New();
        nint handle = mine.Handle;
        nint answered = handle;
        Assert.Equal(
            (int)FlowReturn.Ok,
            PadGetRange(pad.Handle, 0, 0, &answered));
        Assert.Equal(handle, lent);
        Assert.Equal(handle, answered);
        Assert.Equal(1, Refcount(handle));
    }

    [Fact]
    public void AGetRangeFunctionThatAnswersNoBufferIsCorrectedToAnError()
    {
        using Pad pad = Pad.New("src", PadDirection.Src);
        pad.SetGetRangeFunction((Pad _, Gst.Object? _, ulong _, uint _, ref Buffer? buffer) =>
        {
            buffer = null;
            return FlowReturn.Ok;
        });

        pad.SetActivateModeFunction((_, _, mode, _) => mode == PadMode.Pull);
        Assert.True(pad.ActivateMode(PadMode.Pull, true));

        Assert.Equal(FlowReturn.Error, pad.GetRange(0, 4, out Buffer? answered));
        Assert.Null(answered);
    }

    [Fact]
    public void AGetRangeFunctionThatReplacesTheBufferItWasLentIsCorrectedToAnError()
    {
        using Pad pad = Pad.New("src", PadDirection.Src);
        pad.SetGetRangeFunction((Pad _, Gst.Object? _, ulong _, uint _, ref Buffer? buffer) =>
        {
            // The puller supplied a buffer to fill, and gst_pad_get_range
            // asserts that it gets that very one back. Answering another is a
            // contract the caller reports as an error without releasing what it
            // handed over, so the trampoline answers it instead.
            buffer = Buffer.New();
            return FlowReturn.Ok;
        });

        pad.SetActivateModeFunction((_, _, mode, _) => mode == PadMode.Pull);
        Assert.True(pad.ActivateMode(PadMode.Pull, true));

        using Buffer mine = Buffer.New();
        nint handle = mine.Handle;
        nint answered = handle;
        Assert.Equal((int)FlowReturn.Error, PadGetRange(pad.Handle, 0, 0, &answered));

        // The storage is left alone and the buffer that was lent keeps the one
        // reference the test holds; the buffer the handler made is released
        // with its wrapper.
        Assert.Equal(handle, answered);
        Assert.Equal(1, Refcount(handle));
    }

    [Fact]
    public void AGetRangeFunctionThatThrowsLeavesTheBufferItWasLentAlone()
    {
        using Pad pad = Pad.New("src", PadDirection.Src);
        pad.SetGetRangeFunction(
            (Pad _, Gst.Object? _, ulong _, uint _, ref Buffer? buffer) =>
                throw new InvalidOperationException("getrange"));

        pad.SetActivateModeFunction((_, _, mode, _) => mode == PadMode.Pull);
        Assert.True(pad.ActivateMode(PadMode.Pull, true));

        using Buffer mine = Buffer.New();
        nint handle = mine.Handle;
        nint answered = handle;
        Assert.Equal((int)FlowReturn.Error, PadGetRange(pad.Handle, 0, 0, &answered));
        Assert.Equal(handle, answered);
        Assert.Equal(1, Refcount(handle));
    }

    [Fact]
    public void ALinkFunctionThatThrowsRefusesTheLink()
    {
        using Pad src = Pad.New("src", PadDirection.Src);
        using Pad sink = Pad.New("sink", PadDirection.Sink);

        src.SetLinkFunction((_, _, _) => throw new InvalidOperationException("link"));

        // GST_PAD_LINK_OK is the zero of the enumeration, so a trampoline that
        // answered the default of its return type would permit a link its
        // handler never approved.
        Assert.Equal(PadLinkReturn.Refused, src.Link(sink));
        Assert.False(src.IsLinked());
    }

    [Fact]
    public void AnIterateInternalLinksFunctionHandsTheIteratorOver()
    {
        using Pad src = Pad.New("src", PadDirection.Src);
        using Pad other = Pad.New("other", PadDirection.Src);
        int calls = 0;

        src.SetIterateInternalLinksFunction((_, _) =>
        {
            calls++;
            Gst.GObject.GType padType = new(Pad.GetGType());
            using Gst.GObject.Value value = Gst.GObject.Value.CreateFor(other, padType);
            return Iterator.NewSingle(padType, value);
        });

        // The iterator the handler built is handed over: the wrapper it made
        // detached, and the one here owns what the library gave back.
        using Iterator? links = src.IterateInternalLinks();
        Assert.Equal(1, calls);
        Assert.NotNull(links);
    }

    [Fact]
    public void TheLinkAndUnlinkFunctionsRunWhenThePadsAreLinkedAndUnlinked()
    {
        using Pad src = Pad.New("src", PadDirection.Src);
        using Pad sink = Pad.New("sink", PadDirection.Sink);

        int linked = 0;
        int unlinked = 0;
        src.SetLinkFunction((_, _, peer) =>
        {
            linked++;
            return peer.Direction == PadDirection.Sink ? PadLinkReturn.Ok : PadLinkReturn.Refused;
        });

        src.SetUnlinkFunction((_, _) => unlinked++);

        Assert.Equal(PadLinkReturn.Ok, src.Link(sink));
        Assert.Equal(1, linked);
        Assert.True(src.Unlink(sink));
        Assert.Equal(1, unlinked);
    }

    [Fact]
    public void AnActivateFunctionRunsWhenThePadIsActivated()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        int calls = 0;
        pad.SetActivateFunction((_, _) =>
        {
            calls++;
            return true;
        });

        Assert.True(pad.SetActive(true));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ReplacingAFunctionReleasesTheStateOfTheOneItReplaced()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        WeakReference first = InstallChain(pad);

        pad.SetChainFunction((_, _, _) => FlowReturn.Ok);
        Collect();
        Assert.False(first.IsAlive);
    }

    [Fact]
    public void UnsettingAFunctionReleasesItsStateAndTheDefaultTakesOver()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        WeakReference first = InstallChain(pad);

        pad.SetChainFunction(null);
        Collect();
        Assert.False(first.IsAlive);

        // With no chain function and no parent, the pad has nothing to hand a
        // buffer to, which is the answer of the library rather than a call into
        // freed state.
        Assert.True(pad.SetActive(true));
        Assert.Equal(FlowReturn.NotSupported, pad.Chain(Buffer.New()));
    }

    [Fact]
    public void DisposingThePadReleasesTheStateOfItsFunctions()
    {
        WeakReference first;
        using (Pad pad = Pad.New("sink", PadDirection.Sink))
        {
            first = InstallChain(pad);
        }

        Collect();
        Assert.False(first.IsAlive);
    }

    /// <summary>
    /// Installs a chain function from a frame of its own and answers a weak
    /// reference to the state behind it, so that a test can tell whether the
    /// entry of the pad still holds it.
    /// </summary>
    /// <param name="pad">The pad to install into.</param>
    /// <returns>A weak reference to the state the delegate closed over.</returns>
    private static WeakReference InstallChain(Pad pad)
    {
        object state = new();
        pad.SetChainFunction((_, _, _) =>
        {
            GC.KeepAlive(state);
            return FlowReturn.Ok;
        });

        return new WeakReference(state);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static int Refcount(nint handle) => ((MiniObjectRaw*)handle)->Refcount;

    /// <summary>
    /// <c>gst_pad_get_range</c> with the buffer pointer the managed member does
    /// not expose: its out parameter always arrives empty, and the in place
    /// half of the contract needs a caller that supplies one.
    /// </summary>
    /// <param name="pad">The pad to pull from.</param>
    /// <param name="offset">The offset of the range.</param>
    /// <param name="size">The length of the range.</param>
    /// <param name="buffer">The buffer to fill, in and out.</param>
    /// <returns>The flow the pad answered.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_pad_get_range")]
    private static partial int PadGetRange(nint pad, ulong offset, uint size, nint* buffer);
}
