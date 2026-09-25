using System.Runtime.InteropServices;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Gst.Video;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed element that implements <c>GstColorBalance</c>: the interface is
/// attached when the type is defined, the runtime keeps the list
/// <c>list_channels</c> lends, and <c>playsink</c> drives the element through
/// its own proxy channels.
/// </summary>
[Collection(GstCollection.Name)]
public sealed unsafe partial class SubclassColorBalanceTests
{
    private static readonly ClockTime StateTimeout = ClockTime.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises the tests with the output they write to.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassColorBalanceTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The consumer surface lists the channels the managed element answered,
    /// as the very wrappers it holds, and answers its balance type.
    /// </summary>
    [Fact]
    public void TheConsumerSurfaceReachesTheManagedMembers()
    {
        using ProbeColorBalanceSink sink = new();
        IColorBalance balance = sink.As<IColorBalance>()
            ?? throw new InvalidOperationException("The managed element is not a color balance.");

        IReadOnlyList<ColorBalanceChannel> listed = balance.ListChannels();

        Assert.Equal(sink.Channels.Count, listed.Count);
        for (int i = 0; i < listed.Count; i++)
        {
            Assert.Same(sink.Channels[i], listed[i]);
        }

        Assert.Equal(ColorBalanceType.Hardware, balance.GetBalanceType());

        sink.BalanceType = ColorBalanceType.Software;
        Assert.Equal(ColorBalanceType.Software, balance.GetBalanceType());
    }

    /// <summary>
    /// A value set through the consumer surface reaches the managed
    /// <c>SetValue</c>, is read back through <c>GetValue</c>, and the
    /// notification the implementation fires reaches a handler of the
    /// <c>value-changed</c> signal.
    /// </summary>
    [Fact]
    public void AValueRoundTripsAndItsChangeIsAnnounced()
    {
        using ProbeColorBalanceSink sink = new();
        IColorBalance balance = sink.As<IColorBalance>()
            ?? throw new InvalidOperationException("The managed element is not a color balance.");
        ColorBalanceChannel hue = balance.ListChannels()[2];

        List<(string? Label, int Value)> announced = [];

        // The generated AddValueChangedHandler casts its receiver to an
        // object wrapper, which the view As hands out for a class that does
        // not declare IColorBalance is not, so the signal is connected by
        // name.
        _ = sink.ConnectSignal("value-changed", (sender, args) =>
        {
            announced.Add((((ColorBalanceChannel)args[0]!).Label, (int)args[1]!));
            return null;
        });

        balance.SetValue(hue, 250);
        balance.SetValue(hue, 250);

        Assert.Equal(2, sink.SetValueCalls);
        Assert.Equal("HUE", sink.LastSetLabel);
        Assert.Equal(250, balance.GetValue(hue));

        // The second write changed nothing, so only the first one was
        // announced: GStreamer fires nothing of its own.
        Assert.Equal([("HUE", 250)], announced);
    }

    /// <summary>
    /// <c>list_channels</c> lends a list the element owns, so the runtime keeps
    /// one and hands the same pointer out for as long as the element answers
    /// the same channels.
    /// </summary>
    [Fact]
    public void TheSameChannelsLendTheSameList()
    {
        using ProbeColorBalanceSink sink = new();

        nint first = ColorBalanceListChannels(sink.Handle);
        nint second = ColorBalanceListChannels(sink.Handle);

        Assert.NotEqual(nint.Zero, first);
        Assert.Equal(first, second);
        Assert.Equal(2, sink.ListCalls);
        Assert.Equal(sink.Channels.Select(channel => channel.Handle), GListMarshal.Collect(first));
    }

