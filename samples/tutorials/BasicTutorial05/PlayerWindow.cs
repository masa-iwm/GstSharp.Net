// The window of basic tutorial 5: the video area, the three transport buttons,
// the seek slider, the list of streams and the timer that refreshes them.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using Gst;
using Gst.GLib;
using Gst.Video;

/// <summary>
/// The user interface of the tutorial, which is <c>create_ui</c>, the timer and
/// every callback of the C original in one place.
/// </summary>
internal sealed class PlayerWindow : Window, IDisposable
{
    /// <summary>The pipeline, which is a playbin as it is upstream.</summary>
    private readonly Pipeline _playbin;

    /// <summary>The bus of the pipeline, read once and held.</summary>
    private readonly Bus _bus;

    /// <summary>The area the video sink renders into.</summary>
    private readonly VideoHost _host = new() { MinWidth = 320, MinHeight = 240 };

    /// <summary>The seek slider, whose unit is a second of media.</summary>
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 100 };

    /// <summary>The read-only text area the stream tags are written to.</summary>
    private readonly TextBox _streams = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        Width = 260,
        Text = string.Empty,
    };

    /// <summary>The once-a-second tick that refreshes the interface.</summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>How long the process has been running, for <c>--timeout</c>.</summary>
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();

    /// <summary>
    /// The native window handle of <see cref="_host"/>, or zero when there is
    /// none. Written on the UI thread and read on a streaming thread, which is
    /// why every access goes through <see cref="Volatile"/>.
    /// </summary>
    private nuint _windowHandle;

    /// <summary>The state of the pipeline, as the bus last reported it.</summary>
    private State _state = State.Null;

    /// <summary>How long the media is, in nanoseconds, or -1 when unknown.</summary>
    private long _duration = -1;

    /// <summary>
    /// Whether the slider is being written by the timer rather than dragged.
    /// </summary>
    private bool _refreshing;

    /// <summary>Whether the pipeline has been started.</summary>
    private bool _started;

    /// <summary>Whether the pipeline has been torn down.</summary>
    private bool _disposed;

    /// <summary>
    /// Builds the window and the pipeline, and wires the one to the other.
    /// </summary>
    internal PlayerWindow()
    {
        Title = "Basic tutorial 5: GUI toolkit integration";
        Width = 640;
        Height = 480;

        _playbin = CreatePlaybin();
        _bus = _playbin.GetBus();

        // The three signals are emitted on a streaming thread. Upstream turns
        // each of them into an application message on the bus rather than
        // touching the widgets from there, and so does this.
        _ = _playbin.ConnectSignal("video-tags-changed", OnTagsChanged);
        _ = _playbin.ConnectSignal("audio-tags-changed", OnTagsChanged);
        _ = _playbin.ConnectSignal("text-tags-changed", OnTagsChanged);

        Content = Compose();

        _host.HandleChanged += OnHandleChanged;
        _slider.ValueChanged += OnSliderMoved;
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Lays the window out the way <c>create_ui</c> does: the video area and
    /// the stream list side by side, the buttons and the slider below them.
    /// </summary>
    /// <returns>The root of the layout.</returns>
    private Control Compose()
    {
        StackPanel controls = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(2),
        };

        controls.Children.Add(Transport("Play", State.Playing));
        controls.Children.Add(Transport("Pause", State.Paused));

        // Upstream's stop button goes to READY and not to NULL: READY keeps the
        // pipeline built and its window handle, so pressing play again starts
        // from the beginning without renegotiating anything.
        controls.Children.Add(Transport("Stop", State.Ready));

        _slider.Margin = new Thickness(8, 0, 2, 0);
        _slider.MinWidth = 320;
        controls.Children.Add(_slider);

        DockPanel root = new();
        DockPanel.SetDock(controls, Dock.Bottom);
        DockPanel.SetDock(_streams, Dock.Right);
        root.Children.Add(controls);
        root.Children.Add(_streams);
        root.Children.Add(_host);

        return root;
    }

    /// <summary>
    /// Makes one of the three buttons, each of which is a state change and
    /// nothing else.
    /// </summary>
    /// <param name="caption">What the button says.</param>
    /// <param name="state">The state the pipeline is asked for.</param>
    /// <returns>The button.</returns>
    private Button Transport(string caption, State state)
    {
        Button button = new() { Content = caption, Margin = new Thickness(2) };
        button.Click += (_, _) => _playbin.SetState(state);
        return button;
    }

    /// <summary>
    /// Builds the playbin and gives it the media and, where it has to be said,
    /// a video sink that can take a window handle.
    /// </summary>
    /// <returns>The pipeline.</returns>
    /// <exception cref="InvalidOperationException">playbin is not registered.</exception>
    private static Pipeline CreatePlaybin()
    {
        // The C program keeps a GstElement*. This asks for a Pipeline, which
        // playbin is: no wrapper is registered for GstPlayBin itself, so the
        // registry builds the closest ancestor it knows, and that is
        // Gst.Pipeline.
        if (ElementFactory.Make("playbin", "playbin") is not Pipeline playbin)
        {
            throw new InvalidOperationException("playbin could not be created.");
        }

        Global.UtilSetObjectArg(playbin, "uri", ToolkitIntegration.Current.Uri);
        ChooseVideoSink(playbin);

        return playbin;
    }

    /// <summary>
    /// Names a video sink on the one configuration where the default choice
    /// cannot work.
    /// </summary>
    /// <param name="playbin">The pipeline to configure.</param>
    /// <remarks>
    /// <para>
    /// A window handle is only as portable as the windowing system behind it.
    /// On Windows it is an HWND and on macOS an NSView, and whatever sink
    /// playbin picks there understands the one its platform has. On Linux the
    /// handle is an X11 XID — the upstream page says the same thing about the
    /// overlay route — and <b>Wayland has no XID at all</b>. Avalonia's Linux
    /// backend is X11, so on a Wayland session it runs through XWayland and its
    /// controls do have an XID; but playbin, which knows nothing of that, may
    /// well pick <c>waylandsink</c>, which cannot take one.
    /// </para>
    /// <para>
    /// So on a Wayland session the sink is named explicitly, and an X11-capable
    /// one is asked for. If neither is installed the run continues with a
    /// message rather than a failure: the picture may then appear in a window
    /// of the sink's own instead of inside this one, which is exactly the
    /// failure <c>GstVideoOverlay</c> exists to avoid and worth seeing when it
    /// happens.
    /// </para>
    /// </remarks>
    private static void ChooseVideoSink(Pipeline playbin)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Console.Error.WriteLine(
                "BasicTutorial05: DISPLAY is not set, so there is no X server to take a window handle from. On a Wayland-only session, start XWayland or run with --headless-selftest.");
            return;
        }

        bool onWayland =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
            string.Equals(
                Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                "wayland",
                StringComparison.OrdinalIgnoreCase);

        if (!onWayland)
        {
            return;
        }

        foreach (string factory in (string[])["xvimagesink", "ximagesink"])
        {
            if (ElementFactory.Make(factory, null) is not Element sink)
            {
                continue;
            }

            // gst_util_set_object_arg deserializes a string into the type of
            // the property, and there is no deserializer for an object-valued
            // one: the element property of a playbin is written with a GValue
            // that holds the element. The value holds a reference while it
            // lives and playbin takes one of its own; the wrapper keeps the one
            // it was born with.
            using Gst.GObject.Value value = Gst.GObject.Value.New(sink.NativeType);
            value.SetObject(sink);
            playbin.SetProperty("video-sink", value);

            Console.WriteLine($"sink:        {factory}, because this is a Wayland session");
            return;
        }

        Console.Error.WriteLine(
            "BasicTutorial05: this is a Wayland session and neither xvimagesink nor ximagesink is installed, so the sink playbin picks may open a window of its own.");
    }

    /// <summary>
    /// Answers the message a video sink posts when it needs to know what to
    /// render into.
    /// </summary>
    /// <param name="bus">The bus the message was posted on.</param>
    /// <param name="message">The message, borrowed for the length of the call.</param>
    /// <returns>What is to happen to the message.</returns>
    /// <remarks>
    /// <para>
    /// This runs on the thread that posted the message — a streaming thread —
    /// and that is the whole point of it being a sync handler. playbin builds
    /// its video sink long after the pipeline was started, and the sink needs
    /// the answer <i>right then</i>: a message that went onto the queue and was
    /// read a tick later would arrive after the sink had already opened a
    /// window of its own. The handler therefore touches nothing of the toolkit
    /// and only reads a handle that the UI thread wrote before the pipeline was
    /// ever started.
    /// </para>
    /// <para>
    /// Everything that is not the prepare-window-handle message is passed, so
    /// the ordinary error, end of stream and state-changed messages still reach
    /// the queue that <see cref="OnTick"/> reads.
    /// </para>
    /// </remarks>
    private BusSyncReply OnSyncMessage(Bus bus, Message message)
    {
        if (!VideoGlobal.IsVideoOverlayPrepareWindowHandleMessage(message))
        {
            return BusSyncReply.Pass;
        }

        nuint handle = Volatile.Read(ref _windowHandle);

        if (handle == 0 || message.Src?.As<IVideoOverlay>() is not IVideoOverlay overlay)
        {
            // Nothing to answer with. Passing it leaves the sink to do what it
            // would have done with no handler at all.
            return BusSyncReply.Pass;
        }

        overlay.SetWindowHandle(handle);
        return BusSyncReply.Drop;
    }

    /// <summary>
    /// Takes the window handle of the video area, and starts the pipeline the
    /// first time one arrives.
    /// </summary>
    /// <param name="handle">The handle, or <see langword="null"/> when it went away.</param>
    private void OnHandleChanged(IPlatformHandle? handle)
    {
        if (handle is null)
        {
            Volatile.Write(ref _windowHandle, 0);
            return;
        }

        Volatile.Write(ref _windowHandle, (nuint)(nint)handle.Handle);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"handle:      {handle.HandleDescriptor ?? "?"} 0x{handle.Handle:x}"));

        if (_started)
        {
            // The control was re-attached, so the sink is holding a window that
            // no longer exists and has to be told about the new one. There is
            // no prepare-window-handle message this time: the sink asks once.
            _playbin.As<IVideoOverlay>()?.SetWindowHandle(Volatile.Read(ref _windowHandle));
            return;
        }

        _started = true;

        // The sync handler is installed before the pipeline moves, because a
        // sink built during the state change would post its question into a bus
        // with no handler on it.
        _bus.SetSyncHandler(OnSyncMessage);

        if (_playbin.SetState(State.Playing) == StateChangeReturn.Failure)
        {
            Console.Error.WriteLine("BasicTutorial05: the pipeline refused to go to PLAYING.");
            ToolkitIntegration.ExitCode = 1;
            Close();
            return;
        }

        _timer.Start();
    }

    /// <summary>
    /// Posts the tags-changed notification onto the bus, from whichever thread
    /// the signal was emitted on.
    /// </summary>
    /// <param name="sender">The playbin.</param>
    /// <param name="arguments">The index of the stream, which is not needed here.</param>
    /// <returns>Nothing; the signal returns void.</returns>
    /// <remarks>
    /// This is <c>tags_cb</c>, unchanged in intent. The bus is the thread hand
    /// off: reading tags here would read them on a streaming thread and writing
    /// them into the text area would touch a control from one, so an empty
    /// structure is posted and <see cref="OnTick"/>, which runs on the UI
    /// thread, does the work. Marshalling with
    /// <c>Dispatcher.UIThread.Post</c> would work as well and would be one
    /// object less, but it would also be a different program from the one the
    /// page walks through, and the bus is already being read on the right
    /// thread.
    /// </remarks>
    private object? OnTagsChanged(Gst.GObject.Object sender, object?[] arguments)
    {
        // The structure is copied into the message, so this wrapper keeps its
        // own and gives it back here; the message itself is consumed by the
        // post, which disposes its wrapper.
        using Structure structure = Structure.NewEmpty("tags-changed");
        _ = _playbin.PostMessage(Message.NewApplication(_playbin, structure));

        return null;
    }

    /// <summary>
    /// Reads the bus and refreshes the interface, once a second.
    /// </summary>
    /// <param name="sender">The timer.</param>
    /// <param name="e">Nothing.</param>
    private void OnTick(object? sender, EventArgs e)
    {
        PumpBus();
        Refresh();
        GstSharp.DrainPendingReleases();

        TimeSpan bound = ToolkitIntegration.Current.Timeout;

        if (bound > TimeSpan.Zero && _elapsed.Elapsed >= bound)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"timeout:     {bound.TotalSeconds:F0} s elapsed, closing"));
            Close();
        }
    }

    /// <summary>
    /// Empties the bus of everything this tutorial listens for.
    /// </summary>
    /// <remarks>
    /// <c>gst_bus_add_signal_watch</c> attaches the bus to a GLib main context
    /// and has no counterpart here, so the four message types the C original
    /// connects to are popped with a zero timeout instead. The loop runs until
    /// the bus is empty, because a second of playback can post more than one
    /// message and a queue that is drained one message per tick would fall
    /// behind.
    /// </remarks>
    private void PumpBus()
    {
        while (true)
        {
            using Message? message = _bus.TimedPopFiltered(
                ClockTime.FromNanoseconds(0),
                MessageType.Error | MessageType.Eos | MessageType.StateChanged | MessageType.Application);

            if (message is null)
            {
                return;
            }

            switch (message.Type)
            {
                case MessageType.Error:
                    (GException error, string? debug) = message.ParseError();
                    Console.Error.WriteLine(
                        $"error:       from element {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"debug:       {debug ?? "none"}");

                    // As upstream: an error stops playback but leaves the
                    // window up. The exit code is the rule of this directory
                    // rather than of the C original, which returns 0.
                    ToolkitIntegration.ExitCode = 1;
                    _playbin.SetState(State.Ready);
                    break;

                case MessageType.Eos:
                    Console.WriteLine("eos:         end of stream reached");
                    _playbin.SetState(State.Ready);
                    break;

                case MessageType.StateChanged:
                    OnStateChanged(message);
                    break;

                case MessageType.Application:
                    if (message.GetStructure()?.GetName() == "tags-changed")
                    {
                        AnalyzeStreams();
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Keeps track of what state the pipeline is in.
    /// </summary>
    /// <param name="message">The state-changed message.</param>
    private void OnStateChanged(Message message)
    {
        if (message.Src is not { } source || source.Handle != _playbin.Handle)
        {
            // Every element in the pipeline posts these. Only the playbin's own
            // say what the application is playing.
            return;
        }

        message.ParseStateChanged(out State old, out _state, out _);
        Console.WriteLine($"state:       {old} -> {_state}");

        if (old == State.Ready && _state == State.Paused)
        {
            // For extra responsiveness, as upstream puts it: the slider and the
            // duration are worth having the moment there is something to read
            // them from, rather than up to a second later.
            Refresh();
        }
    }

    /// <summary>
    /// Moves the slider to where the stream is.
    /// </summary>
    private void Refresh()
    {
        if (_state < State.Paused)
        {
            return;
        }

        if (_duration < 0 && !_playbin.QueryDuration(Format.Time, out _duration))
        {
            Console.Error.WriteLine("BasicTutorial05: the duration could not be queried.");
        }

        if (!_playbin.QueryPosition(Format.Time, out long position))
        {
            return;
        }

        // Writing the slider raises ValueChanged, and a seek to where the
        // stream already is would flush the pipeline once a second. Upstream
        // blocks the signal handler around the write; this flag is the same
        // thing. Maximum is written under it as well, because lowering it can
        // clamp Value and raise the event on its own.
        _refreshing = true;

        try
        {
            if (_duration > 0)
            {
                _slider.Maximum = (double)_duration / ClockTime.NanosecondsPerSecond;
            }

            _slider.Value = (double)position / ClockTime.NanosecondsPerSecond;
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// Seeks to where the slider was dragged.
    /// </summary>
    /// <param name="sender">The slider.</param>
    /// <param name="e">The old and the new position, in seconds.</param>
    private void OnSliderMoved(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        // FLUSH throws away everything that is already in the pipeline so that
        // the new position is seen at once; KEY_UNIT lets the demuxer land on
        // the nearest key frame instead of decoding up to the exact one asked
        // for.
        _playbin.SeekSimple(
            Format.Time,
            SeekFlags.Flush | SeekFlags.KeyUnit,
            (long)(e.NewValue * ClockTime.NanosecondsPerSecond));
    }

    /// <summary>
    /// Writes what is known about every stream into the text area.
    /// </summary>
    private void AnalyzeStreams()
    {
        int videoStreams = _playbin.GetProperty<int>("n-video");
        int audioStreams = _playbin.GetProperty<int>("n-audio");
        int textStreams = _playbin.GetProperty<int>("n-text");

        StringBuilder text = new();

        for (int i = 0; i < videoStreams; i++)
        {
            // The action signal returns a transfer-full GstTagList, which is a
            // mini object, so what comes back is a wrapper that owns that
            // reference: disposing it is the gst_tag_list_unref of the C.
            using Gst.TagList? tags = _playbin.EmitSignal<Gst.TagList>("get-video-tags", i);

            if (tags is null)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $"video stream {i}:\n");
            _ = tags.GetString("video-codec", out string? codec);
            text.Append(CultureInfo.InvariantCulture, $"  codec: {codec ?? "unknown"}\n");
        }

        for (int i = 0; i < audioStreams; i++)
        {
            using Gst.TagList? tags = _playbin.EmitSignal<Gst.TagList>("get-audio-tags", i);

            if (tags is null)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $"\naudio stream {i}:\n");

            if (tags.GetString("audio-codec", out string? codec))
            {
                text.Append(CultureInfo.InvariantCulture, $"  codec: {codec}\n");
            }

            if (tags.GetString("language-code", out string? language))
            {
                text.Append(CultureInfo.InvariantCulture, $"  language: {language}\n");
            }

            if (tags.GetUint("bitrate", out uint bitrate))
            {
                text.Append(CultureInfo.InvariantCulture, $"  bitrate: {bitrate}\n");
            }
        }

        for (int i = 0; i < textStreams; i++)
        {
            using Gst.TagList? tags = _playbin.EmitSignal<Gst.TagList>("get-text-tags", i);

            if (tags is null)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $"\nsubtitle stream {i}:\n");

            text.Append(tags.GetString("language-code", out string? language)
                ? $"  language: {language}\n"
                : "  language: unknown\n");
        }

        _streams.Text = text.ToString();
    }

    /// <inheritdoc/>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // A paused sink has no new frame coming to repaint the window with, so
        // the one it last rendered has to be drawn again at the new size. This
        // is what the draw callback of an overlay-based player is for, and it
        // is only needed while nothing is playing.
        if (_state == State.Paused)
        {
            _playbin.As<IVideoOverlay>()?.Expose();
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }

    /// <summary>
    /// Stops the pipeline and gives everything it holds back.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();

        // The handler captured this window, and a handler that captured
        // something the bus keeps alive is a cycle the collector cannot break.
        // Clearing it is what ends that, and it is done before the pipeline
        // moves so that nothing is posted into a handler that is going away.
        _bus.ClearSyncHandler();

        _playbin.SetState(State.Null);
        _playbin.Dispose();

        GC.SuppressFinalize(this);
    }
}
