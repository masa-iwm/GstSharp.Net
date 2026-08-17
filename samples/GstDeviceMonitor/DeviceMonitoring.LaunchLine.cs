using System.Text;
using Gst;
using Gst.GObject;

/// <content>
/// The gst-launch snippet of a device: <c>get_launch_line</c>,
/// <c>value_to_string</c> and the three shell quoters behind it.
/// </content>
/// <remarks>
/// <para>
/// This is the part of the C tool that turns a device into something that can
/// be pasted into a shell. It is the only part of either tool that has to know
/// which shell that is, and the decision is made at run time from the
/// environment rather than at compile time from the operating system — a
/// Windows machine runs cmd.exe, PowerShell and a POSIX shell, and the three
/// quote differently.
/// </para>
/// </remarks>
internal sealed partial class DeviceMonitoring
{
    /// <summary>
    /// The properties the C tool never prints: they say something about the
    /// element rather than about the device it opens.
    /// </summary>
    private static readonly string[] IgnoredPropertyNames = ["name", "parent", "direction", "template", "caps"];

    /// <summary>Which dialect of quoting the shell of the moment wants.</summary>
    private enum ShellType
    {
        /// <summary>A POSIX shell, which is what <c>g_shell_quote</c> writes for.</summary>
        Posix,

        /// <summary>The Windows command interpreter.</summary>
        Cmd,

        /// <summary>Windows PowerShell.</summary>
        PowerShell,
    }

    /// <summary>
    /// Builds the element part of the gst-launch snippet: the factory name and
    /// every property whose value differs from a bare element of that factory.
    /// </summary>
    /// <param name="device">The device to build it for.</param>
    /// <returns>
    /// The snippet, or <see langword="null"/> when the device produces no
    /// element or the element has no factory.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>get_launch_line</c> statement by statement. Two elements are
    /// made — one for the device and one bare one from the same factory — and
    /// a property is printed when the two disagree about it, which is what
    /// makes the snippet address one particular device rather than the default
    /// one. The comparison is <c>gst_value_compare</c> rather than
    /// <c>g_param_value_defaults</c>, for the reason the C source gives: a
    /// subclass may already have moved the default.
    /// </para>
    /// <para>
    /// Only a property that is both readable and writable is a candidate, which
    /// is the <c>G_PARAM_READWRITE</c> test of the C tool, and five names are
    /// skipped outright. A property that nothing can serialize is skipped as
    /// well: C warns through the debug log and moves on, and so does this.
    /// </para>
    /// </remarks>
    private static string? LaunchLine(Device device)
    {
        // Both elements are created here and finished with here, which is the
        // one sanctioned Dispose of a GObject wrapper. The factory behind them
        // is interned and is not disposed. See docs/ownership.md.
        using Element? element = device.CreateElement(null);

        if (element?.GetFactory() is not ElementFactory factory || factory.Name is not string name)
        {
            return null;
        }

        StringBuilder line = new(name);

        using Element? bare = factory.Create(null);

        if (bare is null)
        {
            // The C tool dereferences the null it would get here. A sample says
            // what it can and stops: the factory name alone is still a snippet.
            return line.ToString();
        }

        foreach (ParamSpec property in element.ListProperties())
        {
            using (property)
            {
                const ParamFlags ReadWrite = ParamFlags.Readable | ParamFlags.Writable;

                if ((property.Flags & ReadWrite) != ReadWrite)
                {
                    continue;
                }

                string propertyName = property.Name;

                if (Array.IndexOf(IgnoredPropertyNames, propertyName) >= 0)
                {
                    continue;
                }

                using Value value = element.GetProperty(propertyName);
                using Value bareValue = bare.GetProperty(propertyName);

                if (Global.ValueCompare(value, bareValue) == ValueOrder.Equal)
                {
                    continue;
                }

                if (ValueToString(value) is string text)
                {
                    line.Append(' ').Append(propertyName).Append('=').Append(text);
                }
            }
        }

        return line.ToString();
    }

    /// <summary>
    /// Writes one property value the way the launch line carries it, quoted for
    /// the shell where it has to be.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <returns>
    /// The text, or <see langword="null"/> when nothing can write a value of
    /// that type.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>value_to_string</c>. A string is taken as it stands rather
    /// than serialized, so that it is quoted once — for the shell — instead of
    /// twice; everything else goes through <c>gst_value_serialize</c>. The
    /// result is then quoted when it holds anything that is not a plain letter
    /// or digit.
    /// </para>
    /// <para>
    /// A string property holding nothing at all takes the serialized branch, as
    /// it does in C: <c>g_utf8_validate</c> refuses a null pointer, so the C
    /// tool falls through to <c>gst_value_serialize</c> and prints its
    /// <c>NULL</c>. The other half of that condition — a string whose bytes are
    /// not valid UTF-8 — has no counterpart here, because a managed string has
    /// already been decoded and the bytes that would have failed the test are
    /// gone by then.
    /// </para>
    /// </remarks>
    private static string? ValueToString(in Value value)
    {
        string? text = value.Type == GType.String
            ? value.GetString() ?? Global.ValueSerialize(value)
            : Global.ValueSerialize(value);

        if (text is null)
        {
            return null;
        }

        return NeedsQuoting(text) ? ShellQuote(text) : text;
    }