    /// <summary>
    /// A different answer gets a new list, and the list lent before stays
    /// readable, because a caller on another thread may still be walking it.
    /// </summary>
    [Fact]
    public void ChangedChannelsGetANewListAndTheOldOneStaysValid()
    {
        using ProbeColorBalanceSink sink = new();

        nint before = ColorBalanceListChannels(sink.Handle);

        using ColorBalanceChannel gamma = ColorBalanceChannel.New("GAMMA", 0, 100);
        sink.Answer = [sink.Channels[0], gamma];
        nint after = ColorBalanceListChannels(sink.Handle);

        Assert.NotEqual(before, after);
        Assert.Equal([sink.Channels[0].Handle, gamma.Handle], GListMarshal.Collect(after));
        Assert.Equal(new[] { "BRIGHTNESS", "CONTRAST", "HUE", "SATURATION" }, LabelsOf(before));

        sink.Answer = [];
        Assert.Equal(nint.Zero, ColorBalanceListChannels(sink.Handle));
        Assert.Equal(new[] { "BRIGHTNESS", "GAMMA" }, LabelsOf(after));
    }

    /// <summary>
    /// The list holds a reference to every channel in it, so a channel the
    /// element dropped stays alive while a list still names it, and the
    /// references go when the element is finalized.
    /// </summary>
    [Fact]
    public void TheListsHoldTheirChannelsUntilTheElementIsFinalized()
    {
        nint channel;
        uint listed;

        using (ProbeColorBalanceSink sink = new())
        {
            channel = sink.Channels[1].Handle;
            uint unlisted = RefCountOf(channel);

            _ = ColorBalanceListChannels(sink.Handle);
            listed = RefCountOf(channel);
            Assert.Equal(unlisted + 1, listed);

            // A second list that names the channel again takes a reference of
            // its own.
            sink.Answer = [sink.Channels[1]];
            _ = ColorBalanceListChannels(sink.Handle);
            Assert.Equal(listed + 1, RefCountOf(channel));

            _ = GObjectNative.ObjectRef(channel);
        }

        // The wrapper of the element was the last owner, so the element is
        // gone and both lists released what they held. What is left is the
        // reference of the channel wrapper, which the collector has not run
        // for, and the one taken above to keep the channel readable here.
        Assert.Equal(listed, RefCountOf(channel));
        GObjectNative.ObjectUnref(channel);
    }

