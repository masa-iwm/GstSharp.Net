using System.Globalization;
using System.Text;
using Gst;
using Gst.GObject;

/// <content>
/// The sections of an element page that describe the element rather than its
/// factory: the interfaces it implements, its flags, what it does with a clock,
/// its pads, its signals, its children and its presets.
/// </content>
internal sealed partial class Inspection
{
    /// <summary>
    /// The flags of an element and their labels, in the order
    /// <c>print_element_flags</c> tests them.
    /// </summary>
    private static readonly (uint Bit, string Name)[] ElementFlagNames =
    [
        ((uint)ElementFlags.LockedState, "LOCKED_STATE"),
        ((uint)ElementFlags.Sink, "SINK"),
        ((uint)ElementFlags.Source, "SOURCE"),
        ((uint)ElementFlags.ProvideClock, "PROVIDE_CLOCK"),
        ((uint)ElementFlags.RequireClock, "REQUIRE_CLOCK"),

        // print_element_flags labels GST_ELEMENT_FLAG_INDEXABLE
        // "REQUIRE_INDEXABLE", a copy of the line above it. The mislabel is the
        // C tool's and is reproduced rather than fixed, because this page is
        // diffed against that tool.
        ((uint)ElementFlags.Indexable, "REQUIRE_INDEXABLE"),
    ];

    /// <summary>
    /// Prints the interfaces of the type of the element, the way
    /// <c>print_interfaces</c> does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    /// <remarks>
    /// A type with no interface prints nothing at all, not even the heading.
    /// </remarks>
    private static void PrintInterfaces(Element element)
    {
        GType[] interfaces = element.NativeType.GetInterfaces();

        if (interfaces.Length == 0)
        {
            return;
        }

        Line(0, "Implemented Interfaces:");

        foreach (GType type in interfaces)
        {
            Line(1, type.Name);
        }

        Line(0, string.Empty);
    }

    /// <summary>
    /// Prints the flags of the element, the way <c>print_element_flags</c>
    /// does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    private static void PrintElementFlags(Element element)
    {
        Line(0, "Element Flags:");

        foreach ((uint bit, string name) in ElementFlagNames)
        {
            if (element.IsFlagSet(bit))
            {
                Line(1, $"- {name}");
            }
        }

        Line(0, string.Empty);
    }

    /// <summary>
    /// Prints what the element does with a clock, the way
    /// <c>print_clocking_info</c> does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    private static void PrintClocking(Element element)
    {
        bool requires = element.IsFlagSet((uint)ElementFlags.RequireClock);
        bool provides = element.IsFlagSet((uint)ElementFlags.ProvideClock);

        Line(0, string.Empty);

        if (!requires && !provides)
        {
            Line(0, "Element has no clocking capabilities.");
            return;
        }

        Line(0, "Clocking Interaction:");

        if (requires)
        {
            Line(1, "element requires a clock");
        }

        if (provides)
        {
            Line(1, "element provides a clock");
        }
    }

    /// <summary>
    /// Prints the pads a fresh element has, the way <c>print_pad_info</c> does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    private static void PrintPads(Element element)
    {
        Line(0, string.Empty);
        Line(0, "Pads:");

        using Iterator iterator = element.IteratePads();
        IReadOnlyList<Pad> pads = iterator.Items<Pad>();

        if (pads.Count == 0)
        {
            Line(1, "none");
            return;
        }

        foreach (Pad pad in pads)
        {
            try
            {
                string direction = pad.GetDirection() switch
                {
                    PadDirection.Src => "SRC",
                    PadDirection.Sink => "SINK",
                    _ => "UNKNOWN",
                };

                Line(1, $"{direction}: '{pad.GetName()}'");

                using PadTemplate? template = pad.GetPadTemplate();

                if (template is not null)
                {
                    Line(2, $"Pad Template: '{NameTemplateOf(template)}'");
                }
            }
            finally
            {
                pad.Dispose();
            }
        }
    }

    /// <summary>
    /// Prints the signals and the action signals of the element, the way
    /// <c>print_signal_info</c> does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    /// <remarks>
    /// <para>
    /// The C tool makes two passes, one for the signals that are not actions
    /// and one for the ones that are. An element that has a sometimes pad
    /// template gets the three <c>GstElement</c> signals about pads listed
    /// first, before its own; the walk up the type chain otherwise stops at
    /// <c>GstElement</c>, and skips the signals <c>GstBin</c> declares unless
    /// the element is a plain bin.
    /// </para>
    /// <para>
    /// The signature underneath a signal is C, so the type of every parameter
    /// is spelled the way <c>pretty_type_name</c> spells it, and the
    /// continuation lines are aligned on a width computed from the name of the
    /// signal and the name of the return type.
    /// </para>
    /// </remarks>
    private static void PrintSignals(Element element)
    {
        GType elementType = GType.FromName("GstElement");
        GType objectType = GType.FromName("GstObject");
        GType binType = GType.FromName("GstBin");
        GType instanceType = element.NativeType;

        for (int pass = 0; pass < 2; pass++)
        {
            bool wantActions = pass == 1;
            List<SignalQuery> found = [];

            if (!wantActions && HasSometimesTemplate(element))
            {
                string[] padSignals = ["pad-added", "pad-removed", "no-more-pads"];

                foreach (string name in padSignals)
                {
                    if (SignalQuery.TryLookup(elementType, name, out SignalQuery query))
                    {
                        found.Add(query);
                    }
                }
            }

            GType owner = instanceType;

            for (; owner.IsValid; owner = owner.Parent)
            {
                if (owner == elementType || owner == objectType)
                {
                    break;
                }

                if (owner == binType && instanceType != binType)
                {
                    continue;
                }

                foreach (SignalQuery query in SignalQuery.List(owner))
                {
                    if (query.IsAction == wantActions)
                    {
                        found.Add(query);
                    }
                }
            }

            if (found.Count == 0)
            {
                continue;
            }

            Line(0, string.Empty);
            Line(0, wantActions ? "Element Actions:" : "Element Signals:");
            Line(0, string.Empty);

            StringBuilder text = new();

            foreach (SignalQuery query in found)
            {
                AppendSignal(text, query, owner, wantActions);
            }

            Write(text);
        }
    }

