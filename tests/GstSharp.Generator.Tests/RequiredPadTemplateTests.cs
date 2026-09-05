using GstSharp.Generator.Emit;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The pad templates a base class needs travel into the registration, so the
/// check runs inside <c>class_init</c> instead of after it returned.
/// </summary>
/// <remarks>
/// The emitted argument is what carries the names of the templates from the
/// per class facts of the generator into <c>SubclassType.Define</c>; a class
/// that loses it would register a type whose instances cannot build their pads
/// and fail deep inside <c>g_object_new</c> instead.
/// </remarks>
public sealed class RequiredPadTemplateTests
{
    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    /// <summary>
    /// A class that needs one template names it, in the order the base class
    /// creates its pads in.
    /// </summary>
    [Fact]
    public void TheRegistrationOfBaseSrcRequiresTheSourceTemplate()
    {
        string source = File("GstSharp.Net.Base/Generated/Subclassing/BaseSrc.Subclass.cs");

        Assert.Contains("requiredPadTemplates: new[] { \"src\" });", source, StringComparison.Ordinal);
    }

    /// <summary>A class that needs two names both, in gir order.</summary>
    [Fact]
    public void TheRegistrationOfBaseTransformRequiresBothTemplates()
    {
        string source = File("GstSharp.Net.Base/Generated/Subclassing/BaseTransform.Subclass.cs");

        Assert.Contains("requiredPadTemplates: new[] { \"sink\", \"src\" });", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// No file checks a template after the registration returned any more: the
    /// post-registration helper is gone, and a call to it would not compile.
    /// </summary>
    [Fact]
    public void NoFileChecksAPadTemplateAfterTheRegistration()
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            Assert.DoesNotContain("RequirePadTemplate", file.Content, StringComparison.Ordinal);
        }
    }

    private static string File(string relativePath)
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, relativePath, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException($"The run produced no '{relativePath}'.");
    }
}