    /// <summary>
    /// A <c>ListChannels</c> that throws is reported, and the caller gets the
    /// list lent before, so a channel <c>playsink</c> already found does not
    /// disappear from under its <c>g_assert</c>.
    /// </summary>
    [Fact]
    public void AThrowingListChannelsKeepsThePreviousList()
    {
        using ProbeColorBalanceSink sink = new();
        nint before = ColorBalanceListChannels(sink.Handle);

        List<Exception> reported = [];
        void OnFailure(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        nint thrown;
        nint nulled;
        nint holed;
        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            sink.ThrowOnList = true;
            thrown = ColorBalanceListChannels(sink.Handle);
            sink.ThrowOnList = false;

            sink.Answer = null;
            nulled = ColorBalanceListChannels(sink.Handle);

            sink.Answer = [sink.Channels[0], null!];
            holed = ColorBalanceListChannels(sink.Handle);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Equal(before, thrown);
        Assert.Equal(before, nulled);
        Assert.Equal(before, holed);

        lock (reported)
        {
            Assert.Equal(3, reported.Count);
            Assert.All(reported, exception => Assert.IsType<InvalidOperationException>(exception));
        }
    }

    /// <summary>
    /// A failed answer after a rebuild gets the newest list, not the first
    /// one: a disposed channel in the answer is reported and the caller is
    /// given the list that replaced the original.
    /// </summary>
    [Fact]
    public void AFailureAfterARebuildKeepsTheNewestList()
    {
        using ProbeColorBalanceSink sink = new();
        nint first = ColorBalanceListChannels(sink.Handle);

        sink.Answer = [sink.Channels[2]];
        nint newest = ColorBalanceListChannels(sink.Handle);
        Assert.NotEqual(first, newest);

        ColorBalanceChannel gone = ColorBalanceChannel.New("GONE", 0, 1);
        gone.Dispose();

        List<Exception> reported = [];
        void OnFailure(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        nint answered;
        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            sink.Answer = [sink.Channels[2], gone];
            answered = ColorBalanceListChannels(sink.Handle);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Equal(newest, answered);

        lock (reported)
        {
            Assert.Contains(reported, exception => exception is ObjectDisposedException);
        }
    }

    /// <summary>
    /// A <c>BalanceType</c> that throws is reported and the caller is told
    /// software, and a <c>SetValue</c> that throws is reported and changes
    /// nothing.
    /// </summary>
    [Fact]
    public void AThrowingBalanceTypeOrSetValueIsReported()
    {
        using ProbeColorBalanceSink sink = new();
        IColorBalance balance = sink.As<IColorBalance>()
            ?? throw new InvalidOperationException("The managed element is not a color balance.");
        ColorBalanceChannel brightness = sink.Channels[0];

        List<Exception> reported = [];
        void OnFailure(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        ColorBalanceType type;
        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            sink.ThrowOnBalanceType = true;
            type = balance.GetBalanceType();

            sink.ThrowOnSetValue = true;
            balance.SetValue(brightness, 300);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Equal(ColorBalanceType.Software, type);
        Assert.Equal(1, sink.SetValueCalls);
        Assert.Equal(0, sink.GetValue(brightness));

        lock (reported)
        {
            Assert.Contains(reported, exception => exception.Message.Contains("BalanceType", StringComparison.Ordinal));
            Assert.Contains(reported, exception => exception.Message.Contains("SetValue", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A <c>GetValue</c> that throws is reported, and the caller is told the
    /// minimum of the channel, which is what the C answers for an element that
    /// implements no <c>get_value</c>.
    /// </summary>
    [Fact]
    public void AThrowingGetValueAnswersTheMinimum()
    {
        using ProbeColorBalanceSink sink = new();
        IColorBalance balance = sink.As<IColorBalance>()
            ?? throw new InvalidOperationException("The managed element is not a color balance.");
        ColorBalanceChannel contrast = sink.Channels[1];

        int reports = 0;
        void OnFailure(Exception exception) => Interlocked.Increment(ref reports);

        int answered;
        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            sink.ThrowOnGetValue = true;
            answered = balance.GetValue(contrast);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Equal(1, reports);
        Assert.Equal(ProbeColorBalanceSink.MinValue, answered);
    }

    /// <summary>
    /// A declaration put into the registration of a type that implements none
    /// of it answers what the C answers for an empty slot, and says why.
    /// </summary>
    [Fact]
    public void AMisdeclaredElementAnswersTheDefaultsAndWarns()
    {
        using MisdeclaredBalance element = new();
        IColorBalance balance = element.As<IColorBalance>()
            ?? throw new InvalidOperationException("The declaration did not attach the interface.");

        ColorBalanceType type = ColorBalanceType.Hardware;
        IReadOnlyList<ColorBalanceChannel> listed = [];
        IReadOnlyList<string> logged = InitializeLogProbe.CaptureWhile(() =>
        {
            type = balance.GetBalanceType();
            listed = balance.ListChannels();
        });

        Assert.Equal(ColorBalanceType.Software, type);
        Assert.Empty(listed);

        _output.WriteLine($"log probe installed: {InitializeLogProbe.IsInstalled}, logged: {logged.Count}");
        if (InitializeLogProbe.IsInstalled)
        {
            Assert.Contains(
                logged,
                message => message.Contains("does not implement IColorBalanceImplementation", StringComparison.Ordinal)
                    && message.Contains(nameof(ProbeColorBalanceSink), StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The consumer the interface exists for: <c>playsink</c> picks the
    /// managed sink as its color balance element because it offers the four
    /// channels, answers the balance type the sink answers, and hands a value
    /// set on its proxy channel to the managed <c>SetValue</c> after finding
    /// the channel by label - the lookup that <c>g_assert</c>s.
    /// </summary>
    [RequiresElementFact("playsink", "videotestsrc", "videoconvert")]
    public void PlaysinkDrivesTheManagedElementThroughItsProxy()
    {
        using ProbeColorBalanceSink sink = new();
        using Pipeline pipeline = Pipeline.New("color-balance-playsink");
        Element source = ElementFactory.Make("videotestsrc", "source")
            ?? throw new InvalidOperationException("videotestsrc is not available.");
        Element play = ElementFactory.Make("playsink", "play")
            ?? throw new InvalidOperationException("playsink is not available.");

        play.SetProperty("video-sink", sink);
        Assert.True(pipeline.AddMany(source, play));

        // A description would link the source to the first request pad that
        // fits, which is the audio one, so the video pad is asked for by name.
        using Pad videoPad = play.RequestPadSimple("video_sink")
            ?? throw new InvalidOperationException("playsink gave no video pad.");
        using Pad sourcePad = source.GetStaticPad("src")
            ?? throw new InvalidOperationException("videotestsrc has no source pad.");
        Assert.Equal(PadLinkReturn.Ok, sourcePad.Link(videoPad));

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Paused));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State state, out State _, StateTimeout));
            Assert.Equal(State.Paused, state);

            IColorBalance proxy = play.As<IColorBalance>()
                ?? throw new InvalidOperationException("playsink is not a color balance.");

            Assert.True(sink.ListCalls > 0, "playsink never asked the managed element for its channels.");
            Assert.Equal(ColorBalanceType.Hardware, proxy.GetBalanceType());

            ColorBalanceChannel saturation = proxy.ListChannels()
                .Single(channel => channel.Label == "SATURATION");
            Assert.DoesNotContain(saturation, sink.Channels);

            proxy.SetValue(saturation, 400);

            Assert.Equal("SATURATION", sink.LastSetLabel);
            Assert.Equal(400, sink.LastSetValue);
            Assert.Equal(400, sink.GetValue(sink.Channels[3]));
            Assert.Equal(400, proxy.GetValue(saturation));
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    private static IReadOnlyList<string?> LabelsOf(nint list) =>
        GListMarshal.Collect(list)
            .Select(handle => Marshal.PtrToStringUTF8(((ColorBalanceChannelRaw*)handle)->Label))
            .ToArray();

    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    [LibraryImport("GstVideo", EntryPoint = "gst_color_balance_list_channels")]
    private static partial nint ColorBalanceListChannels(nint balance);

    /// <summary>
    /// A video sink whose registration carries the color balance declaration
    /// of <see cref="ProbeColorBalanceSink"/> although it implements none of it.
    /// </summary>
    private sealed class MisdeclaredBalance : VideoSink, IManagedSubclass<MisdeclaredBalance>
    {
        private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

        private static readonly SubclassType Definition = DefineSubclass<MisdeclaredBalance>(
            "GstSharpTestMisdeclaredColorBalance",
            static config => config.AddPadTemplate(SinkTemplate),
            new SubclassOptions { Interfaces = [ColorBalanceImplementation.For<ProbeColorBalanceSink>()] });

        /// <summary>Creates an element of the type.</summary>
        internal MisdeclaredBalance()
            : base(Definition.NewInstance())
        {
        }

        private MisdeclaredBalance(SubclassCtorArgs args)
            : base(args)
        {
        }

        /// <summary>Builds the wrapper of an instance native code created.</summary>
        /// <param name="args">What the runtime says about the instance.</param>
        /// <returns>The wrapper.</returns>
        public static MisdeclaredBalance CreateWrapper(SubclassCtorArgs args) => new(args);

        private static PadTemplate NewSinkTemplate()
        {
            using Caps caps = Caps.FromString("video/x-raw")
                ?? throw new InvalidOperationException("The sink caps could not be parsed.");

            return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
                ?? throw new InvalidOperationException("The sink pad template could not be created.");
        }
    }
}
