using GstSharp.Generator.Emit;
using GstSharp.Generator.GirParsing;
using GstSharp.Generator.Planning;
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

    /// <summary>
    /// An opaque record that declares fields: the header of the metadata family
    /// is the shape, a flags word and a pointer to a description of the item.
    /// </summary>
    private const string OpaqueFieldsFixture =
        """
            <bitfield name="MetaFlags" c:type="GstMetaFlags">
              <member name="none" value="0" c:identifier="GST_META_FLAG_NONE"/>
            </bitfield>
            <record name="Meta" c:type="GstMeta" opaque="1">
              <doc xml:space="preserve">Extra data attached to a buffer.</doc>
              <field name="flags" writable="1">
                <doc xml:space="preserve">extra flags for the metadata</doc>
                <type name="MetaFlags" c:type="GstMetaFlags"/>
              </field>
              <field name="info" writable="1">
                <type name="MetaInfo" c:type="const GstMetaInfo*"/>
              </field>
            </record>
        """;

    /// <summary>
    /// A record whose gir declares a union, and one that embeds it by value.
    /// The union stops the layout of the first, which leaves it a prefix, and a
    /// prefix cannot be embedded, so the second is not laid out at all.
    /// </summary>
    private const string UnionFixture =
        """
            <record name="VideoInfo" c:type="GstVideoInfo" opaque="1">
              <field name="width" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <union name="ABI" c:type="ABI">
                <field name="flags" writable="1">
                  <type name="gint" c:type="gint"/>
                </field>
              </union>
              <field name="views" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="VideoFrame" c:type="GstVideoFrame" opaque="1">
              <field name="info" writable="1">
                <type name="VideoInfo" c:type="GstVideoInfo"/>
              </field>
              <field name="id" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
        """;

    /// <summary>
    /// A record that embeds an opaque record whose own layout is complete, which
    /// is the shape of every <c>*Meta</c> of the girs.
    /// </summary>
    private const string EmbeddedRecordFixture =
        """
            <bitfield name="MetaFlags" c:type="GstMetaFlags">
              <member name="none" value="0" c:identifier="GST_META_FLAG_NONE"/>
            </bitfield>
            <record name="Meta" c:type="GstMeta" opaque="1">
              <field name="flags" writable="1">
                <type name="MetaFlags" c:type="GstMetaFlags"/>
              </field>
              <field name="info" writable="1">
                <type name="MetaInfo" c:type="const GstMetaInfo*"/>
              </field>
            </record>
            <record name="VideoCropMeta" c:type="GstVideoCropMeta" opaque="1">
              <field name="meta" writable="1">
                <type name="Meta" c:type="GstMeta"/>
              </field>
              <field name="x" writable="1">
                <type name="guint" c:type="guint"/>
              </field>
              <field name="height" writable="1" version="1.26">
                <type name="guint" c:type="guint"/>
              </field>
            </record>
        """;

    /// <summary>
    /// Fields that take up space in the mirror but carry no value to read: a
    /// vtable slot, a pointer and a fixed size array.
    /// </summary>
    private const string UnreadableFieldsFixture =
        """
            <record name="Iterator" c:type="GstIterator" opaque="1">
              <field name="copy" writable="1">
                <callback name="copy" c:type="GstIteratorCopyFunction">
                  <return-value transfer-ownership="none">
                    <type name="none" c:type="void"/>
                  </return-value>
                </callback>
              </field>
              <field name="cookie" writable="1">
                <type name="guint" c:type="guint"/>
              </field>
              <field name="master_cookie" writable="1">
                <type name="guint" c:type="guint*"/>
              </field>
              <field name="samples" writable="1">
                <array zero-terminated="0" fixed-size="2">
                  <type name="guint" c:type="guint"/>
                </array>
              </field>
              <field name="size" writable="1">
                <type name="guint" c:type="guint"/>
              </field>
            </record>
        """;

    /// <summary>A method that would carry the name of a field accessor.</summary>
    private const string AccessorCollisionFixture =
        """
            <record name="RTSPUrl" c:type="GstRTSPUrl" opaque="1">
              <field name="port" writable="1">
                <type name="guint16" c:type="guint16"/>
              </field>
              <method name="port" c:identifier="gst_rtsp_url_port">
                <return-value transfer-ownership="none">
                  <type name="gint" c:type="gint"/>
                </return-value>
                <parameters>
                  <instance-parameter name="url" transfer-ownership="none">
                    <type name="RTSPUrl" c:type="GstRTSPUrl*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </record>
        """;

    /// <summary>A field whose accessor would carry a name of the runtime.</summary>
    private const string ReservedAccessorNameFixture =
        """
            <record name="RTSPUrl" c:type="GstRTSPUrl" opaque="1">
              <field name="handle" writable="1">
                <type name="guint" c:type="guint"/>
              </field>
              <field name="port" writable="1">
                <type name="guint16" c:type="guint16"/>
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
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
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
                    public nint DataPtr;

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
                    public Gst.ClockTime Pts
                    {
                        get
                        {
                            Gst.ClockTime value = new(((BufferRaw*)Handle)->Pts);
                            System.GC.KeepAlive(this);
                            return value;
                        }
                    }

                    /// <summary>Wraps a native <c>GstBuffer</c>, mapping the null pointer onto <see langword="null"/>.</summary>
                    /// <param name="handle">The native instance, or <c>0</c>.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    /// <returns>The wrapper, or <see langword="null"/> when <paramref name="handle"/> is <c>0</c>.</returns>
                    internal static Buffer? FromNative(nint handle, Gst.Interop.Transfer transfer) =>
                        handle == 0 ? null : new(handle, transfer);

                    /// <summary>Returns the <c>GType</c> that GObject registered <c>GstBuffer</c> under.</summary>
                    /// <returns>The type of the instances of this wrapper.</returns>
                    [LibraryImport("Gst", EntryPoint = "gst_buffer_get_type")]
                    internal static partial nuint GetGType();

                    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
                    /// <param name="handle">The native instance.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    /// <returns>The new wrapper.</returns>
                    internal static object CreateWrapper(nint handle, Gst.Interop.Transfer transfer) => new Buffer(handle, transfer);
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
                public sealed unsafe partial class Segment : Gst.GObject.Boxed
                {
                    /// <summary>Wraps a native <c>GstSegment</c>.</summary>
                    /// <param name="handle">The native instance.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    internal Segment(nint handle, Gst.Interop.Transfer transfer)
                        : base(handle, new Gst.GObject.GType(GetGType()), transfer)
                    {
                    }

                    /// <summary>The <c>rate</c> field of <c>GstSegment</c>.</summary>
                    public double Rate
                    {
                        get
                        {
                            double value = ((SegmentRaw*)Handle)->Rate;
                            System.GC.KeepAlive(this);
                            return value;
                        }
                    }

                    /// <summary>Wraps a native <c>GstSegment</c>, mapping the null pointer onto <see langword="null"/>.</summary>
                    /// <param name="handle">The native instance, or <c>0</c>.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    /// <returns>The wrapper, or <see langword="null"/> when <paramref name="handle"/> is <c>0</c>.</returns>
                    internal static Segment? FromNative(nint handle, Gst.Interop.Transfer transfer) =>
                        handle == 0 ? null : new(handle, transfer);

                    /// <summary>Returns the <c>GType</c> that GObject registered <c>GstSegment</c> under.</summary>
                    /// <returns>The type of the instances of this wrapper.</returns>
                    [LibraryImport("Gst", EntryPoint = "gst_segment_get_type")]
                    internal static partial nuint GetGType();

                    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
                    /// <param name="handle">The native instance.</param>
                    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
                    /// <returns>The new wrapper.</returns>
                    internal static object CreateWrapper(nint handle, Gst.Interop.Transfer transfer) => new Segment(handle, transfer);
                }

                /// <summary>The native layout of <c>GstSegment</c>.</summary>
                /// <remarks>
                /// <para>
                /// The mirror is only ever read through a pointer into memory that GStreamer
                /// owns; it is never allocated, assigned or copied.
                /// </para>
                /// </remarks>
                [StructLayout(LayoutKind.Sequential)]
                internal unsafe struct SegmentRaw
                {
                    /// <summary>The <c>rate</c> field.</summary>
                    internal double Rate;
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

                    /// <summary>Wraps a native <c>GstDebugMessage</c>, mapping the null pointer onto <see langword="null"/>.</summary>
                    /// <param name="handle">The native instance, or <c>0</c>.</param>
                    /// <returns>The wrapper, or <see langword="null"/> when <paramref name="handle"/> is <c>0</c>.</returns>
                    /// <remarks>
                    /// The wrapper of an opaque record is a bare pointer holder: the gir
                    /// describes no way of releasing one, so it does not take part in the
                    /// ownership of what it points at.
                    /// </remarks>
                    internal static DebugMessage? FromNative(nint handle) =>
                        handle == 0 ? null : new(handle);
                }
                """),
            source,
            StringComparer.Ordinal);
    }

    [Fact]
    public void OpaqueRecordWithFieldsMatchesTheSnapshot()
    {
        string source = EmitFixture(OpaqueFieldsFixture, "Meta");

        Assert.Equal(
            Snapshot(
                """
                // <auto-generated/>
                // Generated by GstSharp.Generator from Gst-1.0.gir. Do not edit.

                #nullable enable

                using System.Runtime.InteropServices;

                namespace Gst;

                /// <summary>Extra data attached to a buffer.</summary>
                public sealed unsafe partial class Meta
                {
                    /// <summary>The native instance.</summary>
                    internal nint Handle;

                    /// <summary>Wraps a native <c>GstMeta</c>.</summary>
                    /// <param name="handle">The native instance.</param>
                    internal Meta(nint handle) => Handle = handle;

                    /// <summary>extra flags for the metadata</summary>
                    public Gst.MetaFlags Flags
                    {
                        get
                        {
                            Gst.MetaFlags value = ((MetaRaw*)Handle)->Flags;
                            System.GC.KeepAlive(this);
                            return value;
                        }
                    }

                    /// <summary>Wraps a native <c>GstMeta</c>, mapping the null pointer onto <see langword="null"/>.</summary>
                    /// <param name="handle">The native instance, or <c>0</c>.</param>
                    /// <returns>The wrapper, or <see langword="null"/> when <paramref name="handle"/> is <c>0</c>.</returns>
                    /// <remarks>
                    /// The wrapper of an opaque record is a bare pointer holder: the gir
                    /// describes no way of releasing one, so it does not take part in the
                    /// ownership of what it points at.
                    /// </remarks>
                    internal static Meta? FromNative(nint handle) =>
                        handle == 0 ? null : new(handle);
                }

                /// <summary>The native layout of <c>GstMeta</c>.</summary>
                /// <remarks>
                /// <para>
                /// The mirror is only ever read through a pointer into memory that GStreamer
                /// owns; it is never allocated, assigned or copied.
                /// </para>
                /// </remarks>
                [StructLayout(LayoutKind.Sequential)]
                internal unsafe struct MetaRaw
                {
                    /// <summary>The <c>flags</c> field.</summary>
                    internal Gst.MetaFlags Flags;

                    /// <summary>The <c>info</c> field.</summary>
                    internal nint Info;
                }
                """),
            source,
            StringComparer.Ordinal);
    }

    [Fact]
    public void AUnionStopsTheLayoutWhereItSits()
    {
        // The gir keeps a union out of the field list of the record it is
        // declared in, so every field behind it would land at the wrong offset.
        // The mirror is the prefix in front of the union, and it says so.
        string source = EmitFixture(UnionFixture, "VideoInfo");

        Assert.Equal(["internal int Width;"], MirrorFields(source, "VideoInfoRaw"));
        Assert.Contains(
            "/// Prefix mirror of the C struct: field offsets are exact, <c>sizeof</c> is NOT\n"
            + "/// the C size; never allocate from it.\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains("public int Width\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Views", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordThatEmbedsATruncatedOneIsNotLaidOut()
    {
        // A prefix is shorter than the C structure, so everything behind it in
        // the embedding record would sit at the wrong offset. Nothing is laid
        // out rather than laid out wrongly.
        string source = EmitFixture(UnionFixture, "VideoFrame");

        Assert.DoesNotContain("VideoFrameRaw", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public int Id", source, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class VideoFrame\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordThatEmbedsACompleteOneKeepsTheFieldsBehindIt()
    {
        // The header of the metadata family: a complete opaque record embedded
        // by value, which the mirror spells as the mirror of that record, so
        // the fields behind it keep their offsets and get their accessors.
        string source = EmitFixture(EmbeddedRecordFixture, "VideoCropMeta");

        Assert.Equal(
            [
                "internal Gst.MetaRaw Meta;",
                "internal uint X;",
                "internal uint Height;",
            ],
            MirrorFields(source, "VideoCropMetaRaw"));
        Assert.Contains("uint value = ((VideoCropMetaRaw*)Handle)->X;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Gst.Meta Meta", source, StringComparison.Ordinal);

        // The mirror is complete, so it carries no prefix warning.
        Assert.DoesNotContain("Prefix mirror of the C struct", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldThatArrivedLateSaysWhichGStreamerItNeeds()
    {
        string source = EmitFixture(EmbeddedRecordFixture, "VideoCropMeta");

        Assert.Contains(
            """
                /// <summary>The <c>height</c> field of <c>GstVideoCropMeta</c>.</summary>
                /// <remarks>
                /// <para>Available since GStreamer 1.26.</para>
                /// </remarks>
                public uint Height
            """,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldThatCarriesNoValueIsMirroredWithoutAnAccessor()
    {
        // A vtable slot, a pointer and a fixed size array all take up space in
        // the mirror, and none of them is a value the wrapper can hand out.
        string source = EmitFixture(UnreadableFieldsFixture, "Iterator");

        Assert.Equal(
            [
                "internal nint Copy;",
                "internal uint Cookie;",
                "internal nint MasterCookie;",
                "internal SamplesArray Samples;",
                "internal uint Size;",
            ],
            MirrorFields(source, "IteratorRaw"));

        Assert.Contains("public uint Cookie\n", source, StringComparison.Ordinal);
        Assert.Contains("public uint Size\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public nint Copy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public nint MasterCookie", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public SamplesArray", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodNamedAfterAFieldIsTheOneThatCollides()
    {
        // The accessors claim their names before the callables are planned, so
        // the field keeps the name it is the only binding of, and the method,
        // which a rename in fixups.json can move, is what is reported.
        FixtureEmission emission = EmitFixtureWithDiagnostics(AccessorCollisionFixture, "RTSPUrl");

        Assert.Contains("public ushort Port\n", emission.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("public ushort Port()", emission.Source, StringComparison.Ordinal);
        Assert.Equal(1, emission.Census.SkippedCount("Gst", SkipReason.NameCollision));

        Diagnostic collision = Assert.Single(emission.Diagnostics);
        Assert.Equal("GEN0009", collision.Code);
        Assert.Contains("gst_rtsp_url_port", collision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldNamedAfterARuntimeMemberLosesItsAccessor()
    {
        // Every wrapper carries Handle, so the field cannot have it. The
        // accessor is the side that gives way here, because the runtime member
        // is not something fixups.json can move; a rename of the field is.
        FixtureEmission emission = EmitFixtureWithDiagnostics(ReservedAccessorNameFixture, "RTSPUrl");

        Assert.DoesNotContain("public uint Handle", emission.Source, StringComparison.Ordinal);
        Assert.Contains("internal uint Handle;", emission.Source, StringComparison.Ordinal);
        Assert.Contains("public ushort Port\n", emission.Source, StringComparison.Ordinal);
        Assert.Equal(1, emission.Census.SkippedCount("Gst", SkipReason.NameCollision));

        Diagnostic collision = Assert.Single(emission.Diagnostics);
        Assert.Equal("GEN0018", collision.Code);
        Assert.Contains("'handle' field of 'Gst.RTSPUrl'", collision.Message, StringComparison.Ordinal);
        Assert.Contains("RTSPUrl.Handle", collision.Message, StringComparison.Ordinal);
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
    public void OnlyThePointerFieldsOfAValueTypeCarryThePtrSuffix()
    {
        // A public field of a value projected record that lands on a bare
        // pointer keeps the address under the Ptr suffix, so that the name it
        // derives from the gir stays free for the typed accessor that reads what
        // the address points at. Nothing else moves: a scalar is a value of its
        // own, an inline array is storage rather than an address, and a field
        // private to the C implementation is named by the private rule. Both
        // spellings of a pointer are covered, the star of a 'const gchar *' and
        // the 'gpointer' the gir writes without one.
        string source = EmitFixture(
            """
                <record name="Definition" c:type="GstDefinition">
                  <field name="value" writable="1">
                    <type name="gint" c:type="gint"/>
                  </field>
                  <field name="nick" writable="1">
                    <type name="utf8" c:type="const gchar*"/>
                  </field>
                  <field name="data" writable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </field>
                  <field name="samples" writable="1">
                    <array zero-terminated="0" fixed-size="2">
                      <type name="gpointer" c:type="gpointer"/>
                    </array>
                  </field>
                  <field name="_reserved" readable="0" private="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </field>
                </record>
            """,
            "Definition");

        Assert.Equal(
            [
                "public int Value;",
                "public nint NickPtr;",
                "public nint DataPtr;",
                "public SamplesArray Samples;",
                "private nint _reserved;",
            ],
            StructFields(source));

        // The inline storage type is still named after the field itself.
        Assert.Contains("[InlineArray(2)]\n    public struct SamplesArray\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMirrorOfAMiniObjectKeepsThePlainPointerName()
    {
        // The suffix belongs to the public value types alone. A mirror is
        // internal and is only ever read through a pointer into memory that
        // GStreamer owns, so no public accessor competes with its fields for a
        // name; the same holds for a boxed record and for a record behind a
        // pointer, neither of which emits a public field at all.
        string source = Source("Buffer");

        Assert.Contains("internal nint Pool;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PoolPtr", source, StringComparison.Ordinal);
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
        // Every accessor reads through the raw pointer of the wrapper, so it
        // keeps the wrapper alive until the read is done instead of being an
        // expression bodied member.
        Assert.Contains(
            """
                public Gst.ClockTime Pts
                {
                    get
                    {
                        Gst.ClockTime value = new(((BufferRaw*)Handle)->Pts);
                        System.GC.KeepAlive(this);
                        return value;
                    }
                }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains("Gst.ClockTime value = new(((BufferRaw*)Handle)->Dts);", source, StringComparison.Ordinal);
        Assert.Contains("Gst.ClockTime value = new(((BufferRaw*)Handle)->Duration);", source, StringComparison.Ordinal);
        Assert.Contains("ulong value = ((BufferRaw*)Handle)->Offset;", source, StringComparison.Ordinal);
        Assert.Contains("ulong value = ((BufferRaw*)Handle)->OffsetEnd;", source, StringComparison.Ordinal);

        // The embedded header and the pool pointer stay behind the mirror.
        Assert.DoesNotContain("Pool =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MiniObject =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MapInfoIsAPlainStructWithInlineArrays()
    {
        string source = Source("MapInfo");

        Assert.Contains(
            "[StructLayout(LayoutKind.Sequential)]\npublic unsafe partial struct MapInfo\n",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            [
                "public nint MemoryPtr;",
                "public Gst.MapFlags Flags;",
                "public nint DataPtr;",
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

        // The wrapper is unsafe because its methods pass pointers, but nothing
        // reads the fields of a GstSample: the gir declares none.
        Assert.Contains("public sealed unsafe partial class Sample : Gst.MiniObject\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("struct SampleRaw", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentImportsItsBoxedType()
    {
        string source = Source("Segment");

        Assert.Contains("public sealed unsafe partial class Segment : Gst.GObject.Boxed\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "[LibraryImport(\"Gst\", EntryPoint = \"gst_segment_get_type\")]\n    internal static partial nuint GetGType();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            ": base(handle, new Gst.GObject.GType(GetGType()), transfer)",
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
        Assert.Contains("Gst.MessageType value = ((MessageRaw*)Handle)->Type;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPrivateStateShellIsEmitted()
    {
        // The gir declares one opaque *Private record next to every class that
        // keeps its state out of the public structure. Each is only named by a
        // pointer field, which the mirror spells as a native integer, so a
        // wrapper for it would be a public pointer holder with nothing on it.
        foreach (GeneratedFile file in Generated.Files)
        {
            Assert.False(
                file.RelativePath.EndsWith("Private.cs", StringComparison.Ordinal),
                file.RelativePath + " is the private state shell of a class.");
        }
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
    public void RecordsAreEmittedByDefault()
    {
        IReadOnlyList<GeneratedFile> files = GenerationPipeline.Run(GirFixture.GirDirectory).Files;

        Assert.Contains(files, static f => f.RelativePath == "GstSharp.Net/Generated/Enums.cs");
        Assert.Contains(files, static f => f.RelativePath == "GstSharp.Net/Generated/Buffer.cs");
    }

    [Theory]
    [InlineData("GstSharp.Net", 116)]
    [InlineData("GstSharp.Net.Base", 19)]
    [InlineData("GstSharp.Net.App", 8)]
    [InlineData("GstSharp.Net.Audio", 37)]
    [InlineData("GstSharp.Net.Video", 72)]
    [InlineData("GstSharp.Net.Pbutils", 19)]
    [InlineData("GstSharp.Net.Sdp", 24)]
    [InlineData("GstSharp.Net.WebRTC", 19)]
    [InlineData("GstSharp.Net.Net", 10)]
    [InlineData("GstSharp.Net.Rtsp", 18)]
    [InlineData("GstSharp.Net.GES", 66)]
    public void EveryModuleEmitsItsOwnFiles(string projectDirectory, int count)
    {
        string prefix = projectDirectory + "/Generated/";
        int emitted = 0;
        foreach (GeneratedFile file in Generated.Files)
        {
            if (file.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                emitted++;
            }
        }

        Assert.Equal(count, emitted);
    }

    [Fact]
    public void TheRecordCensusIsStable()
    {
        List<GeneratedFile> files = [];
        foreach (GeneratedFile file in Generated.Files)
        {
            if (file.RelativePath.StartsWith("GstSharp.Net/Generated/", StringComparison.Ordinal))
            {
                files.Add(file);
            }
        }

        // One file per emitted record and per emitted class, plus the
        // enumerations, the holders of the functions of an enumeration, the
        // holders of the functions of a fundamental type, the interfaces, the
        // global functions, the callbacks, the holder of the connected signal
        // handlers and the type table. GstVecDeque is introspectable="0" and
        // twenty records are the private state shell of a class, which is why
        // 54 opaque records emit 33 files.
        Assert.Equal(116, files.Count);
        Assert.Equal(11, Count(files, " : Gst.MiniObject\n"));
        Assert.Equal(12, Count(files, " : Gst.GObject.Boxed\n"));

        // GstDebugCategory, the five metadata structures of the module and the
        // two static template structures are forced behind a pointer by
        // fixups.json, so the module carries eight plain structs fewer than the
        // gir would give. GstMapInfo and GstPollFD carry bound methods, so
        // their declarations are unsafe: the entry point behind a member of a
        // value projected structure takes a pointer to the structure itself.
        Assert.Equal(4, Count(files, "\npublic partial struct "));
        Assert.Equal(2, Count(files, "\npublic unsafe partial struct "));

        // Nine mini object mirrors and twenty of a boxed or an opaque record:
        // every wrapper of the module whose gir declares a field the layout
        // can project reads it through one.
        Assert.Equal(29, Count(files, "\ninternal unsafe struct "));

        foreach (Diagnostic diagnostic in Generated.Diagnostics)
        {
            Assert.NotEqual("GEN0006", diagnostic.Code);
            Assert.NotEqual("GEN0007", diagnostic.Code);
            Assert.NotEqual("GEN0008", diagnostic.Code);
        }
    }

    [Fact]
    public void ARecordForcedOpaqueIsWrappedBehindAPointer()
    {
        string source = SourceOf("GstSharp.Net/Generated/DebugCategory.cs");

        // Copying a category by value would snapshot its threshold, so the copy
        // would stop seeing the level changes it is consulted for.
        Assert.Contains("public sealed unsafe partial class DebugCategory\n", source, StringComparison.Ordinal);
        Assert.Contains("internal DebugCategory(nint handle) => Handle = handle;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial struct DebugCategory", source, StringComparison.Ordinal);

        // Its callers hand the pointer over instead of the address of a copy.
        Assert.Contains(
            "GstDebugLogDefault(category.Handle, ",
            SourceOf("GstSharp.Net/Generated/Global.cs"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GstSharp.Net.Video/Generated/VideoOverlayComposition.cs")]
    [InlineData("GstSharp.Net.Video/Generated/VideoOverlayRectangle.cs")]
    public void AMiniObjectOfAnotherModuleIsNotMistakenForABoxedType(string path)
    {
        // Both are GstMiniObject subtypes whose gir carries no field at all, so
        // only the hard list of the classifier tells them from a boxed type.
        Assert.Contains(" : Gst.MiniObject\n", SourceOf(path), StringComparison.Ordinal);
    }

    [Fact]
    public void EmittingTwiceProducesIdenticalOutput()
    {
        IReadOnlyList<GeneratedFile> first = GenerationPipeline.Run(GirFixture.GirDirectory).Files;
        IReadOnlyList<GeneratedFile> second = GenerationPipeline.Run(GirFixture.GirDirectory).Files;

        Assert.Equal(
            first.Select(static file => file.RelativePath).ToArray(),
            second.Select(static file => file.RelativePath).ToArray());
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Content, second[i].Content, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void ForcingTheRtspTransportOpaqueBindsItsAccessors()
    {
        // A plain struct gets no members at all, so every accessor of
        // GstRTSPTransport used to be dropped and its destination and source
        // fields were handed out as bare pointers. Behind a pointer the record
        // is constructible and readable, and the two callers-allocate entry
        // points stay out because a wrapper is one pointer wide.
        string source = SourceOf("GstSharp.Net.Rtsp/Generated/RTSPTransport.cs");

        Assert.Contains("public sealed unsafe partial class RTSPTransport", source, StringComparison.Ordinal);
        Assert.Contains(
            "public static Gst.Rtsp.RTSPResult New(out Gst.Rtsp.RTSPTransport? transport)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("public string? AsText()", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.Rtsp.RTSPResult Free()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Parse(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Gst.Rtsp.RTSPResult Init(", source, StringComparison.Ordinal);

        // The ranges it embeds by value keep their value projection; forcing
        // one of them behind a pointer would demote the transport again.
        Assert.Contains(
            "public unsafe partial struct RTSPRange",
            SourceOf("GstSharp.Net.Rtsp/Generated/RTSPRange.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public partial struct RTSPTimeRange",
            SourceOf("GstSharp.Net.Rtsp/Generated/RTSPTimeRange.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ForcingTheStaticTemplatesOpaqueBindsThePadTemplatesOfAFactory()
    {
        // A GstStaticCaps is a cache the library writes back through the
        // pointer it is handed, and a GstStaticPadTemplate lives in the static
        // storage of a factory and embeds one by value. A value projection
        // dropped all four of their methods and handed native code the address
        // of a stack copy, which reparsed the caps and leaked the reference the
        // parse took on every call.
        Assert.Contains(
            "public sealed unsafe partial class StaticCaps\n",
            SourceOf("GstSharp.Net/Generated/StaticCaps.cs"),
            StringComparison.Ordinal);

        string template = SourceOf("GstSharp.Net/Generated/StaticPadTemplate.cs");

        Assert.Contains("public sealed unsafe partial class StaticPadTemplate\n", template, StringComparison.Ordinal);
        Assert.Contains("public Gst.PadTemplate? Get()", template, StringComparison.Ordinal);
        Assert.Contains("public Gst.Caps GetCaps()", template, StringComparison.Ordinal);

        // The two shipped consumers hand the pointer over instead of the
        // address of a copy.
        Assert.Contains(
            "GstPadNewFromStaticTemplate(templ.Handle, ",
            SourceOf("GstSharp.Net/Generated/Pad.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "GstPadTemplateNewFromStaticPadTemplateWithGtype(padTemplate.Handle, ",
            SourceOf("GstSharp.Net/Generated/PadTemplate.cs"),
            StringComparison.Ordinal);

        // And what the change is for: the templates of a factory are readable
        // without building an element first. The list is a const GList the
        // factory keeps owning, so neither its spine nor its elements are
        // released here.
        string factory = SourceOf("GstSharp.Net/Generated/ElementFactory.cs");

        Assert.Contains(
            "public System.Collections.Generic.IReadOnlyList<Gst.StaticPadTemplate> GetStaticPadTemplates()",
            factory,
            StringComparison.Ordinal);
        Assert.Contains(
            "        nint nativeResult = GstElementFactoryGetStaticPadTemplates(Handle);\n"
            + "        System.GC.KeepAlive(this);\n"
            + "        nint[] nativeItems = Gst.Interop.GListMarshal.Collect(nativeResult);\n",
            factory,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (nativeItem != 0 && Gst.StaticPadTemplate.FromNative(nativeItem) is { } adopted)",
            factory,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GstSharp.Net/Generated/Meta.cs", "Meta")]
    [InlineData("GstSharp.Net/Generated/CustomMeta.cs", "CustomMeta")]
    [InlineData("GstSharp.Net/Generated/ParentBufferMeta.cs", "ParentBufferMeta")]
    [InlineData("GstSharp.Net/Generated/ProtectionMeta.cs", "ProtectionMeta")]
    [InlineData("GstSharp.Net/Generated/ReferenceTimestampMeta.cs", "ReferenceTimestampMeta")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioClippingMeta.cs", "AudioClippingMeta")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioDownmixMeta.cs", "AudioDownmixMeta")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioLevelMeta.cs", "AudioLevelMeta")]
    [InlineData("GstSharp.Net.Audio/Generated/DsdPlaneOffsetMeta.cs", "DsdPlaneOffsetMeta")]
    [InlineData("GstSharp.Net.Video/Generated/AncillaryMeta.cs", "AncillaryMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoAFDMeta.cs", "VideoAFDMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoAffineTransformationMeta.cs", "VideoAffineTransformationMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoBarMeta.cs", "VideoBarMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoCaptionMeta.cs", "VideoCaptionMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoCodecAlphaMeta.cs", "VideoCodecAlphaMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoCropMeta.cs", "VideoCropMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoOverlayCompositionMeta.cs", "VideoOverlayCompositionMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoRegionOfInterestMeta.cs", "VideoRegionOfInterestMeta")]
    [InlineData("GstSharp.Net.Video/Generated/VideoSEIUserDataUnregisteredMeta.cs", "VideoSEIUserDataUnregisteredMeta")]
    [InlineData("GstSharp.Net.Net/Generated/NetAddressMeta.cs", "NetAddressMeta")]
    [InlineData("GstSharp.Net.Net/Generated/NetControlMessageMeta.cs", "NetControlMessageMeta")]
    [InlineData("GstSharp.Net.GES/Generated/FrameCompositionMeta.cs", "FrameCompositionMeta")]
    public void EveryMetadataStructureIsWrappedBehindAPointer(string path, string typeName)
    {
        // A metadata item lives inside the buffer that carries it, so a value
        // projection could only snapshot it: reads stop tracking the buffer,
        // writes reach a temporary, and the pointer identity the removal and
        // iteration calls key on is lost. GstMeta is the header the other
        // twenty one embed as their first field, so the family goes behind a
        // pointer together and no value type of the family is left.
        string source = SourceOf(path);

        Assert.Contains("public sealed ", source, StringComparison.Ordinal);
        Assert.Contains("partial class " + typeName + "\n", source, StringComparison.Ordinal);
        Assert.Contains("internal " + typeName + "(nint handle) => Handle = handle;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial struct ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Gst.Meta Meta;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMetadataAccessorsOfABufferFollowTheirRecords()
    {
        // Every one of these used to be dropped, because a value projected
        // GstMeta has no C# spelling the planner would accept for a pointer
        // the library hands out. Behind a pointer they bind, and what the
        // caller receives addresses the metadata of the buffer itself.
        string source = SourceOf("GstSharp.Net/Generated/Buffer.cs");

        Assert.Contains("public Gst.Meta? GetMeta(Gst.GObject.GType api)", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.CustomMeta? GetCustomMeta(string name)", source, StringComparison.Ordinal);
        Assert.Contains(
            "public Gst.Meta? AddMeta(Gst.MetaInfo info, nint @params)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public Gst.ParentBufferMeta? AddParentBufferMeta(Gst.Buffer @ref)",
            source,
            StringComparison.Ordinal);

        // The removal keeps its overlay skip: it is the one call of the family
        // whose contract the gir does not describe.
        Assert.DoesNotContain("RemoveMeta(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRtspWatchIsNotEmitted()
    {
        // gst_rtsp_watch_new is introspectable="0" and gst_rtsp_watch_attach
        // takes a GLib main context, so nothing can produce a GstRTSPWatch and
        // every method on it was unreachable. Its callback table has no member
        // at all. Both are skipped rather than shipped dead.
        Assert.DoesNotContain(
            Generated.Files,
            static file => file.RelativePath.Contains("RTSPWatch", StringComparison.Ordinal));
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

    private static int Count(IEnumerable<GeneratedFile> files, string needle)
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

    private static string Source(string typeName) =>
        SourceOf("GstSharp.Net/Generated/" + typeName + ".cs");

    private static string SourceOf(string path)
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException($"No file was generated for '{path}'.");
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

            // The entry points of the members a value projected structure
            // carries sit at the same level and end in a semicolon too; they
            // are declarations rather than storage.
            string trimmed = line.Trim();
            if ((trimmed.StartsWith("public ", StringComparison.Ordinal)
                    || trimmed.StartsWith("private ", StringComparison.Ordinal))
                && !trimmed.StartsWith("private static partial ", StringComparison.Ordinal)
                && trimmed.EndsWith(';'))
            {
                fields.Add(trimmed);
            }
        }

        return [.. fields];
    }

    /// <summary>What one fixture run produced.</summary>
    /// <param name="Source">The generated source text.</param>
    /// <param name="Diagnostics">What the run reported.</param>
    /// <param name="Census">What the run emitted and left out.</param>
    private sealed record FixtureEmission(
        string Source,
        IReadOnlyList<Diagnostic> Diagnostics,
        EmissionCensus Census);

    private static string EmitFixture(string body, string recordName, string? extraNamespace = null)
    {
        FixtureEmission emission = EmitFixtureWithDiagnostics(body, recordName, extraNamespace);
        Assert.Empty(emission.Diagnostics);
        return emission.Source;
    }

    private static FixtureEmission EmitFixtureWithDiagnostics(
        string body,
        string recordName,
        string? extraNamespace = null)
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
        NameMapper names = new(Overlays.Empty, diagnostics);
        Classifier classifier = new(repository, Overlays.Empty, diagnostics);
        TypeMap types = new(repository, classifier, names, diagnostics);
        EmissionCensus census = new();
        SkipRules skipRules = new(Overlays.Empty);
        MarshalPlanner planner = new(repository, classifier, names, types, Overlays.Empty, skipRules, diagnostics);
        SurfaceBuilder surfaces = new(planner, names, types, census, diagnostics);
        List<RegistryEntry> registry = [];
        RecordEmitter emitter = new(
            repository,
            classifier,
            names,
            types,
            Overlays.Empty,
            skipRules,
            diagnostics,
            surfaces,
            census,
            registry);

        GirRecord record = ns.Records.Single(
            candidate => string.Equals(candidate.Name, recordName, StringComparison.Ordinal));
        GeneratedFile generated = emitter.Emit(GstModule, ns, record)
            ?? throw new InvalidOperationException("The record produced no output.");

        return new FixtureEmission(generated.Content, diagnostics.Items, census);
    }
}
