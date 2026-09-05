extern alias gstsharp;

using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.Benchmarks;

/// <summary>
/// The process wide state every benchmark class shares.
/// </summary>
/// <remarks>
/// <para>
/// BenchmarkDotNet runs every class of this assembly in one process, and both
/// halves of that state can only happen once in a process: GStreamer is
/// initialised exactly once and never deinitialised (the rule the integration
/// tests follow in <c>GstFixture</c>), and a <c>GType</c> name can only be
/// registered once, so a second registration of the managed identity filter
/// would be a hard failure rather than a slow benchmark.
/// </para>
/// <para>
/// Both are therefore held here, behind a <see cref="Lazy{T}"/> each, and every
/// <c>[GlobalSetup]</c> asks for them instead of doing the work itself.
/// </para>
/// </remarks>
public static class GstRuntime
{
    /// <summary>The <c>GType</c> name of the managed identity filter.</summary>
    public const string IdentityTypeName = "GstSharpBenchmarksIdentityTransform";

    private static readonly Lazy<bool> Initialisation = new(InitialiseCore);

    private static readonly Lazy<SubclassType> IdentityDefinition = new(DefineIdentity);

    // The two pad templates of the managed filter belong to the class for as
    // long as the process lives, so they are rooted here rather than left to
    // the garbage collector.
    private static PadTemplate? sinkTemplate;

    private static PadTemplate? srcTemplate;

    /// <summary>Gets the registered type of the managed identity filter.</summary>
    public static SubclassType IdentityTransformType => IdentityDefinition.Value;

    /// <summary>
    /// Loads and initialises the native GStreamer libraries, once per process.
    /// </summary>
    /// <remarks>
    /// The default options search the machine for an installation; see
    /// <c>GstSharpOptions</c> for the ways to point it somewhere else.
    /// </remarks>
    public static void EnsureInitialised() => _ = Initialisation.Value;

    /// <summary>
    /// Builds an element out of a factory, and refuses to benchmark anything
    /// when the installation does not have it.
    /// </summary>
    /// <param name="factory">The name of the element factory.</param>
    /// <param name="name">The name the element gets in its pipeline.</param>
    /// <returns>The new element, which the caller owns.</returns>
    /// <exception cref="InvalidOperationException">
    /// The installation has no such element.
    /// </exception>
    public static Element NewElement(string factory, string name) =>
        ElementFactory.Make(factory, name)
        ?? throw new InvalidOperationException(
            $"The installed GStreamer has no '{factory}' element, so the benchmark cannot run.");

    private static bool InitialiseCore()
    {
        gstsharp::GstSharp.Initialize();
        return true;
    }

    private static SubclassType DefineIdentity()
    {
        // The registration talks to GObject, so the library has to be up
        // before the first benchmark class touches this property.
        EnsureInitialised();

        sinkTemplate = NewTemplate("sink", PadDirection.Sink);
        srcTemplate = NewTemplate("src", PadDirection.Src);

        return BaseTransform.DefineSubclass(
            IdentityTypeName,
            ConfigureIdentityClass,
            BaseTransform.TransformIpOverride);
    }

    private static void ConfigureIdentityClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp benchmark identity filter",
            "Filter/Testing",
            "Answers Ok from a managed transform_ip, so the pipeline measures the trampoline",
            "GstSharp.Net benchmarks");

        config.AddPadTemplate(sinkTemplate!);
        config.AddPadTemplate(srcTemplate!);
    }

    private static PadTemplate NewTemplate(string name, PadDirection direction)
    {
        // Any caps: the filter neither looks at the buffers nor changes their
        // format, exactly like the native identity it is compared against.
        using Caps caps = Caps.NewAny();

        return PadTemplate.New(name, direction, PadPresence.Always, caps)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}
