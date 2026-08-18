using Gst;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The guard that turns a plugin set a leg promised, and then lost, into a red
/// suite.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequiresElementFactAttribute"/> skips a test whose elements are
/// not installed, which is the only sane behavior for a matrix in which no
/// installation carries every plugin. It has one blind spot: a test that skips
/// on <em>every</em> leg guards nothing, and says so nowhere except in a line of
/// a test report nobody reads. The WebRTC tests sat in exactly that position
/// for as long as every leg installed the libraries of the bad plugins without
/// the plugins themselves.
/// </para>
/// <para>
/// This is the other half of the arrangement. A leg that installs a plugin set
/// on purpose names the elements it is installing it for in
/// <c>GSTSHARP_REQUIRED_ELEMENTS</c>, as a comma separated list of factory
/// names, and this test holds it to that promise: an element that disappears
/// from the package it used to come from fails here, instead of quietly turning
/// the tests that need it back into skips. Where the variable is unset or empty
/// — every leg that promises nothing, and every development machine — the test
/// passes without asserting anything, which is the whole point: only the leg
/// that claims to be the provider is measured against the claim.
/// </para>
/// <para>
/// The variable gates nothing else. It is a statement about the installation
/// rather than a switch: no test reads it to decide what to run, and the skip
/// mechanism of <see cref="RequiresElementFactAttribute"/> is deliberately left
/// as it is.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RequiredElementsTests
{
    /// <summary>The variable a leg states its promise in.</summary>
    private const string VariableName = "GSTSHARP_REQUIRED_ELEMENTS";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RequiredElementsTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Every element factory the environment promises is in the registry.
    /// </summary>
    [Fact]
    public void EveryPromisedElementIsInTheRegistry()
    {
        string[] required = (Environment.GetEnvironmentVariable(VariableName) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (required.Length == 0)
        {
            _output.WriteLine($"{VariableName} is unset or empty: this installation promises nothing.");
            return;
        }

        List<string> missing = [];

        foreach (string name in required)
        {
            // The factory is a registry singleton behind an interned GObject
            // wrapper, and disposing that wrapper would give up the process's
            // part in the entry's lifetime for every holder at once
            // (docs/ownership.md, "GObject wrappers") — the same reason
            // RequiresElementFactAttribute leaves its lookups undisposed.
            ElementFactory? factory = ElementFactory.Find(name);

            if (factory is null)
            {
                missing.Add(name);
            }
            else
            {
                // The plugin is the interesting half of the answer: it names the
                // package the leg is actually getting the element from.
                _output.WriteLine($"{name}: from the \"{factory.GetPluginName() ?? "unknown"}\" plugin");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{VariableName} promises \"{string.Join("\", \"", required)}\", " +
            $"and the installed GStreamer has no \"{string.Join("\", no \"", missing)}\".");
    }
}
