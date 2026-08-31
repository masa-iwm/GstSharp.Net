using System.Globalization;
using System.Text;
using Gst;
using Gst.Audio;
using Gst.GObject;
using Gst.Pbutils;

/// <content>
/// Everything the tool writes about one URI: <c>print_info</c> with its result
/// switch, <c>print_properties</c>, the topology walk, the per-stream blocks,
/// the tag printer and the table of contents.
/// </content>
/// <remarks>
/// <para>
/// The C tool builds its lines two ways at once, and the difference is visible
/// in the output rather than only in the source. The headline of a stream, its
/// tags and the table of contents go straight to standard output with
/// <c>g_print</c>; the block underneath a headline — codec, stream id, and the
/// per-medium fields — is accumulated in a <c>GString</c> that is printed after
/// the function that built it returns. Since the tags are appended by the
/// builder, <b>the tags of a stream appear before its codec and stream id</b>.
/// That is reproduced here rather than tidied up: every line below is compared
/// against the output of the real tool.
/// </para>
/// <para>
/// The two indentations are also the C tool's, and they are not the same rule.
/// <c>g_print ("%*s", 2 * depth, " ")</c> pads a single space up to a width, so
/// a depth of zero still prints one space; the <c>GString</c> builder appends
/// two spaces per level and prints nothing at all at depth zero.
/// <see cref="Pad"/> is the first, <see cref="Indent"/> the second.
/// </para>
/// </remarks>
internal sealed partial class Discovery
{
    /// <summary>The deepest indentation the table of contents uses.</summary>
    /// <remarks>
    /// <c>MAX_INDENT</c> of the C tool. It bounds the indentation a deeply
    /// nested table of contents can reach, and it bounds nothing else: the
    /// recursion itself is not limited.
    /// </remarks>
    private const int MaxIndent = 40;

