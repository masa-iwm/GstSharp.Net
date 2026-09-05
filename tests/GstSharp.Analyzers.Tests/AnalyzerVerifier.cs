using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace GstSharp.Analyzers.Tests;

/// <summary>
/// Runs one analyzer over a snippet plus the GstSharp stubs.
/// </summary>
/// <typeparam name="TAnalyzer">The analyzer under test.</typeparam>
internal static class AnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    /// <summary>
    /// Compiles <paramref name="sources"/> together with the stubs and asserts
    /// that the analyzer reports exactly the diagnostics the markup declares.
    /// </summary>
    /// <param name="sources">The snippets, with <c>{|GST0001:...|}</c> style markup.</param>
    /// <returns>A task that completes when the verification is done.</returns>
    internal static Task VerifyAsync(params string[] sources) =>
        // netstandard2.0 is enough for most of the stubs and resolves from the
        // local package cache once it is there. Not every test can use it: a
        // stub with a static abstract interface member does not compile against
        // netstandard2.0 reference assemblies and asks for the Net80 set
        // instead, which the first run of a clean machine fetches from
        // nuget.org like any other package. Neither set is downloaded again.
        VerifyAsync(ReferenceAssemblies.NetStandard.NetStandard20, sources);

    /// <summary>
    /// Compiles <paramref name="sources"/> together with the stubs against a
    /// given set of reference assemblies.
    /// </summary>
    /// <param name="referenceAssemblies">The framework to compile against.</param>
    /// <param name="sources">The snippets, with <c>{|GST0001:...|}</c> style markup.</param>
    /// <returns>A task that completes when the verification is done.</returns>
    internal static async Task VerifyAsync(ReferenceAssemblies referenceAssemblies, params string[] sources)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = referenceAssemblies,
            TestState =
            {
                Sources = { GstStubs.Source },
            },
        };

        foreach (string source in sources)
        {
            test.TestState.Sources.Add(source);
        }

        await test.RunAsync();
    }
}
