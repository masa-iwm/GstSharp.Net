using Gst;
using Gst.Base;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="BaseSink.DoPreroll"/> and the preroll lock trio, driven by a
/// real pipeline in both shapes the library uses it in: from a render override
/// that waited on the clock, and from a pulling thread the sink owns.
/// </summary>
/// <remarks>
/// The sources are core elements, the media is generated, and every wait is
/// bounded, so a regression fails the test rather than hanging the suite.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class BaseSinkDoPrerollTests
{
    private static readonly ClockTime StateTimeout = ClockTime.FromSeconds(10);

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises the tests.</summary>
    /// <param name="output">Where diagnostics go.</param>
    public BaseSinkDoPrerollTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A render override that waited on the clock catches the PLAYING to
    /// PAUSED change made meanwhile: its preroll blocks in PAUSED, returns
    /// <see cref="FlowReturn.Ok"/> once the pipeline plays again, and
    /// <see cref="FlowReturn.Flushing"/> when the pipeline goes to READY
    /// instead: the pad deactivation sets the flush (<c>gstbasesink.c:4667</c>),
    /// the wait returns <see cref="FlowReturn.Flushing"/>
    /// (<c>:2442-2443</c>, <c>:2454</c>) and the preroll hands it on
    /// (<c>:2559-2563</c>).
    /// </summary>
    [Fact]
    public void ARenderOverridePrerollsAfterAClockWait()
    {
        using Pipeline pipeline = Pipeline.New("render-preroll");
        using Element source = MakeFakeSource(pull: false);
        using RenderPrerollSink sink = new();

        Assert.True(pipeline.Add(source));
        Assert.True(pipeline.Add(sink));
        Assert.True(source.Link(sink));

        try
        {
            Play(pipeline);

            // Every render prerolls with the lock the library already holds,
            // and while the pipeline plays there is nothing to preroll.
            Assert.True(WaitUntil(() => sink.PlainOk >= 5), "The sink rendered no buffers.");
            Assert.Equal(0, sink.PlainFailed);

            // PLAYING to PAUSED while a render waits on the clock: the preroll
            // it calls next completes the state change and then waits.
            sink.ArmClockWait();
            Assert.True(sink.Waiting.Wait(WaitTimeout), "No render waited on the clock.");
            Assert.Equal(StateChangeReturn.Async, pipeline.SetState(State.Paused));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State paused, out _, StateTimeout));
            Assert.Equal(State.Paused, paused);
            Assert.Equal(ClockReturn.Unscheduled, sink.ClockResult);
            Assert.False(sink.Returned.Wait(TimeSpan.FromMilliseconds(200)), "The preroll returned in PAUSED.");

            Play(pipeline);
            Assert.True(sink.Returned.Wait(WaitTimeout), "The preroll did not return in PLAYING.");
            Assert.Equal(FlowReturn.Ok, sink.ArmedResult);

            // The same wait, ended by PAUSED to READY rather than by PLAYING.
            sink.ArmClockWait();
            Assert.True(sink.Waiting.Wait(WaitTimeout), "No render waited on the clock again.");
            Assert.Equal(StateChangeReturn.Async, pipeline.SetState(State.Paused));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out _, out _, StateTimeout));
            Assert.False(sink.Returned.Wait(TimeSpan.FromMilliseconds(200)), "The preroll returned in PAUSED.");

            Assert.Equal(StateChangeReturn.Success, pipeline.SetState(State.Ready));
            Assert.True(sink.Returned.Wait(WaitTimeout), "The preroll did not return on the way to READY.");
            Assert.Equal(FlowReturn.Flushing, sink.ArmedResult);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// With nothing to preroll, a null argument returns at once: C tests
    /// <c>need_preroll</c> before it looks at the object (<c>gstbasesink.c:2484</c>).
    /// </summary>
    [Fact]
    public void ANullPrerollReturnsAtOnceWhilePlaying()
    {
        using Pipeline pipeline = Pipeline.New("null-preroll");
        using Element source = MakeFakeSource(pull: false);
        using RenderPrerollSink sink = new();

        Assert.True(pipeline.Add(source));
        Assert.True(pipeline.Add(sink));
        Assert.True(source.Link(sink));

        try
        {
            Play(pipeline);
            Assert.True(WaitUntil(() => sink.PlainOk >= 1), "The sink rendered no buffers.");

            sink.ArmNullPreroll();
            Assert.True(sink.Returned.Wait(WaitTimeout), "The null preroll did not return.");
            Assert.Equal(FlowReturn.Ok, sink.ArmedResult);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// A sink that pulls on a thread of its own prerolls under the lock it
    /// takes itself: the thread blocks in the preroll until the pipeline
    /// plays, and a deactivation wakes it with a flush before the override
    /// joins it.
    /// </summary>
    [Fact]
    public void APullingThreadPrerollsUnderTheLockItTakes()
    {
        using Pipeline pipeline = Pipeline.New("pull-preroll");
        using Element source = MakeFakeSource(pull: true);
        using PullThreadSink sink = new();

        Assert.True(pipeline.Add(source));
        Assert.True(pipeline.Add(sink));
        Assert.True(source.Link(sink));

        try
        {
            // The thread prerolls on the first buffer it pulls, which posts
            // async-done, and then waits inside DoPreroll.
            Assert.Equal(StateChangeReturn.Async, pipeline.SetState(State.Paused));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State paused, out _, StateTimeout));
            Assert.Equal(State.Paused, paused);
            Assert.Contains("activate-pull:true", sink.Log);
            Assert.True(WaitUntil(() => sink.InsideDoPreroll), "The thread is not inside DoPreroll.");
            Assert.Equal(0, sink.Prerolled);

            // Playing releases it, and buffers flow.
            Play(pipeline);
            Assert.True(WaitUntil(() => sink.Prerolled >= 5), "No buffers flowed in PLAYING.");

            // Back in PAUSED the thread prerolls again and waits there.
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Paused));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out _, out _, StateTimeout));
            Assert.True(WaitUntil(() => sink.InsideDoPreroll), "The thread is not inside DoPreroll again.");

            // Deactivation calls the unlock override, sets the flush under the
            // lock, which wakes the preroll, and only then asks the override
            // to stop the thread. The woken thread and the override race to
            // record what they saw, so the order of those two entries is not
            // asserted; the flag the override reads is.
            sink.ClearLog();
            Assert.Equal(StateChangeReturn.Success, pipeline.SetState(State.Ready));
            Assert.True(sink.Exited.Wait(WaitTimeout), "The thread did not end.");
            Assert.Equal(1, sink.Joined);

            IReadOnlyList<string> log = sink.Log;
            _output.WriteLine(string.Join(", ", log));

            int unlock = IndexOf(log, "unlock");
            int flushed = IndexOf(log, "preroll:" + FlowReturn.Flushing);
            int deactivated = IndexOf(log, "activate-pull:false flushing=yes");
            Assert.True(unlock >= 0, "The unlock override was not called.");
            Assert.True(flushed > unlock, "DoPreroll did not return Flushing after the unlock.");
            Assert.True(deactivated > unlock, "The override was not asked to stop, with the flush set, after the unlock.");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    private static Element MakeFakeSource(bool pull)
    {
        Element source = ElementFactory.Make("fakesrc", null)
            ?? throw new InvalidOperationException("fakesrc is a core element and has to exist.");

        Global.UtilSetObjectArg(source, "sizetype", "fixed");
        Global.UtilSetObjectArg(source, "sizemax", "64");
        if (pull)
        {
            // fakesrc answers the scheduling query with pull mode, and reports
            // itself seekable, only when it is told to (gstfakesrc.c:923-927).
            source.SetProperty("can-activate-pull", true);
        }

        return source;
    }

    private static void Play(Pipeline pipeline)
    {
        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));
        Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State state, out _, StateTimeout));
        Assert.Equal(State.Playing, state);
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed >= WaitTimeout)
            {
                return false;
            }

            Thread.Sleep(10);
        }

        return true;
    }

    private static int IndexOf(IReadOnlyList<string> log, string entry)
    {
        for (int i = 0; i < log.Count; i++)
        {
            if (string.Equals(log[i], entry, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
