using System.Globalization;
using System.Text;
using Gst;
using Gst.GObject;

/// <content>
/// Everything the tool writes: the census line of one feature, and the sections
/// of an element page that this port covers.
/// </content>
/// <remarks>
/// <para>
/// The C tool writes every line through <c>n_print</c>, which prefixes two
/// spaces per level of a global indentation that the section printers push and
/// pop. <see cref="Line"/> is that function, and the depth is passed rather
/// than kept in a field, because the sections this port covers never nest
/// deeply enough for the difference to be worth a mutable global.
/// </para>
/// <para>
/// Column widths are the C tool's: a property of the factory or the plugin is
/// padded to 25, a property of the element to 20, and a field of a caps
/// structure is right aligned in 15. They are what makes a page of this port
/// comparable line by line with a page of the real tool.
/// </para>
/// </remarks>
internal sealed partial class Inspection
{
    /// <summary>Where the documentation of a GStreamer module lives.</summary>
    private const string DocBaseUrl = "https://gstreamer.freedesktop.org/documentation";

    /// <summary>
    /// The metadata of an element factory, in the order the C tool prints it.
    /// </summary>
    /// <remarks>
    /// <c>gst_element_factory_get_metadata_keys</c> returns a <c>gchar**</c> and
    /// is not bound, so the keys are named rather than enumerated. These are the
    /// ones <c>gst_element_class_set_metadata</c> takes and the ones every
    /// element has; a factory that carries a key of its own has that key left
    /// out here and printed by the C tool.
    /// </remarks>
    private static readonly string[] MetadataKeys =
        ["long-name", "klass", "description", "author", "doc-uri", "icon-name"];

    /// <summary>
    /// The modules whose plugins have generated documentation, which is how the
    /// C tool decides whether it can build a documentation URL.
    /// </summary>
    private static readonly string[] GstreamerModules =
    [
        "gstreamer", "gst-plugins-base", "gst-plugins-good", "gst-plugins-ugly",
        "gst-plugins-bad", "gst-editing-services", "gst-libav", "gst-rtsp-server",
    ];

    /// <summary>The ranks the C tool has a name for.</summary>
    private static readonly (Rank Value, string Name)[] RankNames =
    [
        (Rank.None, "none"), (Rank.Marginal, "marginal"), (Rank.Secondary, "secondary"), (Rank.Primary, "primary"),
    ];

    /// <summary>
    /// Prints the census line of one feature, the way <c>print_element_list</c>
    /// does.
    /// </summary>
    /// <param name="plugin">The plugin the feature belongs to.</param>
    /// <param name="feature">The feature to print.</param>
    /// <remarks>
    /// A feature that is not an element factory is printed with its GObject type
    /// name in parentheses, which is the C tool's third branch. Its second
    /// branch, the one that prints the file extensions of a typefind factory,
    /// needs <c>gst_type_find_factory_get_extensions</c> — a <c>gchar**</c>
    /// return that is not bound — so a typefind factory takes the third branch
    /// here and comes out as <c>(GstTypeFindFactory)</c>.
    /// </remarks>
    private static void PrintFeatureLine(Plugin plugin, PluginFeature feature)
    {
        if (feature is ElementFactory factory)
        {
            Console.WriteLine(
                $"{plugin.GetName()}:  {factory.GetName()}: {factory.GetMetadata("long-name")}");
            return;
        }

        Console.WriteLine($"{plugin.GetName()}:  {feature.GetName()} ({feature.NativeType.Name})");
    }

