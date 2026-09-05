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
    /// The metadata key of the documentation URL of a factory, which decides
    /// whether one is built.
    /// </summary>
    private const string DocUriKey = "doc-uri";

    /// <summary>The indentation everything under the name of a property is written at.</summary>
    /// <remarks>
    /// <c>print_object_properties_info</c> runs inside one <c>push_indent</c>
    /// and pushes eleven more for the lines under the name, and <c>n_print</c>
    /// writes two spaces per level.
    /// </remarks>
    private static readonly string PropertyIndent = new(' ', 2 * 12);

    /// <summary>
    /// The prefix <c>print_object_properties_info</c> hands to
    /// <c>print_caps</c> and to <c>print_field</c> for a caps valued or a
    /// structure valued property.
    /// </summary>
    private static readonly string PropertyValuePrefix = new(' ', 27);

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
    /// The three branches of the C tool are an element factory, a typefind
    /// factory — whose file extensions follow a single space rather than the
    /// two an element factory gets — and anything else, printed with its
    /// GObject type name in parentheses.
    /// </remarks>
    private static void PrintFeatureLine(Plugin plugin, PluginFeature feature)
    {
        if (feature is ElementFactory factory)
        {
            Console.WriteLine(
                $"{plugin.GetName()}:  {factory.GetName()}: {factory.GetMetadata("long-name")}");
            return;
        }

        if (feature is TypeFindFactory typefind)
        {
            string[]? extensions = typefind.GetExtensions();

            Console.WriteLine($"{plugin.GetName()}: {feature.GetName()}: "
                + (extensions is null ? "no extensions" : string.Join(", ", extensions)));
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

        foreach (string key in factory.GetMetadataKeys() ?? [])
        {
            string shown = Capitalize(key);

            // The C tool uppercases the first letter of the key in place before
            // it compares the key against "doc-uri", so the comparison never
            // matches and the documentation URL is built even for a factory
            // that carries a doc-uri of its own. The quirk is reproduced rather
            // than fixed, because this page is diffed against that tool.
            seenDocUri = seenDocUri || string.Equals(shown, DocUriKey, StringComparison.Ordinal);
            Line(1, $"{shown,-25}{factory.GetMetadata(key)}");
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
    /// <c>gst_element_factory_get_static_pad_templates</c>, which is bound as
    /// <c>ElementFactory.GetStaticPadTemplates</c>. What it hands back is a list
    /// of records bound behind a pointer, and a record bound behind a pointer
    /// offers no fields, so the name, direction and presence the C tool reads
    /// out of the structure would have to come from the <c>GstPadTemplate</c>
    /// that <c>gst_static_pad_template_get</c> builds from each one. The
    /// templates of the element's own class are that same set — they are what
    /// the factory's static ones were turned into — and they are already built,
    /// so they are what is walked here. Their direction and presence come from
    /// the GObject properties of the template, since
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

            // A template whose pads are of a class of their own, which is what
            // GST_PAD_TEMPLATE_GTYPE answers: the C tool names the class and
            // prints its properties with no instance to read them off. A
            // template that builds plain pads reports GstPad, and one that was
            // built without a type reports none; neither gets the block.
            GType padType = template.Gtype;

            if (padType != GType.None && !string.Equals(padType.Name, "GstPad", StringComparison.Ordinal))
            {
                Line(2, $"Type: {padType.Name}");
                PrintProperties(Gst.GObject.Object.ListProperties(padType), null, "Pad Properties", 2);
            }

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
    /// <param name="element">The element to describe.</param>
    private static void PrintUriHandling(Element element)
    {
        if (element.As<IURIHandler>() is not { } handler)
        {
            Line(0, "Element has no URI handling capabilities.");
            return;
        }

        string type = handler.GetUriType() switch
        {
            URIType.Src => "source",
            URIType.Sink => "sink",
            _ => "unknown",
        };

        string[]? protocols = handler.GetProtocols();

        Line(0, string.Empty);
        Line(0, "URI handling capabilities:");
        Line(1, $"Element can act as {type}.");

        if (protocols is { Length: > 0 })
        {
            Line(1, "Supported URI protocols:");

            foreach (string protocol in protocols)
            {
                Line(2, protocol);
            }
        }
        else
        {
            Line(1, "No supported URI protocols");
        }
    }

    /// <summary>
    /// Prints the properties of the element, the way
    /// <c>print_element_properties_info</c> does.
    /// </summary>
    /// <param name="element">The element to describe.</param>
    private static void PrintProperties(Element element)
    {
        Console.WriteLine();
        PrintProperties(element.ListProperties(), element, "Element Properties", 0);
    }

    /// <summary>
    /// Prints one block of properties, the way
    /// <c>print_object_properties_info</c> does.
    /// </summary>
    /// <param name="properties">The specifications to print, disposed here.</param>
    /// <param name="instance">
    /// The object to read the values off, or <see langword="null"/> for a class
    /// nothing has an instance of.
    /// </param>
    /// <param name="description">The heading, without its colon.</param>
    /// <param name="depth">The indentation of the heading.</param>
    /// <remarks>
    /// The C function takes a class and an object that may be <c>NULL</c>.
    /// With no object it reads no value - the default of each specification
    /// stands in for one - and it leaves out every property the pad hierarchy
    /// already carries, so that the block under a pad template says what that
    /// pad type adds rather than repeating a page of its own.
    /// </remarks>
    private static void PrintProperties(
        ParamSpec[] properties,
        Gst.GObject.Object? instance,
        string description,
        int depth)
    {
        Line(depth, $"{description}:");
        Line(depth, string.Empty);

        List<ParamSpec> sorted = [.. properties];
        sorted.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        try
        {
            // No count and no "none": the C function prints the heading, the
            // blank line and whatever survives the loop, so a class whose every
            // property belongs to the hierarchy above it prints a heading with
            // nothing under it.
            foreach (ParamSpec property in sorted)
            {
                if (instance is null && IsInherited(property))
                {
                    continue;
                }

                PrintProperty(instance, property, depth + 1);
            }
        }
        finally
        {
            foreach (ParamSpec property in sorted)
            {
                property.Dispose();
            }
        }
    }

    /// <summary>
    /// Tells whether a property comes from the hierarchy every pad has, which
    /// is what the C tool leaves out of a block printed without an object.
    /// </summary>
    /// <param name="property">The property to test.</param>
    /// <returns><see langword="true"/> when the property is inherited.</returns>
    private static bool IsInherited(ParamSpec property)
    {
        // The C tool compares the owner against G_TYPE_OBJECT, GST_TYPE_OBJECT
        // and GST_TYPE_PAD. It is the owner itself that is compared, never a
        // derivation of it, and a registered name names one type, so the names
        // say the same thing - and they are what a sample can ask for.
        string owner = property.OwnerType.Name;

        return string.Equals(owner, "GObject", StringComparison.Ordinal)
            || string.Equals(owner, "GstObject", StringComparison.Ordinal)
            || string.Equals(owner, "GstPad", StringComparison.Ordinal);
    }

    /// <summary>
    /// Prints one property, which is the body of the loop of
    /// <c>print_object_properties_info</c>.
    /// </summary>
    /// <param name="element">The element the property belongs to.</param>
    /// <param name="property">The property to print.</param>
    /// <remarks>
    /// The C tool reads the value off the element when the property is
    /// readable and falls back to <c>g_param_value_set_default</c> when it is
    /// not; <see cref="ParamSpec.DefaultValue"/> is that default, copied here
    /// because the value itself belongs to the specification.
    /// </remarks>
    private static void PrintProperty(Gst.GObject.Object? instance, ParamSpec property, int depth)
    {
        bool readable = (property.Flags & ParamFlags.Readable) != 0;

        Line(depth, $"{property.Name,-20}: {property.Blurb ?? "(null)"}");

        using Value value = readable && instance is not null
            ? instance.GetProperty(property.Name)
            : property.DefaultValue.ToValue();

        StringBuilder text = new();
        text.Append(PropertyIndent).Append("flags: ").Append(FlagsOf(property.Flags)).Append('\n');
        AppendValue(text, property, value);
        text.Append(readable ? "\n" : " Write only\n");
        Write(Indented(text, depth - 1));

        Line(depth, string.Empty);
    }

    /// <summary>
    /// Indents every line of a block by the levels a caller pushed on top of
    /// the one <c>print_object_properties_info</c> writes its lines at.
    /// </summary>
    /// <param name="block">The block, whose lines already carry that one level.</param>
    /// <param name="levels">How many levels to add.</param>
    /// <returns>The same block.</returns>
    private static StringBuilder Indented(StringBuilder block, int levels)
    {
        if (levels <= 0)
        {
            return block;
        }

        string padding = new(' ', 2 * levels);

        // Backwards, and short of the newline that ends the block: an
        // insertion moves everything after it, and the end carries no line of
        // its own.
        for (int position = block.Length - 2; position >= 0; position--)
        {
            if (block[position] == '\n')
            {
                block.Insert(position + 1, padding);
            }
        }

        return block.Insert(0, padding);
    }

    /// <summary>
    /// Appends the type, the range and the default of one property, which is
    /// the <c>switch</c> of <c>print_object_properties_info</c>.
    /// </summary>
    /// <param name="text">The block being built.</param>
    /// <param name="property">The property to describe.</param>
    /// <param name="value">The value the property was read as.</param>
    private static void AppendValue(StringBuilder text, ParamSpec property, in Value value)
    {
        GType type = value.Type;

        if (type == GType.String)
        {
            string? content = value.GetString();

            text.Append(PropertyIndent).Append("String. ")
                .Append(content is null ? "Default: null" : $"Default: \"{content}\"");
            return;
        }

        if (type == GType.Boolean)
        {
            text.Append(PropertyIndent)
                .Append(value.GetBoolean() ? "Boolean. Default: true" : "Boolean. Default: false");
            return;
        }

        if (type == GType.ULong && property is ParamSpecULong pulong)
        {
            AppendRange(text, "Unsigned Long", pulong.Minimum, pulong.Maximum, (ulong)value.GetULong());
            return;
        }

        if (type == GType.Long && property is ParamSpecLong plong)
        {
            AppendRange(text, "Long", plong.Minimum, plong.Maximum, (long)value.GetLong());
            return;
        }

        if (type == GType.UInt && property is ParamSpecUInt puint)
        {
            AppendRange(text, "Unsigned Integer", puint.Minimum, puint.Maximum, value.GetUInt());
            return;
        }

        if (type == GType.Int && property is ParamSpecInt pint)
        {
            AppendRange(text, "Integer", pint.Minimum, pint.Maximum, value.GetInt());
            return;
        }

        if (type == GType.UInt64 && property is ParamSpecUInt64 puint64)
        {
            AppendRange(text, "Unsigned Integer64", puint64.Minimum, puint64.Maximum, value.GetUInt64());
            return;
        }

        if (type == GType.Int64 && property is ParamSpecInt64 pint64)
        {
            AppendRange(text, "Integer64", pint64.Minimum, pint64.Maximum, value.GetInt64());
            return;
        }

        if (type == GType.Float && property is ParamSpecFloat pfloat)
        {
            AppendFloatRange(text, "Float", pfloat.Minimum, pfloat.Maximum, value.GetFloat());
            return;
        }

        if (type == GType.Double && property is ParamSpecDouble pdouble)
        {
            AppendFloatRange(text, "Double", pdouble.Minimum, pdouble.Maximum, value.GetDouble());
            return;
        }

        AppendOtherValue(text, property, value);
    }

    /// <summary>
    /// Appends the branches the <c>default:</c> label of
    /// <c>print_object_properties_info</c> covers.
    /// </summary>
    /// <param name="text">The block being built.</param>
    /// <param name="property">The property to describe.</param>
    /// <param name="value">The value the property was read as.</param>
    /// <remarks>
    /// The order of the tests is the C tool's, except that a fraction, a
    /// <c>GstValueArray</c> and a <c>GValueArray</c> are recognised before a
    /// boxed value rather than after: their specification classes do not
    /// derive from <c>GParamSpecBoxed</c>, which is what the C tool tests,
    /// while their value types are boxed, which is what a test on the value
    /// would see. The <c>GValueArray</c> one is a test on the class of the
    /// specification, the way <c>G_IS_PARAM_SPEC_VALUE_ARRAY</c> is: a
    /// <c>GParamSpecBoxed</c> whose value type happens to be a
    /// <c>GValueArray</c> is a boxed value to the C tool.
    /// </remarks>
    private static void AppendOtherValue(StringBuilder text, ParamSpec property, in Value value)
    {
        GType type = property.ValueType;
        string name = type.Name;

        if (string.Equals(name, "GstCaps", StringComparison.Ordinal))
        {
            using Caps? caps = value.GetMiniObject<Caps>();

            if (caps is null)
            {
                text.Append(PropertyIndent).Append("Caps (NULL)");
            }
            else
            {
                AppendCaps(text, caps, 12, PropertyValuePrefix, string.Empty);
            }

            return;
        }

        if (property is ParamSpecEnum penum)
        {
            int current = value.GetEnum();
            EnumValue[] values = penum.Values;
            string nick = string.Empty;

            foreach (EnumValue member in values)
            {
                if (member.Value == current)
                {
                    nick = member.Nick ?? string.Empty;
                }
            }

            text.Append(PropertyIndent).Append(string.Create(
                CultureInfo.InvariantCulture,
                $"Enum \"{name}\" Default: {current}, \"{nick}\""));

            foreach (EnumValue member in values)
            {
                text.Append('\n').Append(PropertyIndent).Append(string.Create(
                    CultureInfo.InvariantCulture,
                    $"   ({member.Value}): {member.Nick ?? string.Empty,-16} - {member.Name}"));
            }

            return;
        }

        if (property is ParamSpecFlags pflags)
        {
            uint current = value.GetFlags();
            FlagsValue[] values = pflags.Values;

            text.Append(PropertyIndent).Append(string.Create(
                CultureInfo.InvariantCulture,
                $"Flags \"{name}\" Default: 0x{current:x8}, \"{FlagsToString(values, current)}\""));

            foreach (FlagsValue member in values)
            {
                text.Append('\n').Append(PropertyIndent).Append(string.Create(
                    CultureInfo.InvariantCulture,
                    $"   (0x{member.Value:x8}): {member.Nick ?? string.Empty,-16} - {member.Name}"));
            }

            return;
        }

        if (property is ParamSpecFraction fraction)
        {
            text.Append(PropertyIndent).Append(string.Create(
                CultureInfo.InvariantCulture,
                $"Fraction. Range: {fraction.MinimumNumerator}/{fraction.MinimumDenominator} - "
                + $"{fraction.MaximumNumerator}/{fraction.MaximumDenominator} Default: "
                + $"{Global.ValueGetFractionNumerator(value)}/{Global.ValueGetFractionDenominator(value)} "));
            return;
        }

        if (property is ParamSpecArray array)
        {
            if (string.Equals(value.Type.Name, "GstValueArray", StringComparison.Ordinal))
            {
                text.Append(PropertyIndent)
                    .Append($"Default: \"{Global.ValueSerialize(value)}\"").Append('\n');
            }

            using ParamSpec? member = array.ElementSpec;

            text.Append(PropertyIndent).Append(member is null
                ? "GstValueArray of GValues"
                : $"GstValueArray of GValues of type \"{member.ValueType.Name}\"");
            return;
        }

        if (property is ParamSpecValueArray valueArray)
        {
            using ParamSpec? member = valueArray.ElementSpec;

            text.Append(PropertyIndent).Append(member is null
                ? "Array of GValues"
                : $"Array of GValues of type \"{member.ValueType.Name}\"");
            return;
        }

        GType fundamental = type.Fundamental;

        if (fundamental == GType.Object)
        {
            text.Append(PropertyIndent).Append($"Object of type \"{name}\"");
            return;
        }

        if (fundamental == GType.Boxed)
        {
            text.Append(PropertyIndent).Append($"Boxed pointer of type \"{name}\"");

            if (string.Equals(name, "GstStructure", StringComparison.Ordinal))
            {
                // The copy g_boxed_copy made for the wrapper is the caller's,
                // so it is released once its fields have been printed.
                using Structure? structure = value.GetBoxed<Structure>();

                if (structure is not null)
                {
                    text.Append('\n');
                    AppendFields(text, structure, 12, PropertyValuePrefix);
                }
            }

            return;
        }

        if (fundamental == GType.Pointer)
        {
            text.Append(PropertyIndent).Append(type == GType.Pointer
                ? "Pointer."
                : $"Pointer of type \"{name}\".");
            return;
        }

        text.Append(PropertyIndent).Append(string.Create(
            CultureInfo.InvariantCulture,
            $"Unknown type {(long)type.Value} \"{name}\""));
    }

    /// <summary>Appends the range and the default of an integral property.</summary>
    /// <typeparam name="T">The kind of number the property holds.</typeparam>
    /// <param name="text">The block being built.</param>
    /// <param name="kind">The name the C tool gives the type.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="current">The value the property was read as.</param>
    /// <remarks>
    /// The trailing space is the C tool's: every one of these branches ends its
    /// format string with one.
    /// </remarks>
    private static void AppendRange<T>(StringBuilder text, string kind, T minimum, T maximum, T current)
        where T : IFormattable =>
        text.Append(PropertyIndent).Append(string.Create(
            CultureInfo.InvariantCulture,
            $"{kind}. Range: {minimum} - {maximum} Default: {current} "));

    /// <summary>
    /// Appends the range and the default of a floating point property, whose
    /// numbers the C tool writes with <c>%15.7g</c>.
    /// </summary>
    /// <param name="text">The block being built.</param>
    /// <param name="kind">The name the C tool gives the type.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="current">The value the property was read as.</param>
    private static void AppendFloatRange(
        StringBuilder text,
        string kind,
        double minimum,
        double maximum,
        double current) =>
        text.Append(PropertyIndent).Append(
            $"{kind}. Range: {Printf15G(minimum)} - {Printf15G(maximum)} Default: {Printf15G(current)} ");

    /// <summary>Writes a number the way C's <c>printf ("%15.7g")</c> does.</summary>
    /// <param name="value">The number to write.</param>
    /// <returns>The number, right aligned in fifteen columns.</returns>
    /// <remarks>
    /// .NET's <c>G7</c> is not the same format: it writes an uppercase exponent
    /// of three digits and switches to the exponent form at another magnitude.
    /// C picks that form when the decimal exponent is below -4 or at least the
    /// precision, writes at least two exponent digits, and drops the trailing
    /// zeros of either form.
    /// </remarks>
    private static string Printf15G(double value)
    {
        const int Precision = 7;

        if (double.IsNaN(value))
        {
            return "nan".PadLeft(15);
        }

        if (double.IsInfinity(value))
        {
            return (value < 0 ? "-inf" : "inf").PadLeft(15);
        }

        string scientific = value.ToString("E6", CultureInfo.InvariantCulture);
        int marker = scientific.IndexOf('E', StringComparison.Ordinal);
        int exponent = int.Parse(scientific[(marker + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture);

        if (exponent < -4 || exponent >= Precision)
        {
            string mantissa = TrimTrailingZeros(scientific[..marker]);
            string digits = Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);

            return $"{mantissa}e{(exponent < 0 ? '-' : '+')}{digits}".PadLeft(15);
        }

        int decimals = Math.Max(0, Precision - 1 - exponent);

        return TrimTrailingZeros(value.ToString(
            "F" + decimals.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture)).PadLeft(15);
    }

    /// <summary>Drops the trailing zeros of a decimal fraction, as <c>%g</c> does.</summary>
    /// <param name="text">The number written out.</param>
    /// <returns>The number without a trailing run of zeros or a bare point.</returns>
    private static string TrimTrailingZeros(string text)
    {
        if (!text.Contains('.', StringComparison.Ordinal))
        {
            return text;
        }

        string trimmed = text.TrimEnd('0');

        return trimmed.EndsWith('.') ? trimmed[..^1] : trimmed;
    }

    /// <summary>Spells a set of flags the way <c>flags_to_string</c> does.</summary>
    /// <param name="values">The members of the flags type.</param>
    /// <param name="flags">The set to spell.</param>
    /// <returns>The nicks, joined with a plus sign.</returns>
    /// <remarks>
    /// An exact match wins outright; otherwise the members are taken from the
    /// highest down, greedily, and what does not decompose at all is
    /// <c>(none)</c>.
    /// </remarks>
    private static string FlagsToString(FlagsValue[] values, uint flags)
    {
        foreach (FlagsValue member in values)
        {
            if (member.Value == flags)
            {
                return member.Nick ?? string.Empty;
            }
        }

        StringBuilder text = new();
        uint left = flags;

        for (int i = values.Length - 1; i >= 0; i--)
        {
            if (values[i].Value != 0 && (left & values[i].Value) == values[i].Value)
            {
                if (text.Length > 0)
                {
                    text.Append('+');
                }

                text.Append(values[i].Nick ?? string.Empty);
                left -= values[i].Value;

                if (left == 0)
                {
                    break;
                }
            }
        }

        return text.Length == 0 ? "(none)" : text.ToString();
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
        StringBuilder text = new();
        AppendCaps(text, caps, depth, prefix, fieldName);
        Write(text);
    }

    /// <summary>
    /// Appends one caps to a block, the way <c>print_caps</c> writes one.
    /// </summary>
    /// <param name="text">The block being built.</param>
    /// <param name="caps">The caps to print.</param>
    /// <param name="depth">The indentation.</param>
    /// <param name="prefix">The prefix every line carries.</param>
    /// <param name="fieldName">The name of the field the caps came from.</param>
    private static void AppendCaps(StringBuilder text, Caps caps, int depth, string prefix, string fieldName)
    {
        if (caps.IsAny())
        {
            AppendLine(text, depth, prefix + "ANY");
            return;
        }

        if (caps.IsEmpty())
        {
            AppendLine(text, depth, prefix + "EMPTY");
            return;
        }

        uint size = caps.GetSize();

        for (uint i = 0; i < size; i++)
        {
            using Structure structure = caps.GetStructure(i);
            using CapsFeatures? features = caps.GetFeatures(i);

            if (features is not null && IsWorthPrinting(features))
            {
                AppendLine(text, depth, $"{prefix}{fieldName}{structure.GetName()}({features})");
            }
            else
            {
                AppendLine(
                    text,
                    depth,
                    $"{prefix}{fieldName}{(fieldName.Length > 0 ? ": " : string.Empty)}{structure.GetName()}");
            }

            AppendFields(text, structure, depth, prefix);
        }
    }

    /// <summary>
    /// Appends the fields of one structure to a block, the way
    /// <c>print_field</c> writes them for the values that are reachable.
    /// </summary>
    /// <param name="text">The block being built.</param>
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
    private static void AppendFields(StringBuilder text, Structure structure, int depth, string prefix)
    {
        int fields = structure.NFields();

        for (int i = 0; i < fields; i++)
        {
            string field = structure.NthFieldName((uint)i);
            using Value value = structure.GetValue(field);

            // gchararray is spelled "string" for a caps field, and nothing else
            // is renamed.
            string typeName = value.Type == GType.String ? "string" : value.Type.Name;

            AppendLine(text, depth, $"{prefix}  {field,15}: {Global.ValueSerialize(value)} ({typeName})");
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
    /// <para>
    /// The GStreamer half of the bits — controllable, the three
    /// mutable-in-state bits and conditionally available — are not members of
    /// <see cref="ParamFlags"/>, which lists what GObject itself defines, so
    /// they are named by their bit here. They are
    /// <c>1 &lt;&lt; (G_PARAM_USER_SHIFT + n)</c> out of
    /// <c>gst/gstparamspecs.h</c>.
    /// </para>
    /// <para>
    /// The C tool tracks a <c>first_flag</c> to decide whether a separator is
    /// needed, but writes one unconditionally for controllable, conditionally
    /// available and the mutable-in-state bits. A property that is neither
    /// readable nor writable and carries one of those therefore begins its list
    /// with a comma. The quirk is reproduced rather than fixed, because this
    /// page is diffed against that tool.
    /// </para>
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
        StringBuilder text = new();
        bool first = true;

        if ((flags & ParamFlags.Readable) != 0)
        {
            text.Append("readable");
            first = false;
        }

        if ((flags & ParamFlags.Writable) != 0)
        {
            text.Append(first ? string.Empty : ", ").Append("writable");
            first = false;
        }

        if ((flags & ParamFlags.Deprecated) != 0)
        {
            text.Append(first ? string.Empty : ", ").Append("deprecated");
            first = false;
        }

        if ((bits & Controllable) != 0)
        {
            text.Append(", controllable");
            first = false;
        }

        if ((bits & ConditionallyAvailable) != 0)
        {
            text.Append(", conditionally available");
            first = false;
        }

        if ((flags & ParamFlags.ConstructOnly) != 0)
        {
            text.Append(", can be set only at object construction time");
        }
        else if ((bits & MutablePlaying) != 0)
        {
            text.Append(", changeable in NULL, READY, PAUSED or PLAYING state");
        }
        else if ((bits & MutablePaused) != 0)
        {
            text.Append(", changeable only in NULL, READY or PAUSED state");
        }
        else if ((bits & MutableReady) != 0)
        {
            text.Append(", changeable only in NULL or READY state");
        }

        const uint Known = (uint)(ParamFlags.Construct | ParamFlags.ConstructOnly | ParamFlags.LaxValidation
            | ParamFlags.StaticStrings | ParamFlags.Readable | ParamFlags.Writable | ParamFlags.Deprecated)
            | Controllable | MutablePlaying | MutablePaused | MutableReady | ConditionallyAvailable
            | DocShowDefault;

        if ((bits & ~Known) != 0)
        {
            text.Append(first ? string.Empty : ", ")
                .Append("0x")
                .Append((bits & ~Known).ToString("x", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

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

    /// <summary>Appends one line at an indentation to a block.</summary>
    /// <param name="block">The block being built.</param>
    /// <param name="depth">How many levels to indent by.</param>
    /// <param name="text">The line.</param>
    private static void AppendLine(StringBuilder block, int depth, string text) =>
        block.Append(' ', 2 * depth).Append(text).Append('\n');

    /// <summary>Writes a block whose lines are separated by a single newline.</summary>
    /// <param name="block">The block to write.</param>
    /// <remarks>
    /// A block is built with <c>\n</c>, because a section of the C tool is one
    /// run of <c>g_print</c> calls that decide for themselves where a line ends;
    /// the platform's line break is put back here so that the whole page has
    /// one ending.
    /// </remarks>
    private static void Write(StringBuilder block) =>
        Console.Write(block.Replace("\n", Environment.NewLine).ToString());
}
