using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The type registry has to know the types of every binding assembly after
/// <c>GstSharp.Initialize</c>, whether or not the application ever called
/// into them.
/// </summary>
/// <remarks>
/// <para>
/// These are the shapes the application writes: a cast and a type test, after
/// nothing but <c>GstSharp.Initialize()</c> — which is all the fixture of this
/// collection calls. None of them calls into an extension module, because
/// <c>GstApp.Initialize()</c> or any other member of one would register the
/// module by hand and prove nothing.
/// </para>
/// <para>
/// They are end to end tests rather than tests of the module sweep: naming a
/// type in a cast makes the runtime prepare it, and preparing it runs the
/// module initialiser of its assembly, so each of these would also pass on a
/// runtime that leaves everything to that. What pins the sweep down is
/// <see cref="ModuleRegistrationTests"/>, which names no module type at all.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class TypeRegistryTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public TypeRegistryTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The failure this is about: <c>GetByName(...) as AppSink</c> coming back
    /// null, with no error anywhere, because <c>GstSharp.Net.App</c> was never
    /// called into.
    /// </summary>
    [Fact]
    public void AppSinkIsRecognizedAfterTheGlobalInitializeAlone()
    {
        using Element? element = ElementFactory.Make("appsink", "recognized");
        Assert.IsType<Gst.App.AppSink>(element);
    }

    /// <summary>
    /// The same for the module that carries no element of its own and is only
    /// ever seen as a base type: <c>filesrc</c> is a <c>GstBaseSrc</c>, and
    /// <c>message.Src is BaseSrc</c> is the shape the application uses.
    /// </summary>
    [Fact]
    public void BaseSrcIsRecognizedAfterTheGlobalInitializeAlone()
    {
        using Element? element = ElementFactory.Make("filesrc", "recognized-base");
        Assert.IsAssignableFrom<Gst.Base.BaseSrc>(element);
    }

    /// <summary>
    /// And for the two modules that nothing else in this suite touches at all,
    /// which is what makes them the honest test of the sweep.
    /// </summary>
    [Fact]
    public void VideoAndAudioTypesAreRecognizedAfterTheGlobalInitializeAlone()
    {
        using Element? converter = ElementFactory.Make("videoconvert", "recognized-video");
        Assert.IsAssignableFrom<Gst.Video.VideoFilter>(converter);

        using Element? volume = ElementFactory.Make("volume", "recognized-audio");
        Assert.IsAssignableFrom<Gst.Audio.AudioFilter>(volume);
    }

    /// <summary>
    /// Wrapping an object as one of its base types is normal — no binding wraps
    /// the type of every element a plugin implements — and it is also the shape
    /// of the silent failure above. The diagnostic reports it, once per type,
    /// and only while somebody is listening.
    /// </summary>
    [Fact]
    public void TheFallbackDiagnosticReportsWhatWasWrappedAsWhat()
    {
        List<TypeFallback> reported = [];
        void OnFallback(TypeFallback fallback)
        {
            lock (reported)
            {
                reported.Add(fallback);
            }
        }

        TypeRegistry.Fallback += OnFallback;

        try
        {
            using Element? element = ElementFactory.Make("capsfilter", "reported");
            Assert.NotNull(element);
        }
        finally
        {
            TypeRegistry.Fallback -= OnFallback;
        }

        foreach (TypeFallback fallback in reported)
        {
            _output.WriteLine($"{fallback.InstanceType.Name} was wrapped as {fallback.WrapperType.Name}");
        }

        TypeFallback capsFilter = Assert.Single(
            reported,
            fallback => string.Equals(fallback.InstanceType.Name, "GstCapsFilter", StringComparison.Ordinal));

        // GstCapsFilter is a GstBaseTransform, which the GstBase module does
        // wrap, so that is the wrapper the lookup settles on.
        Assert.Equal("GstBaseTransform", capsFilter.WrapperType.Name);
    }
}