    /// <summary>
    /// Prints the factory section, the way <c>print_factory_details_info</c>
    /// does.
    /// </summary>
    /// <param name="factory">The factory of the element.</param>
    /// <param name="plugin">The plugin the factory came from, if any.</param>
    private static void PrintFactoryDetails(ElementFactory factory, Plugin? plugin)
    {
        Line(0, "Factory Details:");

        int rank = (int)factory.GetRank();
        Line(1, $"{"Rank",-25}{RankName(rank)} ({rank.ToString(CultureInfo.InvariantCulture)})");

        bool seenDocUri = false;

        foreach (string key in MetadataKeys)
        {
            if (factory.GetMetadata(key) is not { } value)
            {
                continue;
            }

            seenDocUri = seenDocUri || string.Equals(key, "doc-uri", StringComparison.Ordinal);
            Line(1, $"{Capitalize(key),-25}{value}");
        }

        if (!seenDocUri && plugin is not null && !factory.GetSkipDocumentation()
            && HasGeneratedDocumentation(plugin))
        {
            // A plugin with a single feature has no page of its own: the
            // feature's anchor on the plugin page is the whole of it.
            bool single = Registry.Get().GetFeatureListByPlugin(plugin.GetName()).Count == 1;
            string url = single
                ? $"{DocBaseUrl}/{plugin.GetName()}/#{factory.GetName()}-page"
                : $"{DocBaseUrl}/{plugin.GetName()}/{factory.GetName()}.html";

            Line(1, $"{"Documentation",-25}{url}");
        }

        Line(0, string.Empty);
    }

    /// <summary>
    /// Prints the plugin section, the way <c>print_plugin_info</c> does.
    /// </summary>
    /// <param name="plugin">The plugin to describe.</param>
    private static void PrintPluginDetails(Plugin plugin)
    {
        Line(0, "Plugin Details:");
        Line(1, $"{"Name",-25}{plugin.GetName()}");
        Line(1, $"{"Description",-25}{plugin.GetDescription()}");
        Line(1, $"{"Filename",-25}{plugin.GetFilename() ?? "(null)"}");
        Line(1, $"{"Version",-25}{plugin.GetVersion()}");
        Line(1, $"{"License",-25}{plugin.GetLicense()}");
        Line(1, $"{"Source module",-25}{plugin.GetSource()}");

        if (HasGeneratedDocumentation(plugin))
        {
            Line(1, $"{"Documentation",-25}{DocBaseUrl}/{plugin.GetName()}/");
        }

        if (plugin.GetReleaseDateString() is { } released)
        {
            // YYYY-MM-DDTHH:MMZ becomes YYYY-MM-DD HH:MM (UTC); a plain
            // YYYY-MM-DD is printed as it is and without a zone.
            string text = released;
            string zone = string.Empty;

            if (text.Contains('T', StringComparison.Ordinal))
            {
                text = text.Replace('T', ' ').Replace('Z', ' ');
                zone = "(UTC)";
            }

            Line(1, $"{"Source release date",-25}{text}{zone}");
        }

        Line(1, $"{"Binary package",-25}{plugin.GetPackage()}");
        Line(1, $"{"Origin URL",-25}{plugin.GetOrigin()}");
        Line(0, string.Empty);
    }

