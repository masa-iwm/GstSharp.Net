using GstSharp.Generator.Emit;
using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Loads the vendored reference gir files once for the whole test run.
/// </summary>
internal static class GirFixture
{
    private static readonly Lazy<Repository> LazyRepository = new(
        static () => Repository.Load(Path.Combine(GirDirectory, "reference")),
        isThreadSafe: true);

    private static readonly Lazy<Overlays> LazyOverlays = new(
        static () => Overlays.Load(Path.Combine(GirDirectory, "overlays")),
        isThreadSafe: true);

    private static readonly Lazy<string> LazyRoot = new(FindRepositoryRoot, isThreadSafe: true);

    /// <summary>Gets the repository root directory.</summary>
    internal static string RepositoryRoot => LazyRoot.Value;

    /// <summary>Gets the <c>girs</c> directory of the repository.</summary>
    internal static string GirDirectory => Path.Combine(RepositoryRoot, "girs");

    /// <summary>Gets the loaded gir repository.</summary>
    internal static Repository Repository => LazyRepository.Value;

    /// <summary>Gets the loaded overlays.</summary>
    internal static Overlays Overlays => LazyOverlays.Value;

    /// <summary>
    /// Runs the generator over the vendored gir files and refuses a run that
    /// reported an error.
    /// </summary>
    /// <returns>The result of the run.</returns>
    /// <remarks>
    /// The command line reports the diagnostics and stops before it writes or
    /// compares anything when one of them is an error, and a fixture run does
    /// the same unless the test asked for the errors. A census counting a
    /// result nobody would have accepted is a census of members that do not
    /// ship, so the tests reading the real corpus in memory take the same gate.
    /// </remarks>
    internal static GenerationResult RunWithoutErrors()
    {
        GenerationResult result = GenerationPipeline.Run(GirDirectory);
        Assert.All(
            result.Diagnostics,
            static diagnostic => Assert.NotEqual(DiagnosticSeverity.Error, diagnostic.Severity));
        return result;
    }

    /// <summary>Gets a loaded namespace by name.</summary>
    /// <param name="name">The gir namespace name.</param>
    /// <returns>The namespace.</returns>
    internal static GirNamespace Namespace(string name) =>
        Repository.FindNamespace(name)
        ?? throw new InvalidOperationException($"The gir namespace '{name}' was not loaded.");

    /// <summary>Gets a symbol of a loaded namespace.</summary>
    /// <param name="qualifiedName">The qualified gir name, for example <c>Gst.Buffer</c>.</param>
    /// <returns>The symbol.</returns>
    internal static GirSymbol Symbol(string qualifiedName) =>
        Repository.Resolve(qualifiedName, context: null)
        ?? throw new InvalidOperationException($"The gir symbol '{qualifiedName}' was not found.");

    /// <summary>Finds a callable by its <c>c:identifier</c>.</summary>
    /// <param name="cIdentifier">The C entry point name.</param>
    /// <returns>The callable.</returns>
    internal static GirCallable Callable(string cIdentifier)
    {
        foreach (GirNamespace ns in Repository.Namespaces)
        {
            foreach (GirCallable callable in EnumerateCallables(ns))
            {
                if (string.Equals(callable.CIdentifier, cIdentifier, StringComparison.Ordinal))
                {
                    return callable;
                }
            }
        }

        throw new InvalidOperationException($"No callable with the c:identifier '{cIdentifier}' was found.");
    }

    private static IEnumerable<GirCallable> EnumerateCallables(GirNamespace ns)
    {
        foreach (GirFunction function in ns.Functions)
        {
            yield return function;
        }

        foreach (GirTypeDeclaration declaration in Declarations(ns))
        {
            foreach (GirFunction constructor in declaration.Constructors)
            {
                yield return constructor;
            }

            foreach (GirFunction method in declaration.Methods)
            {
                yield return method;
            }

            foreach (GirFunction function in declaration.Functions)
            {
                yield return function;
            }
        }
    }

    private static IEnumerable<GirTypeDeclaration> Declarations(GirNamespace ns)
    {
        foreach (GirClass declaration in ns.Classes)
        {
            yield return declaration;
        }

        foreach (GirRecord declaration in ns.Records)
        {
            yield return declaration;
        }

        foreach (GirInterface declaration in ns.Interfaces)
        {
            yield return declaration;
        }

        foreach (GirUnion declaration in ns.Unions)
        {
            yield return declaration;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "girs", "reference", "Gst-1.0.gir")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be located from " + AppContext.BaseDirectory + ".");
    }
}
