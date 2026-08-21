using GstSharp.Generator.Emit;
using GstSharp.Generator.GirParsing;
using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// One generator run over a gir that a test wrote by hand.
/// </summary>
/// <param name="Result">Everything the run produced.</param>
internal sealed record FixtureRun(GenerationResult Result)
{
    /// <summary>Gets the content of one generated file of the <c>Gst</c> module.</summary>
    /// <param name="name">The file name below <c>Generated</c>, for example <c>Element.cs</c>.</param>
    /// <returns>The generated source text.</returns>
    internal string File(string name)
    {
        string path = "GstSharp.Net/Generated/" + name;
        foreach (GeneratedFile file in Result.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException(
            $"The run produced no '{path}'. It produced: {string.Join(", ", Result.Files.Select(static f => f.RelativePath))}.");
    }

    /// <summary>Tests whether the run produced a file.</summary>
    /// <param name="name">The file name below <c>Generated</c>.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    internal bool HasFile(string name) =>
        Result.Files.Any(file => file.RelativePath.EndsWith("/" + name, StringComparison.Ordinal));

    /// <summary>Gets the body of one generated member, without its documentation.</summary>
    /// <param name="fileName">The file to read.</param>
    /// <param name="signature">The start of the member declaration.</param>
    /// <returns>The declaration and its body, trimmed of the leading indentation.</returns>
    internal string Member(string fileName, string signature)
    {
        string[] lines = File(fileName).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith(signature, StringComparison.Ordinal))
            {
                continue;
            }

            int indent = lines[i].Length - lines[i].TrimStart().Length;
            List<string> body = [lines[i][indent..]];
            for (int j = i + 1; j < lines.Length; j++)
            {
                string line = lines[j];
                if (line.Length == 0)
                {
                    body.Add(line);
                    continue;
                }

                body.Add(line.Length > indent ? line[indent..] : line);
                if (line.Length > indent && line[indent] == '}')
                {
                    break;
                }

                if (line.TrimEnd().EndsWith(';') && body.Count == 2)
                {
                    break;
                }
            }

            return string.Join("\n", body);
        }

        throw new InvalidOperationException($"'{fileName}' declares no member starting with '{signature}'.");
    }
}

/// <summary>
/// Runs the whole pipeline over a gir namespace that a test wrote by hand.
/// </summary>
/// <remarks>
/// The fixtures go through <see cref="GenerationPipeline.Execute"/> rather than
/// through a single emitter, so that the wiring of the emitters is exercised
/// too. A fixture always declares the <c>GObject</c> namespace, because every
/// class of the <c>Gst</c> namespace ends up deriving from
/// <c>GObject.InitiallyUnowned</c>.
/// </remarks>
internal static class Fixture
{
    /// <summary>
    /// The <c>GObject</c> namespace that every class fixture needs. The
    /// <c>Value</c> record is declared so that a fixture may reference
    /// <c>GObject.Value</c>, which the type map projects onto the hand written
    /// runtime struct, and the <c>ValueArray</c> record so that one may
    /// reference <c>GObject.ValueArray</c>, whose boxed registration routes it
    /// through the runtime type registry of the planner; neither record is
    /// ever emitted, because the GObject module is not generated.
    /// </summary>
    internal const string GObjectNamespace =
        """
          <namespace name="GObject" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <class name="Object" c:type="GObject" glib:type-name="GObject" glib:get-type="g_object_get_type">
            </class>
            <class name="InitiallyUnowned" c:type="GInitiallyUnowned" parent="Object" glib:type-name="GInitiallyUnowned" glib:get-type="g_initially_unowned_get_type">
            </class>
            <record name="Value" c:type="GValue">
              <field name="g_type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="ValueArray" c:type="GValueArray" glib:type-name="GValueArray" glib:get-type="g_value_array_get_type">
              <field name="n_values" writable="1">
                <type name="guint" c:type="guint"/>
              </field>
            </record>
          </namespace>
        """;

    /// <summary>Runs the generator over a hand written <c>Gst</c> namespace.</summary>
    /// <param name="body">The members of the <c>Gst</c> namespace.</param>
    /// <param name="overlays">The corrections to apply, if any.</param>
    /// <param name="extraNamespaces">
    /// Whole <c>namespace</c> elements to declare beside <c>Gst</c> and
    /// <c>GObject</c>, for a fixture that has to resolve types of another
    /// module.
    /// </param>
    /// <returns>The run.</returns>
    internal static FixtureRun Run(string body, Overlays? overlays = null, string? extraNamespaces = null)
    {
        GirRepository file = GirReader.ReadXml(
            $"""
            <repository xmlns="http://www.gtk.org/introspection/core/1.0" xmlns:c="http://www.gtk.org/introspection/c/1.0" xmlns:glib="http://www.gtk.org/introspection/glib/1.0" version="1.2">
              <namespace name="Gst" version="1.0" c:identifier-prefixes="Gst" c:symbol-prefixes="gst">
            {body}
              </namespace>
            {GObjectNamespace}
            {extraNamespaces}
            </repository>
            """,
            "fixture.gir");

        Repository repository = Repository.FromRepositories([file]);
        GenerationResult result = GenerationPipeline.Execute(repository, overlays ?? Overlays.Empty);

        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            Assert.NotEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        }

        return new FixtureRun(result);
    }
}
