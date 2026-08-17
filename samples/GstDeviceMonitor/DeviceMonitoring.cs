using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Gst;

/// <summary>
/// The run of <c>gst-device-monitor-1.0</c>: a <see cref="DeviceMonitor"/>, the
/// filters the command line asked for, and the bus conversation that is the
/// whole of the tool.
/// </summary>
internal sealed partial class DeviceMonitoring
{
    /// <summary>The listing finished, whether or not it found anything.</summary>
    private const int ExitNoError = 0;

    /// <summary>The command line could not be read.</summary>
    private const int ExitError = 1;

    /// <summary>The monitor refused to start, which is <c>-1</c> in the C tool.</summary>
    private const int ExitStartFailure = -1;

    /// <summary>How long one poll of the bus waits.</summary>
    private static readonly ClockTime PollSlice = ClockTime.FromMilliseconds(100);

    private readonly Options _options;
    private bool _quit;

    /// <summary>Initialises a run.</summary>
    /// <param name="options">The command line of the process.</param>
    private DeviceMonitoring(Options options) => _options = options;

    /// <summary>
    /// Reads the command line, initialises GStreamer and lists the devices.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>
    /// The exit code of the C tool: 0 for a listing that ran — including one
    /// that found no devices at all — 1 for a command line that could not be
    /// read, and -1 for a monitor that refused to start.
    /// </returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The tool turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        Options options;

        try
        {
            options = Options.Parse(arguments);
        }
        catch (OptionException error)
        {
            // The wording of the option parser of GLib, which is what the C
            // tool prints here.
            Console.WriteLine($"Error initializing: {error.Message}");
            return ExitError;
        }

        if (options.Help)
        {
            Console.WriteLine(Options.Usage);
            return ExitNoError;
        }

        try
        {
            // The --gst-* arguments travel with the options and reach
            // gst_init_check from here.
            GstSharp.Initialize(options.Native);
        }
        catch (Exception error)
        {
            Console.WriteLine($"Error initializing: {error.Message}");
            return ExitError;
        }

        if (options.Version)
        {
            PrintVersion();
            return ExitNoError;
        }

        try
        {
            return new DeviceMonitoring(options).Execute();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{Options.ProgramName}: {error}");
            return ExitError;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Builds the monitor, starts it and reads its bus until the listing is
    /// over.
    /// </summary>
    /// <returns>The exit code.</returns>
    private int Execute()
    {
        // The monitor is created here and finished with here, which is the one
        // sanctioned Dispose of a GObject wrapper. See docs/ownership.md.
        using DeviceMonitor monitor = DeviceMonitor.New();

        monitor.SetShowAllDevices(_options.IncludeHidden);

        // The bus wrapper is interned and shared with every other lookup of the
        // same bus, so it is not disposed here.
        Bus bus = monitor.GetBus();

        // The remaining arguments, in the form DEVICE_CLASSES or
        // DEVICE_CLASSES:FILTER_CAPS.
        foreach (DeviceFilter filter in _options.Filters)
        {
            AddFilter(monitor, filter);
        }

        Console.WriteLine("Probing devices...");
        Console.WriteLine();

        if (!monitor.Start())
        {
            Console.Error.WriteLine("Failed to start device monitor!");
            return ExitStartFailure;
        }

        try
        {
            if (_options.Follow)
            {
                Console.WriteLine(
                    "Monitoring devices, waiting for devices to be removed or new devices to be added...");
            }

            RunLoop(bus);
        }
        finally
        {
            monitor.Stop();
        }

        return ExitNoError;
    }

    /// <summary>
    /// Adds one <c>DEVICE_CLASSES[:FILTER_CAPS]</c> filter to the monitor.
    /// </summary>
    /// <param name="monitor">The monitor to narrow.</param>
    /// <param name="filter">The filter that was named on the command line.</param>
    /// <remarks>
    /// Caps that will not parse are a warning and not an error in the C tool,
    /// which then adds the class filter on its own; that is mirrored here. The
    /// wording is the C one, without the process and timestamp decoration GLib
    /// puts around a <c>g_warning</c>.
    /// </remarks>
    private static void AddFilter(DeviceMonitor monitor, DeviceFilter filter)
    {
        using Caps? caps = filter.Caps is null ? null : Caps.FromString(filter.Caps);

        if (filter.Caps is not null && caps is null)
        {
            Console.Error.WriteLine(
                $"{Options.ProgramName}-WARNING **: Couldn't parse device filter caps '{filter.Caps}'");
        }

        _ = monitor.AddFilter(filter.Classes, caps);
    }

    /// <summary>
    /// Polls the bus until the listing is over.
    /// </summary>
    /// <param name="bus">The bus of the monitor.</param>
    /// <remarks>
    /// This is the C tool's <c>g_main_loop_run</c> and its bus watch in one
    /// place. The initial listing arrives as one <c>DEVICE_ADDED</c> message per
    /// device that is already there, followed by
    /// <c>DEVICE_MONITOR_STARTED</c>, which is where a run without
    /// <c>--follow</c> ends.
    /// </remarks>
    private void RunLoop(Bus bus)
    {
        // Not a gst-device-monitor option: this bounds a follow run so that the
        // hotplug path can be exercised without a signal to end it. It is
        // counted from here, and a listing that has not finished by then is cut
        // short, which is the same thing an impatient Ctrl+C would do.
        Stopwatch elapsed = Stopwatch.StartNew();

        while (!_quit)
        {
            using (Message? message = bus.TimedPop(PollSlice))
            {
                // An application with no main loop drains the queue of pending
                // releases itself, and once per poll of the bus is the natural
                // place. See docs/ownership.md.
                GstSharp.DrainPendingReleases();

                if (message is not null)
                {
                    Handle(message);
                }
            }

            if (_options.FollowFor is TimeSpan limit && elapsed.Elapsed >= limit)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Acts on one message, which is the bus handler of the C tool.
    /// </summary>
    /// <param name="message">The message to act on.</param>
    private void Handle(Message message)
    {
        switch (message.Type)
        {
            case MessageType.DeviceAdded:
            {
                // A device is a GObject wrapper, interned and not disposed here.
                message.ParseDeviceAdded(out Device? device);

                if (device is not null)
                {
                    PrintDevice(device, modified: false);
                }

                break;
            }

            case MessageType.DeviceRemoved:
            {
                message.ParseDeviceRemoved(out Device? device);

                if (device is not null)
                {
                    PrintRemoved(device);
                }

                break;
            }

            case MessageType.DeviceChanged:
            {
                message.ParseDeviceChanged(out Device? device, out Device? _);

                if (device is not null)
                {
                    PrintDevice(device, modified: true);
                }

                break;
            }

            case MessageType.DeviceMonitorStarted:
            {
                message.ParseDeviceMonitorStarted(out bool success);

                if (!success)
                {
                    // The C tool leaves the loop here and still ends with 0,
                    // because only a start that failed outright answers -1.
                    Console.Error.WriteLine("Failed to start device monitor!");
                    _quit = true;
                }
                else if (!_options.Follow)
                {
                    _quit = true;
                }

                break;
            }

            default:
                Console.WriteLine($"{MessageTypeExtensions.GetName(message.Type)} message");
                break;
        }
    }
}
