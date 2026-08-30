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
    /// <summary>
    /// A gir holding one bindable class, small enough that a run over it costs
    /// a few milliseconds. The modules it does not declare are reported as
    /// GEN0005 warnings and emit nothing, which is exactly the shape this
    /// needs: one generated directory with a handful of files in it.
    /// </summary>
    private const string Gir =
        $"""
        <repository xmlns="http://www.gtk.org/introspection/core/1.0" xmlns:c="http://www.gtk.org/introspection/c/1.0" xmlns:glib="http://www.gtk.org/introspection/glib/1.0" version="1.2">
          <namespace name="Gst" version="1.0" c:identifier-prefixes="Gst" c:symbol-prefixes="gst">
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="pack" c:identifier="gst_widget_pack">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </class>
          </namespace>
        {Fixture.GObjectNamespace}
        </repository>
        """;

    [Fact]
    public void GenerateDeletesAFileItNoLongerProduces()
    {
        using Workspace workspace = new();
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
        using Workspace workspace = new();
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

    /// <summary>A gir directory and an output directory of their own.</summary>
    private sealed class Workspace : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());

        internal Workspace()
        {
            Directory.CreateDirectory(Path.Combine(_root, "girs", "reference"));
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(_root, "girs", "reference", "Gst-1.0.gir"), Gir);
        }

        internal string OutputDirectory => Path.Combine(_root, "src");

        /// <summary>Writes a source into the generated directory of the module.</summary>
        /// <param name="name">The file name.</param>
        /// <returns>The path it was written to.</returns>
        internal string Plant(string name)
        {
            string directory = Path.Combine(OutputDirectory, "GstSharp.Net", "Generated");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, "// Generated by a run that no longer exists.\n");
            return path;
        }

        /// <summary>Runs one verb over the workspace.</summary>
        /// <param name="verb">The verb to run.</param>
        /// <param name="exitCode">Receives the exit code.</param>
        /// <returns>Everything the run wrote to the console.</returns>
        internal string Run(string verb, out int exitCode)
        {
            TextWriter previousOut = Console.Out;
            TextWriter previousError = Console.Error;
            StringWriter captured = new();
            try
            {
                Console.SetOut(captured);
                Console.SetError(captured);
                exitCode = Program.Main(
                    [verb, "--gir-dir", Path.Combine(_root, "girs"), "--out-dir", OutputDirectory]);
            }
            finally
            {
                Console.SetOut(previousOut);
                Console.SetError(previousError);
            }

            return captured.ToString();
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
