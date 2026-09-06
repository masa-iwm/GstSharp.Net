using GstSharp.Generator.Emit;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Where <c>generate</c> puts the skip report.
/// </summary>
/// <remarks>
/// The report is committed next to the gir files it was derived from, and a run
/// that says nothing keeps it there. What used to be impossible was a dry run:
/// <c>--out-dir</c> moved the sources into a scratch tree but the report was
/// written over the committed one all the same. <c>--report-dir</c> is what
/// moves it with them.
/// </remarks>
[Collection(ConsoleCollection.Name)]
public sealed class SkipReportDirectoryTests
{
    [Fact]
    public void GenerateWritesTheReportWhereReportDirSays()
    {
        using GeneratorWorkspace workspace = new();

        // A report of an earlier run, standing in for the committed one.
        string committed = Path.Combine(workspace.GirDirectory, GenerationPipeline.SkipReportFileName);
        const string Sentinel = "# The report of the run before this one.\n";
        File.WriteAllText(committed, Sentinel);

        _ = workspace.Run("generate", out int exitCode, workspace.OutputDirectory);

        Assert.Equal(0, exitCode);
        Assert.Equal(Sentinel, File.ReadAllText(committed), StringComparer.Ordinal);

        string written = Path.Combine(workspace.OutputDirectory, GenerationPipeline.SkipReportFileName);
        Assert.True(File.Exists(written), "The run wrote no report into the directory it was given.");
        Assert.NotEmpty(File.ReadAllText(written));
    }

    [Fact]
    public void WithoutTheOptionTheReportStaysWithTheGirFiles()
    {
        using GeneratorWorkspace workspace = new();

        _ = workspace.Run("generate", out int exitCode);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(
            Path.Combine(workspace.GirDirectory, GenerationPipeline.SkipReportFileName)));
        Assert.False(File.Exists(
            Path.Combine(workspace.OutputDirectory, GenerationPipeline.SkipReportFileName)));
    }
}
