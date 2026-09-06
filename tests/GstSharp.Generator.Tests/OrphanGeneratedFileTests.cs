using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The collection of the test classes that drive a verb through the console.
/// </summary>
/// <remarks>
/// Reading what a verb printed means replacing <see cref="Console.Out"/> and
/// <see cref="Console.Error"/>, which belong to the process and not to the
/// test: any other class running beside this one would write into the capture,
/// or into a writer that has already been put back. The collection is
/// therefore not run in parallel with the rest of the assembly.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    /// <summary>The name the collection is referred to by.</summary>
    internal const string Name = "Console";
}

/// <summary>
/// What the command line verbs do with a committed source of a generated
/// directory that the run no longer produces.
/// </summary>
/// <remarks>
/// The verbs used to look only at the files of the run: one that stopped being
/// generated stayed in the tree, kept compiling and kept shipping, which is a
/// binding nobody decided to keep. <c>generate</c> therefore deletes it and
/// says so, and <c>verify</c> fails on it like any other difference.
/// </remarks>
[Collection(ConsoleCollection.Name)]
public sealed class OrphanGeneratedFileTests
{
    [Fact]
    public void GenerateDeletesAFileItNoLongerProduces()
    {
        using GeneratorWorkspace workspace = new();
        string stray = workspace.Plant("Widget.Removed.cs");

        string output = workspace.Run("generate", out int exitCode);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(stray), "The orphan was left behind.");
        Assert.Contains(
            "Deleted the orphan generated file 'GstSharp.Net/Generated/Widget.Removed.cs'.",
            output,
            StringComparison.Ordinal);

        // Only the orphan goes: everything the run wrote is still there.
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "GstSharp.Net", "Generated", "Widget.cs")));
    }

    [Fact]
    public void VerifyFailsOnAFileTheRunNoLongerProduces()
    {
        using GeneratorWorkspace workspace = new();
        workspace.Run("generate", out _);
        string stray = workspace.Plant("Widget.Removed.cs");

        string output = workspace.Run("verify", out int exitCode);

        Assert.NotEqual(0, exitCode);
        Assert.True(File.Exists(stray), "verify must report the orphan, not delete it.");
        Assert.Contains(
            "orphan generated file: GstSharp.Net/Generated/Widget.Removed.cs",
            output,
            StringComparison.Ordinal);
    }
}
