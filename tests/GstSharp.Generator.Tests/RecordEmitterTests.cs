using GstSharp.Generator.Emit;
using GstSharp.Generator.GirParsing;
using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Output shape, field layout and determinism of the record emitter.
/// </summary>
public sealed class RecordEmitterTests
{
    /// <summary>
    /// A plain struct that exercises every field projection: an enumeration of
    /// the same module, a <c>glong</c>, a pointer, an alias that ends in a
    /// built-in type, a <c>gboolean</c> and two fixed size arrays, one of them
    /// private to the C implementation.
    /// </summary>
    private const string PlainStructFixture =
        """
            <alias name="ClockTime" c:type="GstClockTime">
              <type name="guint64" c:type="guint64"/>
            </alias>
            <enumeration name="MapFlags" c:type="GstMapFlags">
              <member name="read" value="1" c:identifier="GST_MAP_READ"/>
            </enumeration>
            <record name="MapInfo" c:type="GstMapInfo">
              <doc xml:space="preserve">The result of a map operation.</doc>
              <field name="flags" writable="1">
                <type name="MapFlags" c:type="GstMapFlags"/>
              </field>
              <field name="data" writable="1">
                <type name="gpointer" c:type="gpointer"/>
              </field>
              <field name="size" writable="1">
                <type name="glong" c:type="glong"/>
              </field>
              <field name="timestamp" writable="1">
                <type name="ClockTime" c:type="GstClockTime"/>
              </field>
              <field name="mapped" writable="1">
                <type name="gboolean" c:type="gboolean"/>
              </field>
              <field name="user_data" writable="1">
                <array zero-terminated="0" fixed-size="4">
                  <type name="gpointer" c:type="gpointer"/>
                </array>
              </field>
              <field name="_gst_reserved" readable="0" private="1">
                <array zero-terminated="0" fixed-size="2">
                  <type name="gpointer" c:type="gpointer"/>
                </array>
              </field>
            </record>
        """;

    /// <summary>
    /// A mini object: the classifier recognises it by the <c>GstMiniObject</c>
    /// that its first field embeds by value.
    /// </summary>
    private const string MiniObjectFixture =
        """
            <alias name="ClockTime" c:type="GstClockTime">
              <type name="guint64" c:type="guint64"/>
            </alias>
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
              <field name="refcount" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Buffer" c:type="GstBuffer" glib:type-name="GstBuffer" glib:get-type="gst_buffer_get_type">
              <doc xml:space="preserve">The basic unit of data transfer.</doc>
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
              <field name="pool" writable="1">
                <type name="BufferPool" c:type="GstBufferPool*"/>
              </field>
              <field name="pts" writable="1">
                <doc xml:space="preserve">presentation timestamp of the buffer</doc>
                <type name="ClockTime" c:type="GstClockTime"/>
              </field>
            </record>
        """;

    /// <summary>A boxed type, that is a record with a <c>glib:get-type</c>.</summary>
    private const string BoxedFixture =
        """
            <record name="Segment" c:type="GstSegment" glib:type-name="GstSegment" glib:get-type="gst_segment_get_type">
              <doc xml:space="preserve">A segment of a stream.</doc>
              <field name="rate" writable="1">
                <type name="gdouble" c:type="gdouble"/>
              </field>
            </record>
        """;

    /// <summary>An opaque record, which can only be handled behind a pointer.</summary>
    private const string OpaqueFixture =
        """
            <record name="DebugMessage" c:type="GstDebugMessage" disguised="1" opaque="1">
              <doc xml:space="preserve">A debug message.</doc>
            </record>
        """;