    /// <summary>
    /// Prints the type hierarchy of the element, the way
    /// <c>print_hierarchy</c> does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    private static void PrintHierarchy(Element element)
    {
        List<string> chain = [];

        for (GType type = element.NativeType; type.IsValid; type = type.Parent)
        {
            chain.Add(type.Name);
        }

        // The C recursion prints the root first and indents one step further
        // for every level on the way back down.
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            int distance = chain.Count - 1 - i;

            Console.WriteLine(distance == 0
                ? chain[i]
                : new string(' ', 6 * (distance - 1)) + " +----" + chain[i]);
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Prints the pad templates of the element, the way
    /// <c>print_pad_templates_info</c> does.
    /// </summary>
    /// <param name="element">The element the templates belong to.</param>
    /// <param name="factory">The factory of the element.</param>
    /// <remarks>
    /// The C tool reads the static templates off the factory, through
    /// <c>gst_element_factory_get_static_pad_templates</c>, which returns a
    /// <c>GList</c> of structures the binding does not marshal. The templates of
    /// the element's own class are the same set with the same names, directions,
    /// presences and caps — they are what the factory's static ones were turned
    /// into — so they are what is walked here. Their direction and presence come
    /// from the GObject properties of the template, since
    /// <c>GST_PAD_TEMPLATE_DIRECTION</c> and <c>GST_PAD_TEMPLATE_PRESENCE</c>
    /// are macros over the structure and have no accessor to bind.
    /// </remarks>
    private static void PrintPadTemplates(Element element, ElementFactory factory)
    {
        Line(0, "Pad Templates:");

        if (factory.GetNumPadTemplates() == 0)
        {
            Line(1, "none");
            return;
        }

        List<PadTemplate> templates = [.. element.GetPadTemplateList()];
        templates.Sort(static (left, right) => string.CompareOrdinal(NameTemplateOf(left), NameTemplateOf(right)));

        for (int i = 0; i < templates.Count; i++)
        {
            PadTemplate template = templates[i];

            string direction = DirectionOf(template) switch
            {
                PadDirection.Src => "SRC",
                PadDirection.Sink => "SINK",
                _ => "UNKNOWN",
            };

            Line(1, $"{direction} template: '{NameTemplateOf(template)}'");

            string availability = PresenceOf(template) switch
            {
                PadPresence.Always => "Always",
                PadPresence.Sometimes => "Sometimes",
                PadPresence.Request => "On request",
                _ => "UNKNOWN",
            };

            Line(2, $"Availability: {availability}");

            using Caps caps = template.GetCaps();
            Line(2, "Capabilities:");
            PrintCaps(caps, 3, string.Empty, string.Empty);

            if (i + 1 < templates.Count)
            {
                Line(1, string.Empty);
            }
        }
    }

    /// <summary>
    /// Prints what the element can do with URIs, the way
    /// <c>print_uri_handler_info</c> does.
    /// </summary>
    /// <param name="factory">The factory of the element.</param>
    /// <remarks>
    /// The C tool asks the element, this asks its factory: the registry knows
    /// the URI type of every element without loading one, and
    /// <c>gst_uri_handler_get_uri_type</c> on an element that is not a handler
    /// answers <c>GST_URI_UNKNOWN</c>, which is what the factory answers too.
    /// The list of protocols underneath needs
    /// <c>gst_uri_handler_get_protocols</c>, another <c>gchar**</c> return that
    /// is not bound, so it is missing from this page.
    /// </remarks>
    private static void PrintUriHandling(ElementFactory factory)
    {
        // The blank line the clocking section would have printed. See the
        // coverage note.
        Line(0, string.Empty);

        string? type = factory.GetUriType() switch
        {
            URIType.Src => "source",
            URIType.Sink => "sink",
            _ => null,
        };

        if (type is null)
        {
            Line(0, "Element has no URI handling capabilities.");
            return;
        }

        Line(0, "URI handling capabilities:");
        Line(1, $"Element can act as {type}.");
    }

    /// <summary>
    /// Prints the properties of the element, the way
    /// <c>print_object_properties_info</c> does for what is reachable.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    /// <remarks>
    /// Three columns of the C layout are missing and all three are the same
    /// gap: <c>GParamSpec</c> introspection beyond name, type and flags is not
    /// bound. The description after the name is
    /// <c>g_param_spec_get_blurb</c>; the range of a numeric property is a
    /// field of the <c>GParamSpec</c> subtype; the table under an enumeration
    /// or a flags property is a walk of its <c>GEnumClass</c>. The name, the
    /// flags, the type and the value are printed in their C columns, and the
    /// value is read off a freshly built element, which is exactly what the C
    /// tool prints as the default for a readable property.
    /// </remarks>
    private void PrintProperties(Element element)
    {
        Console.WriteLine();
        Line(0, "Element Properties:");
        Line(0, string.Empty);

        List<ParamSpec> properties = [.. element.ListProperties()];
        properties.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        try
        {
            if (properties.Count == 0)
            {
                Line(1, "none");
                return;
            }

            foreach (ParamSpec property in properties)
            {
                Line(1, $"{property.Name,-20}:");
                Line(12, $"flags: {FlagsOf(property.Flags)}");
                Line(12, DescribeValue(element, property));
                Line(1, string.Empty);
            }
        }
        finally
        {
            foreach (ParamSpec property in properties)
            {
                property.Dispose();
            }
        }
    }

    /// <summary>
    /// Names the sections of <c>gst-inspect-1.0</c> that this page leaves out.
    /// </summary>
    /// <remarks>
    /// A page that silently dropped half a tool's sections would read like the
    /// whole of it. The note is the honest alternative, and
    /// <c>--no-coverage-note</c> takes it off for a run that is being compared
    /// against the C tool line by line.
    /// </remarks>
    private void PrintCoverageNote()
    {
        if (!_options.ShowCoverage)
        {
            return;
        }

        Console.WriteLine();
        Line(0, "Sections of gst-inspect-1.0 this port does not print: Element Flags,");
        Line(0, "Implemented Interfaces, Clocking Interaction, Pads, Element Signals,");
        Line(0, "Children and Presets; inside the sections above, property descriptions,");
        Line(0, "numeric ranges, enumeration and flags tables, the fields of a structure");
        Line(0, "valued property, and the list of supported URI protocols. The header of");
        Line(0, "samples/GstInspect says what each of them needs. Blank lines that belong");
        Line(0, "to a section that is not printed are not printed either.");
    }

    /// <summary>
    /// Prints one caps, the way <c>print_caps</c> does.
    /// </summary>
    /// <param name="caps">The caps to print.</param>
    /// <param name="depth">The indentation.</param>
    /// <param name="prefix">The prefix every line carries.</param>
    /// <param name="fieldName">The name of the field the caps came from.</param>
    private static void PrintCaps(Caps caps, int depth, string prefix, string fieldName)
    {
        if (caps.IsAny())
        {
            Line(depth, prefix + "ANY");
            return;
        }

        if (caps.IsEmpty())
        {
            Line(depth, prefix + "EMPTY");
            return;
        }

        uint size = caps.GetSize();

        for (uint i = 0; i < size; i++)
        {
            using Structure structure = caps.GetStructure(i);
            using CapsFeatures? features = caps.GetFeatures(i);

            if (features is not null && IsWorthPrinting(features))
            {
                Line(depth, $"{prefix}{fieldName}{structure.GetName()}({features})");
            }
            else
            {
                Line(depth, $"{prefix}{fieldName}{(fieldName.Length > 0 ? ": " : string.Empty)}{structure.GetName()}");
            }

            PrintFields(structure, depth, prefix);
        }
    }

    /// <summary>
    /// Prints the fields of one structure of a caps, the way
    /// <c>print_field</c> does for the values that are reachable.
    /// </summary>
    /// <param name="structure">The structure to print.</param>
    /// <param name="depth">The indentation.</param>
    /// <param name="prefix">The prefix every line carries.</param>
    /// <remarks>
    /// The fields are walked by index rather than through
    /// <c>gst_structure_foreach_id_str</c>, which has no binding.
    /// <see cref="Structure.NthFieldName"/> reports them in the order the
    /// structure stores them, which is the order the callback would have seen
    /// them in. A field whose value is itself a caps or a structure is printed
    /// as its serialization rather than recursed into: the nesting needs
    /// <c>gst_value_get_caps</c> and <c>gst_value_get_structure</c>, which are
    /// not bound.
    /// </remarks>
    private static void PrintFields(Structure structure, int depth, string prefix)
    {
        int fields = structure.NFields();

        for (int i = 0; i < fields; i++)
        {
            string field = structure.NthFieldName((uint)i);
            using Value value = structure.GetValue(field);

            // gchararray is spelled "string" for a caps field, and nothing else
            // is renamed.
            string typeName = value.Type == GType.String ? "string" : value.Type.Name;

            Line(depth, $"{prefix}  {field,15}: {Global.ValueSerialize(value)} ({typeName})");
        }
    }

    /// <summary>
    /// Tells whether the memory features of a caps say anything the C tool
    /// prints.
    /// </summary>
    /// <param name="features">The features of one structure of the caps.</param>
    /// <returns>
    /// <see langword="true"/> for ANY, and for anything that is not plain
    /// system memory.
    /// </returns>
    private static bool IsWorthPrinting(CapsFeatures features)
    {
        if (features.IsAny())
        {
            return true;
        }

        // GST_CAPS_FEATURES_MEMORY_SYSTEM_MEMORY is a static of the library and
        // has no binding; the same single feature built here compares equal.
        using CapsFeatures systemMemory = CapsFeatures.NewSingle("memory:SystemMemory");
        return !features.IsEqual(systemMemory);
    }

    /// <summary>
    /// Spells the flags of a property, the way the C tool spells them.
    /// </summary>
    /// <param name="flags">The flags of the property.</param>
    /// <returns>The comma separated list.</returns>
    /// <remarks>
    /// The GStreamer half of the bits — controllable, the three mutable-in-state
    /// bits and conditionally available — are not members of
    /// <see cref="ParamFlags"/>, which lists what GObject itself defines, so
    /// they are named by their bit here. They are
    /// <c>1 &lt;&lt; (G_PARAM_USER_SHIFT + n)</c> out of
    /// <c>gst/gstparamspecs.h</c>.
    /// </remarks>
    private static string FlagsOf(ParamFlags flags)
    {
        const uint Controllable = 1u << 9;
        const uint MutableReady = 1u << 10;
        const uint MutablePaused = 1u << 11;
        const uint MutablePlaying = 1u << 12;
        const uint DocShowDefault = 1u << 13;
        const uint ConditionallyAvailable = 1u << 14;

        uint bits = (uint)flags;
        List<string> named = [];

        if ((flags & ParamFlags.Readable) != 0)
        {
            named.Add("readable");
        }

        if ((flags & ParamFlags.Writable) != 0)
        {
            named.Add("writable");
        }

        if ((flags & ParamFlags.Deprecated) != 0)
        {
            named.Add("deprecated");
        }

        if ((bits & Controllable) != 0)
        {
            named.Add("controllable");
        }

        if ((bits & ConditionallyAvailable) != 0)
        {
            named.Add("conditionally available");
        }

        if ((flags & ParamFlags.ConstructOnly) != 0)
        {
            named.Add("can be set only at object construction time");
        }
        else if ((bits & MutablePlaying) != 0)
        {
            named.Add("changeable in NULL, READY, PAUSED or PLAYING state");
        }
        else if ((bits & MutablePaused) != 0)
        {
            named.Add("changeable only in NULL, READY or PAUSED state");
        }
        else if ((bits & MutableReady) != 0)
        {
            named.Add("changeable only in NULL or READY state");
        }

        const uint Known = (uint)(ParamFlags.Construct | ParamFlags.ConstructOnly | ParamFlags.LaxValidation
            | ParamFlags.StaticStrings | ParamFlags.Readable | ParamFlags.Writable | ParamFlags.Deprecated)
            | Controllable | MutablePlaying | MutablePaused | MutableReady | ConditionallyAvailable
            | DocShowDefault;

        if ((bits & ~Known) != 0)
        {
            named.Add("0x" + (bits & ~Known).ToString("x", CultureInfo.InvariantCulture));
        }

        return string.Join(", ", named);
    }

    /// <summary>
    /// Describes the type of a property and the value a fresh element has.
    /// </summary>
    /// <param name="element">The element the property belongs to.</param>
    /// <param name="property">The property to describe.</param>
    /// <returns>The line the C tool writes under the flags.</returns>
    private static string DescribeValue(Element element, ParamSpec property)
    {
        GType type = property.ValueType;
        string typeName = type.Name;

        // A property that cannot be read has no value to show: the C tool falls
        // back to g_param_value_set_default, which needs a GParamSpec accessor
        // that is not bound.
        if ((property.Flags & ParamFlags.Readable) == 0)
        {
            return NameOfType(type) + " (write only)";
        }

        using Value value = element.GetProperty(property.Name);

        if (type == GType.String)
        {
            return value.GetString() is { } text ? $"String. Default: \"{text}\"" : "String. Default: null";
        }

        if (type == GType.Boolean)
        {
            return $"Boolean. Default: {(value.GetBoolean() ? "true" : "false")}";
        }

        if (type == GType.Int)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Integer. Default: {value.GetInt()}");
        }

        if (type == GType.UInt)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Unsigned Integer. Default: {value.GetUInt()}");
        }

