using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;

/// <summary>
/// The run of the inspect-lite port: either the registry census or the page of
/// one element.
/// </summary>
internal sealed partial class Inspection
{
    /// <summary>The registry was read.</summary>
    private const int ExitNoError = 0;

    /// <summary>The command line was wrong, or the element was not found.</summary>
    private const int ExitError = 1;

    private readonly Options _options;

    /// <summary>Initialises a run.</summary>
    /// <param name="options">The command line of the process.</param>
    private Inspection(Options options) => _options = options;

    /// <summary>
    /// Reads the command line, initialises GStreamer and prints what was asked
    /// for.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>
    /// The exit code of the C tool: 0 when the census or the page was printed,
    /// and 1 when the command line was unusable or no such element exists.
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
            GstSharp.Initialize(options.Native);
        }
        catch (Exception error)
        {
            Console.WriteLine($"Error initializing: {error.Message}");
            return ExitError;
        }

        try
        {
            Inspection run = new(options);

            return options.Names.Count == 0
                ? run.PrintCensus()
                : run.PrintPages();
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
    /// Prints the page of every name on the command line.
    /// </summary>
    /// <returns>The exit code.</returns>
    private int PrintPages()
    {
        int result = ExitNoError;

        foreach (string name in _options.Names)
        {
            if (PrintElement(name) != ExitNoError)
            {
                result = ExitError;
            }

            GstSharp.DrainPendingReleases();
        }

        return result;
    }

    /// <summary>
    /// Prints the page of one element, the way <c>print_element_info</c>
    /// does.
    /// </summary>
    /// <param name="name">The name of the element factory.</param>
    /// <returns>The exit code.</returns>
    private int PrintElement(string name)
    {
        // gst_element_factory_find loads the feature, which is the first half
        // of what print_element_info does with gst_plugin_feature_load.
        using ElementFactory? factory = ElementFactory.Find(name);

        if (factory is null)
        {
            Console.WriteLine($"No such element or plugin '{name}'");
            return ExitError;
        }

        using Element? element = factory.Create(null);

        if (element is null)
        {
            Console.WriteLine("couldn't construct element for some reason");
            return ExitError;
        }

        using Plugin? plugin = factory.GetPlugin();

        PrintFactoryDetails(factory, plugin);

        if (plugin is not null)
        {
            PrintPluginDetails(plugin);
        }

        PrintHierarchy(element);
        PrintInterfaces(element);
        PrintElementFlags(element);
        PrintPadTemplates(element, factory);
        PrintClocking(element);
        PrintUriHandling(element);
        PrintPads(element);
        PrintProperties(element);
        PrintSignals(element);
        PrintChildren(element);
        PrintPresets(element);

        return ExitNoError;
    }

    /// <summary>
    /// Prints one line per feature of every plugin in the registry, the way
    /// <c>print_element_list</c> does, and the totals underneath.
    /// </summary>
    /// <returns>The exit code.</returns>
    /// <remarks>
    /// The order is the one <c>--sort-type=name</c> produces, which is the
    /// default of the C tool: plugins by name, and the features of each plugin
    /// by name, both compared byte by byte as <c>strcmp</c> does.
    /// </remarks>
    private int PrintCensus()
    {
        Registry registry = Registry.Get();

        List<Plugin> plugins = [.. registry.GetPluginList()];
        plugins.Sort(static (left, right) => string.CompareOrdinal(left.GetName(), right.GetName()));

        int pluginCount = 0;
        int featureCount = 0;
        int blacklistCount = 0;

        foreach (Plugin plugin in plugins)
        {
            pluginCount++;

            if (plugin.IsFlagSet((uint)PluginFlags.Blacklisted))
            {
                blacklistCount++;
                continue;
            }

            List<PluginFeature> features = [.. registry.GetFeatureListByPlugin(plugin.GetName())];
            features.Sort(static (left, right) => string.CompareOrdinal(left.GetName(), right.GetName()));

            foreach (PluginFeature feature in features)
            {
                featureCount++;
                PrintFeatureLine(plugin, feature);
            }
        }

        string blacklist = blacklistCount == 0
            ? string.Empty
            : $" ({Plural(blacklistCount, "blacklist entry", "blacklist entries")} not shown)";

        Console.WriteLine();
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Total count: {Plural(pluginCount, "plugin")}{blacklist}, {Plural(featureCount, "feature")}"));

        return ExitNoError;
    }

    /// <summary>
    /// Spells a count the way <c>ngettext</c> does for the English catalogue.
    /// </summary>
    /// <param name="count">How many there are.</param>
    /// <param name="noun">What they are.</param>
    /// <param name="plural">
    /// What more than one of them are, when adding an <c>s</c> is not enough.
    /// </param>
    /// <returns>The counted noun.</returns>
    private static string Plural(int count, string noun, string? plural = null) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{count} {(count == 1 ? noun : plural ?? noun + "s")}");
}