    /// <summary>
    /// Tells whether a value has to be quoted before a shell sees it.
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <returns><see langword="true"/> when it needs quoting.</returns>
    /// <remarks>
    /// The scan starts at the second byte, because the C loop steps before it
    /// looks — <c>while (*++d)</c> — so the first character of a value never
    /// decides anything and a value one character long is never quoted. That is
    /// reproduced rather than corrected: it is what makes <c>loopback=true</c>
    /// come out bare while <c>device='{...}'</c> comes out quoted, and a sample
    /// that quoted differently from the tool it ports would be the wrong kind
    /// of different. The scan is over the UTF-8 bytes, as the C one is, so a
    /// character outside ASCII forces the quotes whichever byte of it is seen.
    /// </remarks>
    private static bool NeedsQuoting(string text)
    {
        ReadOnlySpan<byte> bytes = Encoding.UTF8.GetBytes(text);

        for (int i = 1; i < bytes.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit((char)bytes[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads which shell the tool is being run from.
    /// </summary>
    /// <returns>The dialect to quote for.</returns>
    /// <remarks>
    /// This is <c>get_shell_type</c>, and it is a run time question rather than
    /// a compile time one: <c>PROMPT</c> is set by cmd.exe, <c>PSModulePath</c>
    /// by PowerShell, and neither by a POSIX shell. The order matters — cmd.exe
    /// started from PowerShell inherits <c>PSModulePath</c> and is still
    /// cmd.exe.
    /// </remarks>
    private static ShellType GetShellType()
    {
        if (Environment.GetEnvironmentVariable("PROMPT") is not null)
        {
            return ShellType.Cmd;
        }

        if (Environment.GetEnvironmentVariable("PSModulePath") is not null)
        {
            return ShellType.PowerShell;
        }

        return ShellType.Posix;
    }

    /// <summary>
    /// Quotes a value for the shell the tool was started from.
    /// </summary>
    /// <param name="text">The text to quote.</param>
    /// <returns>The quoted text.</returns>
    private static string ShellQuote(string text) => GetShellType() switch
    {
        ShellType.Cmd => CmdQuote(text),
        ShellType.PowerShell => PowerShellQuote(text),
        _ => PosixQuote(text),
    };

    /// <summary>
    /// Quotes a value for a POSIX shell.
    /// </summary>
    /// <param name="text">The text to quote.</param>
    /// <returns>The quoted text.</returns>
    /// <remarks>
    /// This is <c>g_shell_quote</c>: single quotes around the whole value, and
    /// a literal single quote inside it closed, escaped and reopened. GLib has
    /// no binding for it here — it takes and returns a plain string and the
    /// generator emits nothing from <c>GLib-2.0.gir</c> — and the algorithm is
    /// four lines, so it is written out.
    /// </remarks>
    private static string PosixQuote(string text)
    {
        StringBuilder quoted = new("'");

        foreach (char character in text)
        {
            if (character == '\'')
            {
                quoted.Append("'\\''");
            }
            else
            {
                quoted.Append(character);
            }
        }

        return quoted.Append('\'').ToString();
    }

    /// <summary>
    /// Quotes a value for cmd.exe.
    /// </summary>
    /// <param name="text">The text to quote.</param>
    /// <returns>The quoted text.</returns>
    /// <remarks>
    /// This is <c>cmd_quote</c>, replacement by replacement and in its order:
    /// the percent signs are caret escaped <em>after</em> the enclosing quotes
    /// are in place, which is what makes the escape read as
    /// <c>"^%"</c> — closing the quoted run, escaping the sign, reopening it —
    /// rather than as a caret inside a quoted string, where cmd.exe would take
    /// it literally.
    /// </remarks>
    private static string CmdQuote(string text)
    {
        StringBuilder quoted = new(text);

        quoted.Replace("\"", "\"\"");
        quoted.Replace("\\", "\\\\");
        quoted.Insert(0, '"');
        quoted.Append('"');
        quoted.Replace("%", "\"^%\"");

        return quoted.ToString();
    }

    /// <summary>
    /// Quotes a value for PowerShell.
    /// </summary>
    /// <param name="text">The text to quote.</param>
    /// <returns>The quoted text.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>powershell_quote</c>: single quotes around the value, with
    /// every quote character inside it doubled. PowerShell accepts the two
    /// typographic single quotes as string delimiters as well as the ASCII one,
    /// so all three are doubled — the C source has one
    /// <c>g_string_replace</c> per character, and that is what the three
    /// replacements below are.
    /// </para>
    /// <para>
    /// The typographic pair is <c>U+2018</c> and <c>U+2019</c>. They are
    /// written here as escapes rather than as themselves so that the intent
    /// survives any editor that helpfully straightens a curly quote — which is
    /// also why reading them out of the C source needs care.
    /// </para>
    /// </remarks>
    private static string PowerShellQuote(string text)
    {
        StringBuilder quoted = new(text);

        quoted.Replace("'", "''");
        quoted.Replace("\u2018", "\u2018\u2018");
        quoted.Replace("\u2019", "\u2019\u2019");
        quoted.Replace("\\", "\\\\");
        quoted.Insert(0, '\'');
        quoted.Append('\'');

        return quoted.ToString();
    }
}