    private static readonly ModuleInfo GstModule = ModuleMap.Find("Gst")!;

    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory, emitRecords: true),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    [Fact]
    public void PlainStructMatchesTheSnapshot()
    {
        string source = EmitFixture(PlainStructFixture, "MapInfo");

        Assert.Equal(
            Snapshot(
                """
                // <auto-generated/>
                // Generated by GstSharp.Generator from Gst-1.0.gir. Do not edit.

                #nullable enable

                using System.Runtime.CompilerServices;
                using System.Runtime.InteropServices;

                namespace Gst;

                /// <summary>The result of a map operation.</summary>
                [StructLayout(LayoutKind.Sequential)]
                public partial struct MapInfo
                {
                    /// <summary>The <c>flags</c> field of <c>GstMapInfo</c>.</summary>
                    public Gst.MapFlags Flags;

                    /// <summary>The <c>data</c> field of <c>GstMapInfo</c>.</summary>
                    public nint Data;

                    /// <summary>The <c>size</c> field of <c>GstMapInfo</c>.</summary>
                    public System.Runtime.InteropServices.CLong Size;

                    /// <summary>The <c>timestamp</c> field of <c>GstMapInfo</c>.</summary>
                    public Gst.ClockTime Timestamp;

                    // <c>gboolean</c> is a 32 bit integer; every non zero value is true.
                    /// <summary>The <c>mapped</c> field of <c>GstMapInfo</c>.</summary>
                    public int Mapped;

                    /// <summary>The <c>user_data</c> field of <c>GstMapInfo</c>.</summary>
                    public UserDataArray UserData;

                    /// <summary>The <c>_gst_reserved</c> field of <c>GstMapInfo</c>.</summary>
                    private GstReservedArray _gstReserved;

                    /// <summary>Inline storage of the 4 elements of the <c>user_data</c> field of <c>GstMapInfo</c>.</summary>
                    [InlineArray(4)]
                    public struct UserDataArray
                    {
                        private nint _element0;
                    }

                    /// <summary>Inline storage of the 2 elements of the <c>_gst_reserved</c> field of <c>GstMapInfo</c>.</summary>
                    [InlineArray(2)]
                    private struct GstReservedArray
                    {
                        private nint _element0;
                    }
                }
                """),
            source,
            StringComparer.Ordinal);
    }

    [Fact]
    public void MiniObjectMatchesTheSnapshot()
    {
        string source = EmitFixture(MiniObjectFixture, "Buffer");

        Assert.Equal(
            Snapshot(
                """
                // <auto-generated/>
                // Generated by GstSharp.Generator from Gst-1.0.gir. Do not edit.

                #nullable enable

                using System.Runtime.InteropServices;

                namespace Gst;

                /// <summary>The basic unit of data transfer.</summary>
                public sealed unsafe partial class Buffer : Gst.MiniObject
                {
                    /// <summary>Wraps a native <c>GstBuffer</c>.</summary>
                    /// <param name="handle">The native instance.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    internal Buffer(nint handle, Gst.Interop.Transfer transfer)
                        : base(handle, transfer)
                    {
                    }

                    /// <summary>presentation timestamp of the buffer</summary>
                    public Gst.ClockTime Pts => new(((BufferRaw*)Handle)->Pts);

                    /// <summary>Wraps a native <c>GstBuffer</c>, mapping the null pointer onto <see langword="null"/>.</summary>
                    /// <param name="handle">The native instance, or <c>0</c>.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    /// <returns>The wrapper, or <see langword="null"/> when <paramref name="handle"/> is <c>0</c>.</returns>
                    internal static Buffer? FromNative(nint handle, Gst.Interop.Transfer transfer) =>
                        handle == 0 ? null : new(handle, transfer);
                }

                /// <summary>The native layout of <c>GstBuffer</c>.</summary>
                /// <remarks>
                /// <para>
                /// The mirror is only ever read through a pointer into memory that GStreamer
                /// owns; it is never allocated, assigned or copied.
                /// </para>
                /// </remarks>
                [StructLayout(LayoutKind.Sequential)]
                internal unsafe struct BufferRaw
                {
                    /// <summary>The <c>mini_object</c> field.</summary>
                    internal Gst.MiniObjectRaw MiniObject;

                    /// <summary>The <c>pool</c> field.</summary>
                    internal nint Pool;

                    /// <summary>The <c>pts</c> field.</summary>
                    internal ulong Pts;
                }
                """),
            source,
            StringComparer.Ordinal);
    }

    [Fact]
    public void BoxedRecordMatchesTheSnapshot()
    {
        string source = EmitFixture(BoxedFixture, "Segment");

        Assert.Equal(
            Snapshot(
                """
                // <auto-generated/>
                // Generated by GstSharp.Generator from Gst-1.0.gir. Do not edit.

                #nullable enable

                using System.Runtime.InteropServices;

                namespace Gst;

                /// <summary>A segment of a stream.</summary>
                public sealed partial class Segment : Gst.GObject.Boxed
                {
                    /// <summary>Wraps a native <c>GstSegment</c>.</summary>
                    /// <param name="handle">The native instance.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    internal Segment(nint handle, Gst.Interop.Transfer transfer)
                        : base(handle, new Gst.GObject.GType(SegmentGetType()), transfer)
                    {
                    }

                    /// <summary>Wraps a native <c>GstSegment</c>, mapping the null pointer onto <see langword="null"/>.</summary>
                    /// <param name="handle">The native instance, or <c>0</c>.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    /// <returns>The wrapper, or <see langword="null"/> when <paramref name="handle"/> is <c>0</c>.</returns>
                    internal static Segment? FromNative(nint handle, Gst.Interop.Transfer transfer) =>
                        handle == 0 ? null : new(handle, transfer);

                    /// <summary>Returns the <c>GType</c> that GObject registered <c>GstSegment</c> under.</summary>
                    /// <returns>The boxed type.</returns>
                    [LibraryImport("Gst", EntryPoint = "gst_segment_get_type")]
                    private static partial nuint SegmentGetType();
                }
                """),
            source,
            StringComparer.Ordinal);
    }

    [Fact]
    public void OpaqueRecordMatchesTheSnapshot()
    {
        string source = EmitFixture(OpaqueFixture, "DebugMessage");

        Assert.Equal(
            Snapshot(
                """
                // <auto-generated/>
                // Generated by GstSharp.Generator from Gst-1.0.gir. Do not edit.

                #nullable enable

                namespace Gst;

                /// <summary>A debug message.</summary>
                public sealed partial class DebugMessage
                {
                    /// <summary>The native instance.</summary>
                    internal nint Handle;

                    /// <summary>Wraps a native <c>GstDebugMessage</c>.</summary>
                    /// <param name="handle">The native instance.</param>
                    internal DebugMessage(nint handle) => Handle = handle;
                }
                """),
            source,
            StringComparer.Ordinal);
    }

    [Fact]
    public void EnumerationsOfAnotherModuleKeepTheirUnderlyingType()
    {
        // GObject is hand written, so its enumerations have no generated C#
        // type that a field could refer to.
        string source = EmitFixture(
            """
                <record name="Slot" c:type="GstSlot">
                  <field name="flags" writable="1">
                    <type name="GObject.SignalFlags" c:type="GSignalFlags"/>
                  </field>
                  <field name="pad" writable="1">
                    <type name="gint" c:type="gint"/>
                  </field>
                </record>
            """,
            "Slot",
            """
                <namespace name="GObject" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
                  <bitfield name="SignalFlags" c:type="GSignalFlags">
                    <member name="run_first" value="1" c:identifier="G_SIGNAL_RUN_FIRST"/>
                  </bitfield>
                </namespace>
            """);

        Assert.Contains(
            "// <c>GSignalFlags</c> is not generated in this module; the field keeps its underlying type.\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains("public int Flags;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalFlags Flags", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldNamedAfterItsStructIsRenamed()
    {
        // A member cannot carry the name of its enclosing type.
        string source = EmitFixture(
            """
                <record name="Meta" c:type="GstMeta">
                  <field name="meta" writable="1">
                    <type name="gint" c:type="gint"/>
                  </field>
                  <field name="flags" writable="1">
                    <type name="gint" c:type="gint"/>
                  </field>
                </record>
            """,
            "Meta");

        Assert.Contains("public int MetaField;", source, StringComparison.Ordinal);
        Assert.Contains("public int Flags;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BufferMirrorsTheNativeFieldSequence()
    {
        string[] fields = MirrorFields(Source("Buffer"), "BufferRaw");

        Assert.Equal(
            [
                "internal Gst.MiniObjectRaw MiniObject;",
                "internal nint Pool;",
                "internal ulong Pts;",
                "internal ulong Dts;",
                "internal ulong Duration;",
                "internal ulong Offset;",
                "internal ulong OffsetEnd;",
            ],
            fields);
    }

    [Fact]
    public void BufferExposesItsPublicScalarFields()
    {
        string source = Source("Buffer");

        Assert.Contains("public sealed unsafe partial class Buffer : Gst.MiniObject\n", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.ClockTime Pts => new(((BufferRaw*)Handle)->Pts);", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.ClockTime Dts => new(((BufferRaw*)Handle)->Dts);", source, StringComparison.Ordinal);
        Assert.Contains(
            "public Gst.ClockTime Duration => new(((BufferRaw*)Handle)->Duration);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("public ulong Offset => ((BufferRaw*)Handle)->Offset;", source, StringComparison.Ordinal);
        Assert.Contains("public ulong OffsetEnd => ((BufferRaw*)Handle)->OffsetEnd;", source, StringComparison.Ordinal);

        // The embedded header and the pool pointer stay behind the mirror.
        Assert.DoesNotContain("Pool =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MiniObject =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MapInfoIsAPlainStructWithInlineArrays()
    {
        string source = Source("MapInfo");

        Assert.Contains("[StructLayout(LayoutKind.Sequential)]\npublic partial struct MapInfo\n", source, StringComparison.Ordinal);
        Assert.Equal(
            [
                "public nint Memory;",
                "public Gst.MapFlags Flags;",
                "public nint Data;",
                "public nuint Size;",
                "public nuint Maxsize;",
                "public UserDataArray UserData;",
                "private GstReservedArray _gstReserved;",
            ],
            StructFields(source));

        Assert.Contains("[InlineArray(4)]\n    public struct UserDataArray\n", source, StringComparison.Ordinal);
        Assert.Contains("[InlineArray(4)]\n    private struct GstReservedArray\n", source, StringComparison.Ordinal);
        Assert.Contains("        private nint _element0;\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleIsWrappedWithoutAMirror()
    {
        string source = Source("Sample");

        Assert.Contains("public sealed partial class Sample : Gst.MiniObject\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("struct SampleRaw", source, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentImportsItsBoxedType()
    {
        string source = Source("Segment");

        Assert.Contains("public sealed partial class Segment : Gst.GObject.Boxed\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "[LibraryImport(\"Gst\", EntryPoint = \"gst_segment_get_type\")]\n    private static partial nuint SegmentGetType();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            ": base(handle, new Gst.GObject.GType(SegmentGetType()), transfer)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheMirrorOfAMessageStopsAtThePrivateTail()
    {
        // GstMessage ends in a GMutex and a GCond. Both are private to the C
        // implementation and neither has a C# spelling of a guaranteed size.
        string source = Source("Message");

        Assert.Equal(
            [
                "internal Gst.MiniObjectRaw MiniObject;",
                "internal Gst.MessageType Type;",
                "internal ulong Timestamp;",
                "internal nint Src;",
                "internal uint Seqnum;",
            ],
            MirrorFields(source, "MessageRaw"));
        Assert.Contains("It stops at the first field that has no portable C# spelling.", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.MessageType Type => ((MessageRaw*)Handle)->Type;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMiniObjectBaseClassIsNotGenerated()
    {
        // Gst.MiniObject is hand written; only the mirror that the derived
        // types embed comes from the generator.
        string source = Source("MiniObject");

        Assert.DoesNotContain("class MiniObject", source, StringComparison.Ordinal);
        Assert.Contains("internal unsafe struct MiniObjectRaw\n", source, StringComparison.Ordinal);
        Assert.Contains("internal nuint Type;", source, StringComparison.Ordinal);
        Assert.Contains("internal nint Copy;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsAreOffByDefault()
    {
        GeneratedFile file = Assert.Single(GenerationPipeline.Run(GirFixture.GirDirectory).Files);

        Assert.Equal("GstSharp.Net/Generated/Enums.cs", file.RelativePath);
    }

    [Fact]
    public void TheRecordCensusIsStable()
    {
        IReadOnlyList<GeneratedFile> files = Generated.Files;

        // One file per emitted record, next to the enumerations. GstVecDeque is
        // introspectable="0", which is why 34 opaque records emit 33 files.
        Assert.Equal(72, files.Count);
        Assert.Equal(11, Count(files, " : Gst.MiniObject\n"));
        Assert.Equal(12, Count(files, " : Gst.GObject.Boxed\n"));
        Assert.Equal(14, Count(files, "\npublic partial struct "));
        Assert.Equal(9, Count(files, "\ninternal unsafe struct "));

        foreach (GeneratedFile file in files)
        {
            Assert.StartsWith("GstSharp.Net/Generated/", file.RelativePath, StringComparison.Ordinal);
        }

        foreach (Diagnostic diagnostic in Generated.Diagnostics)
        {
            Assert.NotEqual("GEN0006", diagnostic.Code);
            Assert.NotEqual("GEN0007", diagnostic.Code);
        }
    }

    [Fact]
    public void EmittingTwiceProducesIdenticalOutput()
    {
        IReadOnlyList<GeneratedFile> first = GenerationPipeline.Run(GirFixture.GirDirectory, emitRecords: true).Files;
        IReadOnlyList<GeneratedFile> second = GenerationPipeline.Run(GirFixture.GirDirectory, emitRecords: true).Files;

        Assert.Equal(
            first.Select(static file => file.RelativePath).ToArray(),
            second.Select(static file => file.RelativePath).ToArray());
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Content, second[i].Content, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void EveryGeneratedFileEndsWithASingleNewline()
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            Assert.DoesNotContain("\r", file.Content, StringComparison.Ordinal);
            Assert.EndsWith("}\n", file.Content, StringComparison.Ordinal);
        }
    }

    private static int Count(IReadOnlyList<GeneratedFile> files, string needle)
    {
        int count = 0;
        foreach (GeneratedFile file in files)
        {
            if (file.Content.Contains(needle, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Normalises a snapshot: the generated files use LF and end with a newline.
    /// </summary>
    /// <param name="snapshot">The expected source text.</param>
    /// <returns>The normalised text.</returns>
    private static string Snapshot(string snapshot) =>
        snapshot.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static string Source(string typeName)
    {
        string path = "GstSharp.Net/Generated/" + typeName + ".cs";
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException($"No file was generated for '{typeName}'.");
    }

    /// <summary>Returns the field declarations of a generated mirror, in order.</summary>
    /// <param name="source">The generated source text.</param>
    /// <param name="typeName">The name of the mirror.</param>
    /// <returns>The trimmed field declarations.</returns>
    private static string[] MirrorFields(string source, string typeName)
    {
        int start = source.IndexOf("internal unsafe struct " + typeName + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No mirror named '{typeName}' was generated.");

        List<string> fields = [];
        foreach (string line in source[start..].Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("internal ", StringComparison.Ordinal) && trimmed.EndsWith(';'))
            {
                fields.Add(trimmed);
            }
        }

        return [.. fields];
    }

    /// <summary>Returns the field declarations of a generated struct, in order.</summary>
    /// <param name="source">The generated source text.</param>
    /// <returns>The trimmed field declarations.</returns>
    private static string[] StructFields(string source)
    {
        List<string> fields = [];
        foreach (string line in source.Split('\n'))
        {
            // Members of the struct itself sit at one level of indentation; the
            // fields of the inline storage types it nests sit deeper.
            if (!line.StartsWith("    ", StringComparison.Ordinal) || line.Length <= 4 || line[4] == ' ')
            {
                continue;
            }

            string trimmed = line.Trim();
            if ((trimmed.StartsWith("public ", StringComparison.Ordinal)
                    || trimmed.StartsWith("private ", StringComparison.Ordinal))
                && trimmed.EndsWith(';'))
            {
                fields.Add(trimmed);
            }
        }

        return [.. fields];
    }

    private static string EmitFixture(string body, string recordName, string? extraNamespace = null)
    {
        GirRepository file = GirReader.ReadXml(
            $"""
            <repository xmlns="http://www.gtk.org/introspection/core/1.0" xmlns:c="http://www.gtk.org/introspection/c/1.0" xmlns:glib="http://www.gtk.org/introspection/glib/1.0" version="1.2">
              <namespace name="Gst" version="1.0" c:identifier-prefixes="Gst" c:symbol-prefixes="gst">
            {body}
              </namespace>
            {extraNamespace ?? string.Empty}
            </repository>
            """,
            "fixture.gir");

        Repository repository = Repository.FromRepositories([file]);
        GirNamespace ns = repository.FindNamespace("Gst")
            ?? throw new InvalidOperationException("The fixture declares no Gst namespace.");
        DiagnosticBag diagnostics = new();
        NameMapper names = new(Overlays.Empty);
        Classifier classifier = new(repository, diagnostics);
        TypeMap types = new(repository, classifier, names, diagnostics);
        RecordEmitter emitter = new(repository, classifier, names, types, Overlays.Empty, diagnostics);

        GirRecord record = ns.Records.Single(
            candidate => string.Equals(candidate.Name, recordName, StringComparison.Ordinal));
        GeneratedFile generated = emitter.Emit(GstModule, ns, record)
            ?? throw new InvalidOperationException("The record produced no output.");

        Assert.Empty(diagnostics.Items);
        return generated.Content;
    }
}
