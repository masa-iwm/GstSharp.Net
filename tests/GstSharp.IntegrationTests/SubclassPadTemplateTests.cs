using Gst.Base;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A class initialiser that does not add the pad template its base class needs
/// fails the definition, from inside <c>class_init</c>.
/// </summary>
/// <remarks>
/// The type name is burnt either way — a static <c>GType</c> cannot be
/// unregistered — so what these tests pin is that the failure is reported as
/// the failure of the definition, that the managed side never publishes the
/// broken type, and that the retry says which failure took the name.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SubclassPadTemplateTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassPadTemplateTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A source whose class initialiser adds nothing fails with the missing
    /// <c>src</c> template, and the retry under the same name says so.
    /// </summary>
    /// <remarks>
    /// The two halves share one test because the second depends on the name the
    /// first burnt.
    /// </remarks>
    [Fact]
    public void ASourceWithoutItsPadTemplateFailsTheDefinitionAndKeepsTheName()
    {
        const string TypeName = "GstSharpTestSrcWithoutPadTemplate";

        Assert.False(GType.FromName(TypeName).IsValid);

        List<Exception> reported = [];
        InvalidOperationException error = DefineAndCollect(
            reported,
            () => Assert.Throws<InvalidOperationException>(
                () => BaseSrc.DefineSubclass(TypeName, static _ => { })));

        _output.WriteLine(error.Message);
        Assert.Contains("class initialiser", error.Message, StringComparison.Ordinal);

        Exception failure = Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("\"src\" pad template", failure.Message, StringComparison.Ordinal);

        // The trampoline reports what it caught as well, so the trap saw the
        // same failure and nothing else.
        Exception trapped = Assert.Single(reported);
        Assert.Same(failure, trapped);

        // The type exists - class_init ran - but nothing managed answers for
        // it, so an instance of it would never be wrapped as the subclass.
        GType type = GType.FromName(TypeName);
        Assert.True(type.IsValid);
        Assert.Null(SubclassRegistry.Find(type));

        // Retrying the same name cannot re-register it, and says why it is
        // taken instead of blaming the caller for a duplicate definition.
        InvalidOperationException retry = DefineAndCollect(
            reported,
            () => Assert.Throws<InvalidOperationException>(
                () => BaseSrc.DefineSubclass(TypeName, static _ => { })));

        _output.WriteLine(retry.Message);
        Assert.Contains("is taken already", retry.Message, StringComparison.Ordinal);
        Assert.Contains(
            "A previous DefineSubclass with this name failed in its class initialiser:",
            retry.Message,
            StringComparison.Ordinal);
        Assert.Contains("\"src\" pad template", retry.Message, StringComparison.Ordinal);

        // The retry never reached class_init, so it reported nothing.
        Assert.Single(reported);
    }

    /// <summary>
    /// The generic overload fails the same way, and the failed type is not
    /// resolved to the managed subclass afterwards.
    /// </summary>
    [Fact]
    public void TheGenericOverloadFailsTheSameWayAndPublishesNothing()
    {
        const string TypeName = "GstSharpTestGenericSrcWithoutPadTemplate";

        Assert.False(GType.FromName(TypeName).IsValid);

        List<Exception> reported = [];
        InvalidOperationException error = DefineAndCollect(
            reported,
            () => Assert.Throws<InvalidOperationException>(
                () => BaseSrc.DefineSubclass<ProbeSrcWithoutPadTemplate>(TypeName, static _ => { })));

        _output.WriteLine(error.Message);

        Exception failure = Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("\"src\" pad template", failure.Message, StringComparison.Ordinal);
        Assert.Same(failure, Assert.Single(reported));

        // The wrap factory of the generic overload is registered as the last
        // step of a definition that succeeded, so this one left none behind.
        GType type = GType.FromName(TypeName);
        Assert.True(type.IsValid);
        Assert.Null(SubclassRegistry.Find(type));
    }

    /// <summary>
    /// Runs a definition that is expected to fail with the trap armed, so the
    /// report the trampoline makes is collected instead of reaching the console
    /// or the next test.
    /// </summary>
    /// <param name="reported">The exceptions the trap saw.</param>
    /// <param name="definition">The definition to run.</param>
    /// <returns>What the definition threw.</returns>
    private static InvalidOperationException DefineAndCollect(
        List<Exception> reported,
        Func<InvalidOperationException> definition)
    {
        void OnFailure(Exception exception) => reported.Add(exception);

        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            return definition();
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }
    }
}

/// <summary>
/// A source that is never registered successfully: its class initialiser adds
/// no pad template, which is what
/// <see cref="SubclassPadTemplateTests"/> is about.
/// </summary>
/// <remarks>
/// It exists for the generic overload, which needs a type that states how its
/// wrapper is built. No instance of it is ever created.
/// </remarks>
internal sealed class ProbeSrcWithoutPadTemplate : BaseSrc, IManagedSubclass<ProbeSrcWithoutPadTemplate>
{
    private ProbeSrcWithoutPadTemplate(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">The instance, on its way into the constructor.</param>
    /// <returns>The wrapper.</returns>
    public static ProbeSrcWithoutPadTemplate CreateWrapper(SubclassCtorArgs args) => new(args);
}
