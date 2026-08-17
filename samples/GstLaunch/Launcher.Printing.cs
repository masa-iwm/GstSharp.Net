using System.Globalization;
using System.Text;
using Gst;
using Gst.GLib;
using Gst.GObject;

/// <content>
/// Everything the tool writes to the console: the quiet gate, the printers of
/// the message payloads, and the time format of GStreamer.
/// </content>
/// <remarks>
/// The C tool prints through three different calls and the difference matters:
/// <c>PRINT</c> is silenced by <c>-q</c>, <c>gst_printerr</c> goes to standard
/// error and is never silenced, and a plain <c>gst_print</c> — which the
/// position line and the property notifications use — is not silenced either.
/// The three methods below keep that apart.
/// </remarks>
internal sealed partial class Launcher
{
    /// <summary>The widest indent a table of contents is printed with.</summary>
    private const int MaxIndent = 40;

    /// <summary>The number of characters a position field is cut to.</summary>
    private const int PositionFieldWidth = 9;

    /// <summary>Writes a line unless <c>-q</c> was given.</summary>
    /// <param name="text">The line to write.</param>
    private void Print(string text)
    {
        if (!_options.Quiet)
        {
            Console.Out.WriteLine(text);
        }
    }

    /// <summary>
    /// Writes text without ending the line, unless <c>-q</c> was given.
    /// </summary>
    /// <param name="text">The text to write.</param>
    private void PrintPartial(string text)
    {
        if (!_options.Quiet)
        {
            Console.Out.Write(text);
        }
    }

    /// <summary>Writes a line that <c>-q</c> does not silence.</summary>
    /// <param name="text">The line to write.</param>
    private static void PrintAlways(string text) => Console.Out.WriteLine(text);

    /// <summary>
    /// Writes text that <c>-q</c> does not silence, without ending the line.
    /// </summary>
    /// <param name="text">The text to write.</param>
    private static void PrintAlwaysPartial(string text) => Console.Out.Write(text);

