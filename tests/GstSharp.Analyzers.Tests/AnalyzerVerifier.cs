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
    internal static async Task VerifyAsync(params string[] sources)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            // netstandard2.0 is enough for the stubs and resolves from the local
            // package cache, which keeps the tests offline and deterministic.
            ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
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