    /// <summary>Appends the signature of one signal to a block.</summary>
    /// <param name="text">The block being built.</param>
    /// <param name="query">The signal to describe.</param>
    /// <param name="objectType">
    /// The type the walk up the chain stopped at, which is what the C tool
    /// names the first parameter of a handler after: the loop variable it
    /// reads is the one the <c>break</c> left behind, so every handler is
    /// declared as taking a <c>GstElement</c>.
    /// </param>
    /// <param name="wantActions">Whether this is the pass over action signals.</param>
    private static void AppendSignal(StringBuilder text, SignalQuery query, GType objectType, bool wantActions)
    {
        string returnName = PrettyTypeName(query.ReturnType, out string returnMark);

        int width = query.Name.Length + returnName.Length + returnMark.Length - 1 + (wantActions ? 36 : 24);
        string indent = new(' ', width);

        text.Append(wantActions
            ? $"  \"{query.Name}\" -> {returnName} {returnMark}:  g_signal_emit_by_name (element, \"{query.Name}\""
            : $"  \"{query.Name}\" :  {returnName}{returnMark}user_function ({objectType.Name} * object");

        GType[] parameters = query.GetParameterTypes();

        for (int i = 0; i < parameters.Length; i++)
        {
            string typeName = PrettyTypeName(parameters[i], out string mark);

            // A string and a string array are the only parameters the C tool
            // declares const.
            string constant = string.Equals(typeName, "gchar", StringComparison.Ordinal)
                && mark.Contains('*', StringComparison.Ordinal)
                    ? "const "
                    : string.Empty;

            text.Append(",\n").Append(indent).Append(string.Create(
                CultureInfo.InvariantCulture,
                $"{constant}{typeName}{mark}arg{i}"));
        }

        if (!wantActions)
        {
            text.Append(",\n").Append(indent).Append("gpointer user_data);\n");
        }
        else if (query.ReturnType == GType.None)
        {
            text.Append(");\n");
        }
        else
        {
            text.Append(",\n").Append(indent)
                .Append($"{query.ReturnType.Name} *{returnMark}p_return_value);\n");
        }

        text.Append('\n');
    }

    /// <summary>
    /// Names a type the way <c>pretty_type_name</c> does, and says what pointer
    /// marker follows it.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <param name="mark">The marker, which carries its own spaces.</param>
    /// <returns>The name of the type in C.</returns>
    private static string PrettyTypeName(GType type, out string mark)
    {
        if (type == GType.String)
        {
            mark = " * ";
            return "gchar";
        }

        if (string.Equals(type.Name, "GStrv", StringComparison.Ordinal))
        {
            mark = " ** ";
            return "gchar";
        }

        mark = NeedsPointerMarker(type) ? " * " : " ";
        return type.Name;
    }

    /// <summary>
    /// Tells whether a type is passed by pointer, the way
    /// <c>gtype_needs_ptr_marker</c> does.
    /// </summary>
    /// <param name="type">The type to test.</param>
    /// <returns><see langword="true"/> when the parameter is a pointer.</returns>
    private static bool NeedsPointerMarker(GType type)
    {
        if (type == GType.Pointer)
        {
            return false;
        }

        GType fundamental = type.Fundamental;

        return fundamental == GType.Pointer || fundamental == GType.Boxed || fundamental == GType.Object;
    }

    /// <summary>
    /// Tells whether the element has a pad template whose pads come and go,
    /// which is what <c>has_sometimes_template</c> asks.
    /// </summary>
    /// <param name="element">The element to test.</param>
    /// <returns><see langword="true"/> when one of the templates is sometimes.</returns>
    private static bool HasSometimesTemplate(Element element)
    {
        foreach (PadTemplate template in element.GetPadTemplateList())
        {
            if (PresenceOf(template) == PadPresence.Sometimes)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Prints the children of a bin, the way <c>print_children_info</c> does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    /// <remarks>
    /// The C tool walks <c>GST_BIN (element)-&gt;children</c> directly;
    /// <c>gst_bin_iterate_elements</c> walks that same list, in that same
    /// order, and is what the binding offers.
    /// </remarks>
    private static void PrintChildren(Element element)
    {
        if (element is not Bin bin)
        {
            return;
        }

        using Iterator iterator = bin.IterateElements();
        IReadOnlyList<Element> children = iterator.Items<Element>();

        if (children.Count == 0)
        {
            return;
        }

        Line(0, string.Empty);
        Line(0, "Children:");

        foreach (Element child in children)
        {
            try
            {
                Line(1, child.GetName() ?? string.Empty);
            }
            finally
            {
                child.Dispose();
            }
        }
    }

    /// <summary>
    /// Prints the presets of the element, the way <c>print_preset_list</c>
    /// does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    private static void PrintPresets(Element element)
    {
        if (element.As<IPreset>() is not { } preset)
        {
            return;
        }

        string[]? names = preset.GetPresetNames();

        if (names is not { Length: > 0 })
        {
            return;
        }

        Line(0, string.Empty);
        Line(0, "Presets:");

        foreach (string name in names)
        {
            string comment = preset.GetMeta(name, "comment", out string? value) && value is not null
                ? $": {value}"
                : string.Empty;

            Line(1, $"\"{name}\"{comment}");
        }
    }
}