        if (type == GType.Int64)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Integer64. Default: {value.GetInt64()}");
        }

        if (type == GType.UInt64)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Unsigned Integer64. Default: {value.GetUInt64()}");
        }

        if (type == GType.Long)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Long. Default: {value.GetLong()}");
        }

        if (type == GType.ULong)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Unsigned Long. Default: {value.GetULong()}");
        }

        if (type == GType.Float)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Float. Default: {value.GetFloat()}");
        }

        if (type == GType.Double)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Double. Default: {value.GetDouble()}");
        }

        GType fundamental = type.Fundamental;

        if (fundamental == GType.Enum)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Enum \"{typeName}\" Default: {value.GetEnum()}");
        }

        if (fundamental == GType.Flags)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Flags \"{typeName}\" Default: 0x{value.GetFlags():x8}");
        }

        if (fundamental == GType.Object)
        {
            return $"Object of type \"{typeName}\"";
        }

        if (fundamental == GType.Boxed)
        {
            return $"Boxed pointer of type \"{typeName}\"";
        }

        if (fundamental == GType.Pointer)
        {
            return string.Equals(typeName, "gpointer", StringComparison.Ordinal)
                ? "Pointer."
                : $"Pointer of type \"{typeName}\".";
        }

        return string.Create(CultureInfo.InvariantCulture, $"Unknown type {type.Value} \"{typeName}\"");
    }

    /// <summary>
    /// Names the type of a property that could not be read.
    /// </summary>
    /// <param name="type">The type of the values of the property.</param>
    /// <returns>The name the C tool would have used.</returns>
    private static string NameOfType(GType type) =>
        type == GType.String ? "String."
        : type == GType.Boolean ? "Boolean."
        : type == GType.Int ? "Integer."
        : type == GType.UInt ? "Unsigned Integer."
        : type == GType.Int64 ? "Integer64."
        : type == GType.UInt64 ? "Unsigned Integer64."
        : type == GType.Float ? "Float."
        : type == GType.Double ? "Double."
        : $"\"{type.Name}\"";

    /// <summary>
    /// Names a rank the way <c>get_rank_name</c> does.
    /// </summary>
    /// <param name="rank">The rank of the feature.</param>
    /// <returns>The name, or the nearest name and the distance to it.</returns>
    private static string RankName(int rank)
    {
        int best = 0;

        for (int i = 0; i < RankNames.Length; i++)
        {
            if (rank == (int)RankNames[i].Value)
            {
                return RankNames[i].Name;
            }

            if (Math.Abs(rank - (int)RankNames[i].Value) < Math.Abs(rank - (int)RankNames[best].Value))
            {
                best = i;
            }
        }

        int distance = rank - (int)RankNames[best].Value;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{RankNames[best].Name} {(distance > 0 ? '+' : '-')} {Math.Abs(distance)}");
    }

    /// <summary>
    /// Tells whether the plugin belongs to a module whose documentation is
    /// generated, which is what lets the C tool build a URL for it.
    /// </summary>
    /// <param name="plugin">The plugin to test.</param>
    /// <returns><see langword="true"/> when a URL can be built.</returns>
    private static bool HasGeneratedDocumentation(Plugin plugin) =>
        Array.IndexOf(GstreamerModules, plugin.GetSource()) >= 0
        || plugin.GetOrigin().EndsWith("/gst-plugins-rs", StringComparison.Ordinal);

    /// <summary>Reads the name template of a pad template.</summary>
    /// <param name="template">The template to read.</param>
    /// <returns>The name the template gives its pads.</returns>
    private static string NameTemplateOf(PadTemplate template)
    {
        using Value value = template.GetProperty("name-template");
        return value.GetString() ?? string.Empty;
    }

    /// <summary>Reads the direction of a pad template.</summary>
    /// <param name="template">The template to read.</param>
    /// <returns>Which way the pads of the template carry data.</returns>
    private static PadDirection DirectionOf(PadTemplate template)
    {
        using Value value = template.GetProperty("direction");
        return (PadDirection)value.GetEnum();
    }

    /// <summary>Reads the presence of a pad template.</summary>
    /// <param name="template">The template to read.</param>
    /// <returns>When the pads of the template exist.</returns>
    private static PadPresence PresenceOf(PadTemplate template)
    {
        using Value value = template.GetProperty("presence");
        return (PadPresence)value.GetEnum();
    }

    /// <summary>
    /// Uppercases the first letter of a metadata key, as the C tool does before
    /// it prints one.
    /// </summary>
    /// <param name="key">The key to print.</param>
    /// <returns>The key with its first letter uppercased.</returns>
    private static string Capitalize(string key) =>
        key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..];

    /// <summary>
    /// Writes one line at an indentation, which is what <c>n_print</c> does.
    /// </summary>
    /// <param name="depth">How many levels to indent by.</param>
    /// <param name="text">The line.</param>
    /// <remarks>
    /// An empty line is written as the indentation and nothing else, because
    /// that is what <c>n_print ("\n")</c> produces — a detail that shows up in
    /// a diff against the C tool as trailing spaces.
    /// </remarks>
    private static void Line(int depth, string text) =>
        Console.WriteLine(new string(' ', 2 * depth) + text);
}