    /// <summary>
    /// Prints the version of the tool and of the library it loaded.
    /// </summary>
    /// <remarks>
    /// The C tool prints a third line, the origin of the package, which is a
    /// constant of its build and has no run time equivalent here.
    /// </remarks>
    private static void PrintVersion()
    {
        Gst.Version version = GstSharp.NativeVersion;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Options.ProgramName} version {version.Major}.{version.Minor}.{version.Micro}"));
        Console.WriteLine(version.Description);
    }

    /// <summary>
    /// Prints one message the way <c>-m</c> does: who sent it, what it is, and
    /// what it carries.
    /// </summary>
    /// <param name="message">The message to print.</param>
    private void PrintMessageDump(Message message)
    {
        uint seqnum = message.Seqnum;
        string typeName = MessageTypeExtensions.GetName(message.Type);

        PrintPartial(message.Src switch
        {
            Element element => string.Create(
                CultureInfo.InvariantCulture,
                $"Got message #{seqnum} from element \"{element.Name}\" ({typeName}): "),
            Pad pad => string.Create(
                CultureInfo.InvariantCulture,
                $"Got message #{seqnum} from pad \"{PadName(pad)}\" ({typeName}): "),
            Gst.Object other => string.Create(
                CultureInfo.InvariantCulture,
                $"Got message #{seqnum} from object \"{other.Name}\" ({typeName}): "),
            _ => string.Create(CultureInfo.InvariantCulture, $"Got message #{seqnum} ({typeName}): "),
        });

        using Structure? structure = message.GetStructure();
        Print(structure is null ? "no message details" : structure.ToString());
    }

    /// <summary>
    /// Prints an error message, the element that posted it and whatever it
    /// carried besides.
    /// </summary>
    /// <param name="message">The error message.</param>
    private static void PrintErrorMessage(Message message)
    {
        (GException error, string? debug) = message.ParseError();

        Console.Error.WriteLine(message.Src is Gst.Object source
            ? $"ERROR: from element {source.GetPathString()}: {error.Message}"
            : $"ERROR: {error.Message}");

        if (debug is not null)
        {
            Console.Error.WriteLine("Additional debug info:");
            Console.Error.WriteLine(debug);
        }

        using Structure? details = message.GetDetails();

        if (details is not null)
        {
            Console.Error.WriteLine("Details:");
            Console.Error.WriteLine(details.ToString());
        }
    }

    /// <summary>
    /// Prints the tags an element found.
    /// </summary>
    /// <param name="message">The tag message.</param>
    private void HandleTag(Message message)
    {
        Print(message.Src switch
        {
            Element element => $"FOUND TAG      : found by element \"{element.Name}\".",
            Pad pad => $"FOUND TAG      : found by pad \"{PadName(pad)}\".",
            Gst.Object other => $"FOUND TAG      : found by object \"{other.Name}\".",
            _ => "FOUND TAG",
        });

        message.ParseTag(out TagList? tags);

        if (tags is null)
        {
            return;
        }

        using (tags)
        {
            tags.Foreach((list, tag) =>
            {
                if (list is not null && tag is not null)
                {
                    PrintTag(list, tag);
                }
            });
        }
    }

    /// <summary>
    /// Prints every value stored under one tag.
    /// </summary>
    /// <param name="list">The tag list to read.</param>
    /// <param name="tag">The tag to print.</param>
    private void PrintTag(TagList list, string tag)
    {
        GType type = Global.TagGetType(tag);
        string typeName = type.Name;
        uint count = list.GetTagSize(tag);

        for (uint index = 0; index < count; index++)
        {
            string? text;

            if (type == GType.String)
            {
                list.GetStringIndex(tag, index, out text);
            }
            else if (typeName == "GstSample")
            {
                text = DescribeSample(list, tag, index);
            }
            else if (typeName == "GstDateTime")
            {
                text = DescribeDateTime(list, tag, index);
            }
            else
            {
                // The generic case. The C tool uses g_strdup_value_contents,
                // the debug printer of GLib, which has no binding; this is the
                // serialisation a pipeline description would carry, which
                // differs in its quoting.
                using Value value = list.GetValueIndex(tag, index);
                text = value.IsEmpty ? null : Global.ValueSerialize(value);
            }

            if (text is not null)
            {
                Print($"{(index == 0 ? Global.TagGetNick(tag) : string.Empty),16}: {text}");
            }
        }
    }

    /// <summary>
    /// Describes a tag that holds a sample, which is how cover art travels.
    /// </summary>
    /// <param name="list">The tag list to read.</param>
    /// <param name="tag">The tag to read.</param>
    /// <param name="index">Which of its values to read.</param>
    /// <returns>The description, or <see langword="null"/>.</returns>
    private static string? DescribeSample(TagList list, string tag, uint index)
    {
        if (!list.GetSampleIndex(tag, index, out Sample? sample) || sample is null)
        {
            return null;
        }

        using (sample)
        {
            using Gst.Buffer? image = sample.GetBuffer();

            if (image is null)
            {
                return "NULL buffer";
            }

            using Caps? caps = sample.GetCaps();

            return caps is null
                ? string.Create(CultureInfo.InvariantCulture, $"buffer of {image.GetSize()} bytes")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"buffer of {image.GetSize()} bytes, type: {caps}");
        }
    }

    /// <summary>
    /// Describes a tag that holds a date and time.
    /// </summary>
    /// <param name="list">The tag list to read.</param>
    /// <param name="tag">The tag to read.</param>
    /// <param name="index">Which of its values to read.</param>
    /// <returns>The description, or <see langword="null"/>.</returns>
    private static string? DescribeDateTime(TagList list, string tag, uint index)
    {
        if (!list.GetDateTimeIndex(tag, index, out Gst.DateTime? dateTime) || dateTime is null)
        {
            return null;
        }

        using (dateTime)
        {
            if (!dateTime.HasTime())
            {
                return dateTime.ToIso8601String();
            }

            float offset = dateTime.GetTimeZoneOffset();
            string zone = offset != 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"(UTC {(offset > 0 ? "+" : string.Empty)}{offset}h)")
                : "(UTC)";

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{dateTime.GetYear():0000}-{dateTime.GetMonth():00}-{dateTime.GetDay():00} " +
                $"{dateTime.GetHour():00}:{dateTime.GetMinute():00}:{dateTime.GetSecond():00} {zone}");
        }
    }

    /// <summary>
    /// Prints the table of contents an element found.
    /// </summary>
    /// <param name="message">The message that carries it.</param>
    private void HandleToc(Message message)
    {
        Print(message.Src switch
        {
            Element element => $"FOUND TOC      : found by element \"{element.Name}\".",
            Gst.Object other => $"FOUND TOC      : found by object \"{other.Name}\".",
            _ => "FOUND TOC",
        });

        message.ParseToc(out Toc? toc, out bool _);

        if (toc is null)
        {
            return;
        }

        using (toc)
        {
            // The entries are materialized copies that this code owns, which is
            // what the GList marshalling of the binding hands out.
            foreach (TocEntry entry in toc.GetEntries())
            {
                using (entry)
                {
                    PrintTocEntry(entry, 0);
                }
            }
        }
    }

    /// <summary>
    /// Prints one entry of a table of contents and everything under it.
    /// </summary>
    /// <param name="entry">The entry to print.</param>
    /// <param name="indent">How far the entry is indented.</param>
    private void PrintTocEntry(TocEntry entry, int indent)
    {
        int width = Math.Min(indent, MaxIndent);

        entry.GetStartStopTimes(out long start, out long stop);

        StringBuilder line = new();
        line.Append(' ', width)
            .Append(TocEntryTypeExtensions.GetNick(entry.GetEntryType()))
            .Append(':');

        if (IsValidTime(start))
        {
            line.Append(" start: ").Append(FormatClockTime(start));
        }

        if (IsValidTime(stop))
        {
            line.Append(" stop: ").Append(FormatClockTime(stop));
        }

        Print(line.ToString());

        int inner = width + 2;

        using (TagList? tags = entry.GetTags())
        {
            tags?.Foreach((list, tag) =>
            {
                if (list is not null && tag is not null)
                {
                    PrintTocTag(list, tag, inner);
                }
            });
        }

        foreach (TocEntry subEntry in entry.GetSubEntries())
        {
            using (subEntry)
            {
                PrintTocEntry(subEntry, inner);
            }
        }
    }

    /// <summary>
    /// Prints one tag of a table of contents entry.
    /// </summary>
    /// <param name="list">The tags of the entry.</param>
    /// <param name="tag">The tag to print.</param>
    /// <param name="depth">How deep the entry sits.</param>
    /// <remarks>
    /// The C tool reads the tag with <c>gst_tag_list_copy_value</c>, which
    /// merges the values of a tag that carries several rather than taking the
    /// first, and that is what <see cref="TagList.CopyValue"/> is. It also
    /// prints through <c>gst_print</c> rather than <c>PRINT</c>, so <c>-q</c>
    /// does not silence it — which is reproduced here.
    /// </remarks>
    private static void PrintTocTag(TagList list, string tag, int depth)
    {
        using Value value = list.CopyValue(tag);

        if (value.IsEmpty)
        {
            return;
        }

        string? text = value.Type == GType.String
            ? value.GetString()
            : Global.ValueSerialize(value);

        PrintAlways($"{new string(' ', 2 * depth)}{Global.TagGetNick(tag)}: {text}");
    }

    /// <summary>
    /// Prints one property change that a verbose run watches.
    /// </summary>
    /// <param name="message">The property notification.</param>
    private void HandlePropertyNotify(Message message)
    {
        if (_options.Quiet)
        {
            return;
        }

        (Gst.Object? source, string name, Value value) = message.ParsePropertyNotify();

        using (value)
        {
            // Let's not print anything for excluded properties.
            if (_options.Exclude.Contains(name, StringComparer.Ordinal))
            {
                return;
            }

            string text = value.IsEmpty ? "(no value)" : DescribeValue(value);

            PrintAlways($"{source?.GetPathString() ?? "(NULL)"}: {name} = {text}");
        }
    }

    /// <summary>
    /// Writes a property value the way the C tool does, as far as the binding
    /// reaches.
    /// </summary>
    /// <param name="value">The value to describe.</param>
    /// <returns>The description.</returns>
    /// <remarks>
    /// The C tool special cases a string, a caps, a tag list and a structure so
    /// that they print as themselves rather than as an escaped one line string,
    /// and falls back to <c>gst_value_serialize</c> for everything else. All
    /// four cases are reachable here: a structure is a boxed value and
    /// <see cref="Value.GetBoxed{T}"/> builds its wrapper, a caps and a tag
    /// list are mini objects and <see cref="Value.GetMiniObject{T}"/> builds
    /// theirs. <c>ToString</c> on each of those wrappers is the
    /// <c>gst_*_to_string</c> the C tool calls, so the <c>caps</c> lines of a
    /// verbose run match it character for character.
    /// </remarks>
    private static string DescribeValue(in Value value)
    {
        if (value.Type == GType.String)
        {
            return value.GetString() ?? string.Empty;
        }

        switch (value.Type.Name)
        {
            case "GstCaps":
            {
                using Caps? caps = value.GetMiniObject<Caps>();

                if (caps is not null)
                {
                    return caps.ToString();
                }

                break;
            }

            case "GstTagList":
            {
                using TagList? tags = value.GetMiniObject<TagList>();

                if (tags is not null)
                {
                    return tags.ToString();
                }

                break;
            }

            case "GstStructure":
            {
                using Structure? structure = value.GetBoxed<Structure>();

                if (structure is not null)
                {
                    return structure.ToString();
                }

                break;
            }

            default:
                break;
        }

        return Global.ValueSerialize(value) ?? "(no value)";
    }

    /// <summary>
    /// Prints where the pipeline stands, once per tick.
    /// </summary>
    /// <param name="outputIsTty">
    /// Whether the line is overwritten in place rather than scrolled.
    /// </param>
    private void PrintPosition(bool outputIsTty)
    {
        if (_buffering)
        {
            return;
        }

        // The query writes nothing when it fails, and the C tool starts from
        // -1, so the answer of a pipeline that cannot say has to be built here.
        long position = _pipeline.QueryPosition(Format.Time, out long queried) ? queried : -1;
        long duration = _pipeline.QueryDuration(Format.Time, out long queriedDuration) ? queriedDuration : -1;

        if (position < 0)
        {
            return;
        }

        string positionText = Truncate(FormatClockTime(position));
        string durationText = Truncate(FormatClockTime(duration));

        string line = duration > 0 && duration >= position
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{positionText} / {durationText} ({100.0 * position / duration:F1} %)")
            : $"{positionText} / {durationText}";

        if (outputIsTty)
        {
            PrintAlwaysPartial($"{line}\r");
        }
        else
        {
            PrintAlways(line);
        }
    }

    /// <summary>
    /// Prints how long the pipeline played, when it ever started.
    /// </summary>
    private void PrintExecutionTime()
    {
        if (_startedAt.IsNone)
        {
            return;
        }

        ulong elapsed = Global.UtilGetTimestamp().Nanoseconds - _startedAt.Nanoseconds;

        Print($"Execution ended after {FormatClockTime(unchecked((long)elapsed))}");
    }

    /// <summary>
    /// Names a pad the way the debug output of GStreamer does.
    /// </summary>
    /// <param name="pad">The pad to name.</param>
    /// <returns>The name of its parent and its own, separated by a colon.</returns>
    private static string PadName(Pad pad)
    {
        // The parent is an interned GObject wrapper and is not disposed here.
        Gst.Object? parent = pad.GetParent();

        return $"{parent?.Name ?? "''"}:{pad.Name ?? "''"}";
    }

    /// <summary>
    /// Names the element a message came from, as a path from the pipeline.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The path, or a placeholder when the message has no source.</returns>
    private static string PathOf(Message message) => message.Src?.GetPathString() ?? "(NULL)";

    /// <summary>
    /// Tells whether a time read out of GStreamer is a time at all.
    /// </summary>
    /// <param name="value">The time to test.</param>
    /// <returns><see langword="true"/> when it is set.</returns>
    private static bool IsValidTime(long value) => unchecked((ulong)value) != ClockTime.NoneValue;

    /// <summary>
    /// Formats a time the way <c>GST_TIME_FORMAT</c> does.
    /// </summary>
    /// <param name="value">The time in nanoseconds.</param>
    /// <returns>The formatted time.</returns>
    /// <remarks>
    /// <see cref="ClockTime.ToString"/> stops at milliseconds and pads the
    /// hours, and every line of this tool is compared against the output of the
    /// C one, so the format of GStreamer is written out here: unpadded hours and
    /// nine digits of nanoseconds, and the nines that stand for a time that is
    /// not set.
    /// </remarks>
    private static string FormatClockTime(long value)
    {
        ulong time = unchecked((ulong)value);

        if (time == ClockTime.NoneValue)
        {
            return "99:99:99.999999999";
        }

        ulong hours = time / (ClockTime.NanosecondsPerSecond * 3600);
        ulong minutes = time / (ClockTime.NanosecondsPerSecond * 60) % 60;
        ulong seconds = time / ClockTime.NanosecondsPerSecond % 60;
        ulong nanoseconds = time % ClockTime.NanosecondsPerSecond;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{minutes:00}:{seconds:00}.{nanoseconds:000000000}");
    }

    /// <summary>
    /// Cuts a formatted time to the width the position line uses.
    /// </summary>
    /// <param name="text">The formatted time.</param>
    /// <returns>The first nine characters of it.</returns>
    private static string Truncate(string text) =>
        text.Length > PositionFieldWidth ? text[..PositionFieldWidth] : text;
}