    /// <summary>
    /// Prints everything one <see cref="DiscovererInfo"/> has to say, the way
    /// <c>print_info</c> does.
    /// </summary>
    /// <param name="info">What the discovery returned.</param>
    private void PrintInfo(DiscovererInfo info)
    {
        Console.WriteLine($"Done discovering {info.GetUri()}");

        switch (info.GetResult())
        {
            case DiscovererResult.Ok:
                break;

            case DiscovererResult.UriInvalid:
                Console.WriteLine("URI is not valid");
                break;

            case DiscovererResult.Error:
                // Not reachable: a run that ends here posted an error on its
                // bus, which fills the GError, which the binding raises before
                // the information object is wrapped. Analyze prints these two
                // lines and the message from the exception instead.
                Console.WriteLine("An error was encountered while discovering the file");
                break;

            case DiscovererResult.Timeout:
                Console.WriteLine("Analyzing URI timed out");
                break;

            case DiscovererResult.Busy:
                Console.WriteLine("Discoverer was busy");
                break;

            case DiscovererResult.MissingPlugins:
                // The C tool follows this with one indented line per installer
                // detail, out of
                // gst_discoverer_info_get_missing_elements_installer_details.
                // That call is bound, but those lines are not ported, so the
                // headline is all this prints. An unhandled URI scheme, the
                // usual way to reach this result, does not get here at all: it
                // posts an error on the bus and arrives as an exception
                // instead. See the remarks on Analyze.
                Console.WriteLine("Missing plugins");
                break;

            default:
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unknown discoverer result {info.GetResult()}"));
                break;
        }

        // The stream info is an interned GObject wrapper and is not disposed
        // here; the C unref stands for the reference the wrapper holds for the
        // whole process. See docs/ownership.md.
        DiscovererStreamInfo? stream = info.GetStreamInfo();

        if (stream is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Properties:");
            PrintProperties(info, 1);
            PrintTopology(stream, 1);
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Prints the properties of the whole URI, the way <c>print_properties</c>
    /// does.
    /// </summary>
    /// <param name="info">What the discovery returned.</param>
    /// <param name="tab">The indentation the C tool passes in, which is 1.</param>
    private void PrintProperties(DiscovererInfo info, int tab)
    {
        Console.WriteLine(
            $"{Pad(tab + 1)}Duration: {FormatClockTime(unchecked((long)info.GetDuration().Nanoseconds))}");
        Console.WriteLine($"{Pad(tab + 1)}Seekable: {(info.GetSeekable() ? "yes" : "no")}");
        Console.WriteLine($"{Pad(tab + 1)}Live: {(info.GetLive() ? "yes" : "no")}");

        if (_options.Verbose)
        {
            // gst_discoverer_info_get_tags is deprecated in favour of the
            // per-stream and per-container calls, and the C tool prints it
            // anyway: it is the merged list of everything the file said, which
            // no other call answers. Printing what the tool prints is the point
            // of the port, so the deprecation is acknowledged rather than
            // worked around.
#pragma warning disable CS0618
            using TagList? tags = info.GetTags();
#pragma warning restore CS0618

            if (tags is not null)
            {
                // The trailing space is the C tool's: this heading is written
                // as "Tags: " where the per-stream one is written as "Tags:".
                Console.WriteLine($"{Pad(tab + 1)}Tags: ");
                PrintTags(tags, tab + 2);
            }
        }

        if (_options.ShowToc)
        {
            using Toc? toc = info.GetToc();

            if (toc is not null)
            {
                Console.WriteLine($"{Pad(tab + 1)}TOC: ");

                foreach (TocEntry entry in toc.GetEntries())
                {
                    using (entry)
                    {
                        PrintTocEntry(entry, tab + 5);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Walks the chain of streams, the way <c>print_topology</c> does.
    /// </summary>
    /// <param name="info">The stream to print.</param>
    /// <param name="depth">How deep in the topology it sits.</param>
    /// <remarks>
    /// A stream that has a next one is followed; a container that has none is
    /// the branch point, and its streams are printed one level deeper. The
    /// stream wrappers are interned GObject wrappers and are not disposed here.
    /// </remarks>
    private void PrintTopology(DiscovererStreamInfo info, int depth)
    {
        PrintStreamInfo(info, depth);

        DiscovererStreamInfo? next = info.GetNext();

        if (next is not null)
        {
            PrintTopology(next, depth + 1);
            return;
        }

        if (info is DiscovererContainerInfo container)
        {
            foreach (DiscovererStreamInfo stream in container.GetStreams())
            {
                PrintTopology(stream, depth + 1);
            }
        }
    }

    /// <summary>
    /// Prints the headline of one stream and the block underneath it, the way
    /// <c>print_stream_info</c> does.
    /// </summary>
    /// <param name="info">The stream to print.</param>
    /// <param name="depth">How deep in the topology it sits.</param>
    private void PrintStreamInfo(DiscovererStreamInfo info, int depth)
    {
        string? description = null;

        using (Caps? caps = info.GetCaps())
        {
            if (caps is not null)
            {
                // Fixed caps become the human readable codec name, and anything
                // else is printed as caps. Verbose turns the codec name off:
                // the point of the option is to see the caps themselves.
                description = caps.IsFixed() && !_options.Verbose
                    ? PbutilsGlobal.PbUtilsGetCodecDescription(caps)
                    : CapsToString(caps);
            }
        }

        int number = info.GetStreamNumber();
        string shown = description ?? "(NULL)";

        // A container is not an elementary stream and has no number.
        Console.WriteLine(number < 0
            ? $"{Pad(2 * depth)}{info.GetStreamTypeNick()}: {shown}"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{Pad(2 * depth)}{info.GetStreamTypeNick()} #{number}: {shown}"));

        // The tags of every kind of stream are printed from inside these
        // builders, before the block they built reaches standard output. See
        // the remarks on this file.
        List<string> block = info switch
        {
            DiscovererAudioInfo audio => AudioBlock(audio, depth + 1),
            DiscovererVideoInfo video => VideoBlock(video, depth + 1),
            DiscovererSubtitleInfo subtitle => SubtitleBlock(subtitle, depth + 1),
            DiscovererContainerInfo container => ContainerBlock(container, depth + 1),
            _ => [],
        };

        foreach (string line in block)
        {
            Console.WriteLine(line);
        }
    }

    /// <summary>
    /// Builds the block of an audio stream, the way
    /// <c>gst_stream_audio_information_to_string</c> does.
    /// </summary>
    /// <param name="info">The stream to describe.</param>
    /// <param name="depth">The indentation of the block.</param>
    /// <returns>The lines of the block.</returns>
    private List<string> AudioBlock(DiscovererAudioInfo info, int depth)
    {
        List<string> lines = [];
        AppendStreamInformation(lines, info, depth);

        lines.Add($"{Indent(depth)}Language: {info.GetLanguage() ?? "<unknown>"}");
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{Indent(depth)}Channels: {info.GetChannels()} ({ChannelPositions(info)})"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Sample rate: {info.GetSampleRate()}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Depth: {info.GetDepth()}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Bitrate: {info.GetBitrate()}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Max bitrate: {info.GetMaxBitrate()}"));

        PrintTagsTopology(depth, info);
        return lines;
    }

    /// <summary>
    /// Builds the block of a video stream, the way
    /// <c>gst_stream_video_information_to_string</c> does.
    /// </summary>
    /// <param name="info">The stream to describe.</param>
    /// <param name="depth">The indentation of the block.</param>
    /// <returns>The lines of the block.</returns>
    private List<string> VideoBlock(DiscovererVideoInfo info, int depth)
    {
        List<string> lines = [];
        AppendStreamInformation(lines, info, depth);

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Width: {info.GetWidth()}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Height: {info.GetHeight()}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Depth: {info.GetDepth()}"));
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{Indent(depth)}Frame rate: {info.GetFramerateNum()}/{info.GetFramerateDenom()}"));
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{Indent(depth)}Pixel aspect ratio: {info.GetParNum()}/{info.GetParDenom()}"));
        lines.Add($"{Indent(depth)}Interlaced: {(info.IsInterlaced() ? "true" : "false")}");
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Bitrate: {info.GetBitrate()}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Indent(depth)}Max bitrate: {info.GetMaxBitrate()}"));

        PrintTagsTopology(depth, info);
        return lines;
    }

    /// <summary>
    /// Builds the block of a subtitle stream, the way
    /// <c>gst_stream_subtitle_information_to_string</c> does.
    /// </summary>
    /// <param name="info">The stream to describe.</param>
    /// <param name="depth">The indentation of the block.</param>
    /// <returns>The lines of the block.</returns>
    private List<string> SubtitleBlock(DiscovererSubtitleInfo info, int depth)
    {
        List<string> lines = [];
        AppendStreamInformation(lines, info, depth);

        lines.Add($"{Indent(depth)}Language: {info.GetLanguage() ?? "<unknown>"}");

        PrintTagsTopology(depth, info);
        return lines;
    }

    /// <summary>
    /// Prints the tags of a container, which is all the C tool has to say about
    /// one beyond its headline.
    /// </summary>
    /// <param name="info">The container to describe.</param>
    /// <param name="depth">The indentation of the block.</param>
    /// <returns>No lines: a container has no block of its own.</returns>
    private List<string> ContainerBlock(DiscovererContainerInfo info, int depth)
    {
        using TagList? tags = info.GetTags();
        PrintTagsTopology(depth, tags);
        return [];
    }

    /// <summary>
    /// Appends what every stream has, the way
    /// <c>gst_stream_information_to_string</c> does.
    /// </summary>
    /// <param name="lines">The block being built.</param>
    /// <param name="info">The stream to describe.</param>
    /// <param name="depth">The indentation of the block.</param>
    private void AppendStreamInformation(List<string> lines, DiscovererStreamInfo info, int depth)
    {
        if (_options.Verbose)
        {
            using Caps? caps = info.GetCaps();
            lines.Add($"{Indent(depth)}Codec:");
            lines.Add($"{Indent(depth)}  {(caps is null ? "NULL" : CapsToString(caps))}");

            // gst_discoverer_stream_info_get_misc is deprecated, and the C tool
            // still prints it -- behind its own #ifndef GST_DISABLE_DEPRECATED,
            // which the builds this binding loads do not set. The port keeps
            // the block for the same reason it keeps every other line.
#pragma warning disable CS0618
            using Structure? misc = info.GetMisc();
#pragma warning restore CS0618

            if (misc is not null)
            {
                lines.Add($"{Indent(depth)}Additional info:");
                lines.Add($"{Indent(depth)}  {misc}");
            }
        }

        lines.Add($"{Indent(depth)}Stream ID: {info.GetStreamId()}");
    }

    /// <summary>
    /// Prints the tags of one stream, the way <c>print_tags_topology</c> does.
    /// </summary>
    /// <param name="depth">The indentation of the block the stream is in.</param>
    /// <param name="info">The stream whose tags to print.</param>
    private void PrintTagsTopology(int depth, DiscovererStreamInfo info)
    {
        if (!_options.Verbose)
        {
            // Reading the tags at all is skipped when they would not be
            // printed, which is what the early return of the C function
            // amounts to.
            return;
        }

        using TagList? tags = info.GetTags();
        PrintTagsTopology(depth, tags);
    }

    /// <summary>
    /// Prints a block of tags, the way <c>print_tags_topology</c> does.
    /// </summary>
    /// <param name="depth">The indentation of the block.</param>
    /// <param name="tags">The tags, if there are any.</param>
    private void PrintTagsTopology(int depth, TagList? tags)
    {
        if (!_options.Verbose)
        {
            return;
        }

        Console.WriteLine($"{Pad(2 * depth)}Tags:");

        if (tags is not null)
        {
            PrintTags(tags, depth + 1);
        }
        else
        {
            Console.WriteLine($"{Pad(2 * (depth + 1))}None");
        }

        // A line of nothing but the indentation, which the C tool writes as
        // g_print ("%*s\n", 2 * depth, " ").
        Console.WriteLine(Pad(2 * depth));
    }

    /// <summary>
    /// Prints every tag of a list.
    /// </summary>
    /// <param name="tags">The tags to print.</param>
    /// <param name="depth">The indentation of each line.</param>
    private static void PrintTags(TagList tags, int depth) =>
        tags.Foreach((list, tag) => PrintTag(list, tag, depth));

    /// <summary>
    /// Prints one tag, the way <c>print_tag_foreach</c> does.
    /// </summary>
    /// <param name="tags">The list the tag belongs to.</param>
    /// <param name="tag">The name of the tag.</param>
    /// <param name="depth">The indentation of the line.</param>
    /// <remarks>
    /// The value is the merged one, as <c>gst_tag_list_copy_value</c> returns
    /// it, and there are three shapes: a string is printed as it is, an image
    /// is described rather than printed, and everything else goes through
    /// <c>gst_value_serialize</c>.
    /// </remarks>
    private static void PrintTag(TagList tags, string tag, int depth)
    {
        using Value value = tags.CopyValue(tag);

        if (value.IsEmpty)
        {
            // gst_tag_list_copy_value answered FALSE.
            return;
        }

        string text;

        if (value.Type == GType.String)
        {
            text = value.GetString() ?? string.Empty;
        }
        else if (string.Equals(value.Type.Name, "GstSample", StringComparison.Ordinal))
        {
            text = DescribeSample(value);
        }
        else
        {
            text = Global.ValueSerialize(value) ?? string.Empty;
        }

        Console.WriteLine($"{Pad(2 * depth)}{Global.TagGetNick(tag)}: {text}");
    }

    /// <summary>
    /// Describes the image a tag carries, rather than printing its bytes.
    /// </summary>
    /// <param name="value">The value holding the sample.</param>
    /// <returns>The description the C tool builds.</returns>
    private static string DescribeSample(in Value value)
    {
        using Sample? sample = value.GetMiniObject<Sample>();
        using Gst.Buffer? image = sample?.GetBuffer();

        if (image is null)
        {
            return "NULL buffer";
        }

        using Caps? caps = sample?.GetCaps();

        return caps is null
            ? string.Create(CultureInfo.InvariantCulture, $"buffer of {image.GetSize()} bytes")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"buffer of {image.GetSize()} bytes, type: {CapsToString(caps)}");
    }

    /// <summary>
    /// Prints one entry of the table of contents and everything below it, the
    /// way <c>print_toc_entry</c> does.
    /// </summary>
    /// <param name="entry">The entry to print.</param>
    /// <param name="depth">The indentation of the entry.</param>
    private static void PrintTocEntry(TocEntry entry, int depth)
    {
        _ = entry.GetStartStopTimes(out long start, out long stop);

        // The entry line is the one place where the indentation is not doubled:
        // the C tool passes the depth straight to the width.
        Console.WriteLine(
            $"{Pad(depth)}{TocEntryTypeExtensions.GetNick(entry.GetEntryType())}: "
            + $"start: {FormatClockTime(start)} stop: {FormatClockTime(stop)}");

        int indent = Math.Min(depth, MaxIndent) + 2;

        using (TagList? tags = entry.GetTags())
        {
            if (tags is not null)
            {
                Console.WriteLine($"{Pad(2 * depth)}Tags:");
                PrintTags(tags, indent);
            }
        }

        foreach (TocEntry sub in entry.GetSubEntries())
        {
            using (sub)
            {
                PrintTocEntry(sub, indent);
            }
        }
    }

    /// <summary>
    /// Turns caps into the string the C tool prints.
    /// </summary>
    /// <param name="caps">The caps to print.</param>
    /// <returns>The <c>gst_caps_to_string</c> spelling.</returns>
    /// <remarks>
    /// The C <c>caps_to_string</c> has a second half that this does not port:
    /// without <c>--verbose</c> it copies the caps and strips every field whose
    /// value is a <c>GstBuffer</c>, so that a codec_data blob does not fill the
    /// screen. That filter is <c>gst_structure_filter_and_map_in_place_id_str</c>,
    /// which is bound but is not used here, so caps printed without
    /// <c>--verbose</c> keep their buffers. It is reachable in one case only:
    /// caps that are not fixed, because fixed ones are printed as a codec
    /// description instead and
    /// <c>--verbose</c> turns the stripping off in the C tool as well.
    /// </remarks>
    private static string CapsToString(Caps caps) => caps.ToString();

    /// <summary>
    /// Names the speaker positions of an audio stream, the way
    /// <c>format_channel_mask</c> does.
    /// </summary>
    /// <param name="info">The audio stream.</param>
    /// <returns>The positions, or the words the C tool uses instead.</returns>
    /// <remarks>
    /// <para>
    /// <c>gst_audio_channel_positions_from_mask</c> is not bound (see
    /// <c>girs/skip-report.md</c>, GstAudio), and neither is the
    /// <c>GEnumClass</c> walk the C tool takes the nickname from — but neither
    /// is needed. The channel mask is defined bit by bit as
    /// <c>1 &lt;&lt; GstAudioChannelPosition</c>, so the position of the n-th
    /// channel is the n-th set bit counted from zero, and the nickname GObject
    /// registered for an enumeration member is its name in lower case with
    /// dashes between the words. Both halves are checked against the real tool
    /// on a stereo and on a 5.1 file.
    /// </para>
    /// <para>
    /// One case is answered differently on purpose. When the mask names fewer
    /// channels than the stream has, the C function fails and leaves the rest
    /// of its output array uninitialised, which the tool prints anyway; this
    /// prints the positions the mask does name and stops there.
    /// </para>
    /// </remarks>
    private static string ChannelPositions(DiscovererAudioInfo info)
    {
        uint channels = info.GetChannels();

        if (channels == 0)
        {
            return string.Empty;
        }

        ulong mask = info.GetChannelMask();

        if (mask == 0 || channels > 64)
        {
            return "unknown layout";
        }

        List<string> positions = [];

        for (int bit = 0; bit < 64 && positions.Count < channels; bit++)
        {
            if ((mask & (1UL << bit)) != 0)
            {
                positions.Add(NickOf((AudioChannelPosition)bit));
            }
        }

        return string.Join(", ", positions);
    }

    /// <summary>
    /// Spells an audio channel position the way GObject registered it.
    /// </summary>
    /// <param name="position">The position to name.</param>
    /// <returns>The nickname, for example <c>front-left</c>.</returns>
    private static string NickOf(AudioChannelPosition position)
    {
        string name = position.ToString();
        StringBuilder nick = new(name.Length + 4);

        foreach (char c in name)
        {
            if (char.IsUpper(c) && nick.Length > 0)
            {
                nick.Append('-');
            }

            nick.Append(char.ToLowerInvariant(c));
        }

        return nick.ToString();
    }

    /// <summary>
    /// Pads a single space up to a width, which is what <c>%*s</c> with a space
    /// does: a width of zero still prints the space.
    /// </summary>
    /// <param name="width">The width to pad to.</param>
    /// <returns>The padding.</returns>
    private static string Pad(int width) => new(' ', Math.Max(width, 1));

    /// <summary>
    /// Indents by two spaces per level, which is what the <c>GString</c> builder
    /// of the C tool does: a depth of zero indents by nothing.
    /// </summary>
    /// <param name="depth">The number of levels.</param>
    /// <returns>The indentation.</returns>
    private static string Indent(int depth) => new(' ', 2 * Math.Max(depth, 0));
}
