// The sample of the GstTranscoder module: it transcodes one URI into another
// one against an encoding profile, and it does so on the route the module
// documents as the recommended one - RunAsync plus a polled API bus - rather
// than through a signal adapter, so that it needs no main loop and no signal
// handler.
//
// Usage: GstTranscode <src-uri> <dst-uri> [<profile>]
//
// The profile defaults to application/ogg:audio/x-vorbis, which is the
// serialization GstEncodingProfile parses; see the GstEncodingProfile
// documentation for the grammar. Both arguments are URIs rather than paths.
//
// It is headless and needs no network. What it does need at run time are the
// uritranscodebin and transcodebin elements of the transcode plugin of
// gst-plugins-bad, which ships separately from the libgsttranscoder-1.0
// library the module imports from: without them the transcoder is still built
// and GetPipeline answers null, which is the case the sample reports as a
// missing plugin rather than as a transcoding failure.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.Transcoder;

return Transcode.Run(args);

internal static class Transcode
{
    /// <summary>The profile used when the command line names none.</summary>
    private const string DefaultProfile = "application/ogg:audio/x-vorbis";

    /// <summary>How long one poll of the API bus waits.</summary>
    private static readonly ClockTime PollInterval = ClockTime.FromMilliseconds(200);

    /// <summary>
    /// Transcodes one URI into another and reports what the API bus said.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the transcoding finished, 1 on any error.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length is < 2 or > 3)
            {
                Console.Error.WriteLine("Usage: GstTranscode <src-uri> <dst-uri> [<profile>]");
                Console.Error.WriteLine($"       the profile defaults to {DefaultProfile}");
                return 2;
            }

            string source = arguments[0];
            string destination = arguments[1];
            string profile = arguments.Length == 3 ? arguments[2] : DefaultProfile;

            // Initialising through the module rather than through GstSharp is
            // what puts GstTranscoder and GstTranscoderSignalAdapter into the
            // type registry deterministically.
            GstTranscoder.Initialize();

            Console.WriteLine($"source:      {source}");
            Console.WriteLine($"destination: {destination}");
            Console.WriteLine($"profile:     {profile}");

            // The transcoder is built even when the profile cannot be parsed
            // and even when the transcode plugin is missing; both are reported
            // on the API bus instead, which is why nothing here is a guard.
            using Transcoder transcoder = Transcoder.New(source, destination, profile);

            using (Element? pipeline = transcoder.GetPipeline())
            {
                if (pipeline is null)
                {
                    Console.Error.WriteLine(
                        "GstTranscode: uritranscodebin is not installed. Install the transcode "
                        + "plugin of gst-plugins-bad.");
                    return 1;
                }
            }

            return Pump(transcoder);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GstTranscode: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Starts the transcoding and reads the API bus until it is done or fails.
    /// </summary>
    /// <param name="transcoder">The transcoder to run.</param>
    /// <returns>0 when the transcoding finished, 1 on any error.</returns>
    private static int Pump(Transcoder transcoder)
    {
        // The message bus is an interned GObject wrapper of a bus the
        // transcoder owns, so it is not disposed here; see docs/ownership.md.
        Bus bus = transcoder.GetMessageBus();

        Stopwatch elapsed = Stopwatch.StartNew();

        // RunAsync reports its two synchronous failures - no profile, and a
        // state change the pipeline refused - before it returns, so the poll
        // below finds them waiting.
        transcoder.RunAsync();

        while (true)
        {
            using Message? message = bus.TimedPopFiltered(PollInterval, MessageType.Application);

            if (message is null || !Transcoder.IsTranscoderMessage(message))
            {
                continue;
            }

            switch (TranscoderMessageExtensions.ParseType(message))
            {
                case TranscoderMessage.PositionUpdated:
                    TranscoderMessageExtensions.ParsePosition(message, out ClockTime position);
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"position:    {position.TotalSeconds:F2} s"));
                    break;

                case TranscoderMessage.DurationChanged:
                    TranscoderMessageExtensions.ParseDuration(message, out ClockTime duration);
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"duration:    {duration.TotalSeconds:F2} s"));
                    break;

                case TranscoderMessage.StateChanged:
                    TranscoderMessageExtensions.ParseState(message, out TranscoderState state);
                    Console.WriteLine($"state:       {TranscoderStateExtensions.GetName(state)}");
                    break;

                case TranscoderMessage.Done:
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"done:        after {elapsed.Elapsed.TotalSeconds:F2} s"));
                    return 0;

                case TranscoderMessage.Error:
                    ReportError(message);
                    return 1;

                case TranscoderMessage.Warning:
                    ReportWarning(message);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Prints the error of an error message, with its details.</summary>
    /// <param name="message">The message to read.</param>
    private static void ReportError(Message message)
    {
        TranscoderMessageExtensions.ParseError(message, out Gst.GLib.GException error, out Gst.Structure? details);
        using (details)
        {
            Console.Error.WriteLine($"error:       {error.Message}");
            PrintDetails(details);
        }
    }

    /// <summary>Prints the warning of a warning message, with its details.</summary>
    /// <param name="message">The message to read.</param>
    private static void ReportWarning(Message message)
    {
        TranscoderMessageExtensions.ParseWarning(message, out Gst.GLib.GException warning, out Gst.Structure? details);
        using (details)
        {
            Console.Error.WriteLine($"warning:     {warning.Message}");
            PrintDetails(details);
        }
    }

    /// <summary>
    /// Prints the details of an error or a warning when the message carried
    /// any. The four issues the transcoder raises itself carry none.
    /// </summary>
    /// <param name="details">The details, or <see langword="null"/>.</param>
    private static void PrintDetails(Gst.Structure? details)
    {
        if (details is not null)
        {
            Console.Error.WriteLine($"details:     {details}");
        }
    }
}
