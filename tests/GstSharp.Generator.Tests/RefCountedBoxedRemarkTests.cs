using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>MarshalPlanner.RefCountedBoxedTypes</c> table: a boxed record whose
/// registered <c>GBoxedCopyFunc</c> is the type's own <c>_ref</c> is consumed
/// exactly like any other boxed value and documented as taking a reference
/// rather than as taking a copy.
/// </summary>
/// <remarks>
/// The table is curated from the C registrations, because no gir attribute
/// carries the copy function of a boxed record. Two things keep it honest: a
/// key that no longer names a record of the vendored girs is a stale entry, and
/// the two fixtures below pin both halves of the split — the listed record gets
/// the reference wording, an unlisted boxed record keeps the copy wording — as
/// well as the fact that the unlisted record is emitted as the same
/// <c>BoxedCopy</c> the listed one is minted with in
/// <c>GLibDateTimeRuntimeTypeTests</c>.
/// </remarks>
public sealed class RefCountedBoxedRemarkTests
{
    /// <summary>
    /// A <c>GLib</c> namespace with the one record the fixture refers to, the
    /// stand in for the vendored <c>GLib-2.0.gir</c> that
    /// <c>GLibDateTimeRuntimeTypeTests</c> uses.
    /// </summary>
    private const string GLibNamespace =
        """
          <namespace name="GLib" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <record name="DateTime" c:type="GDateTime" opaque="1" glib:type-name="GDateTime" glib:get-type="g_date_time_get_type">
            </record>
          </namespace>
        """;

    /// <summary>
    /// Two consuming members of two classes, so that each generated file holds
    /// one remark: <c>Stamp</c> takes a <c>GDateTime</c>, which the table
    /// lists, and <c>Crate</c> takes a <c>GstSegment</c>, whose copy function
    /// is a real copy.
    /// </summary>
    private const string Body =
        """
            <record name="Segment" c:type="GstSegment" glib:type-name="GstSegment" glib:get-type="gst_segment_get_type">
              <field name="rate" writable="1">
                <type name="gdouble" c:type="gdouble"/>
              </field>
            </record>
            <class name="Stamp" c:type="GstStamp" parent="GObject.Object" glib:type-name="GstStamp" glib:get-type="gst_stamp_get_type">
              <method name="take_time" c:identifier="gst_stamp_take_time">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="stamp" transfer-ownership="none">
                    <type name="Stamp" c:type="GstStamp*"/>
                  </instance-parameter>
                  <parameter name="time" transfer-ownership="full">
                    <type name="GLib.DateTime" c:type="GDateTime*"/>
                  </parameter>
                </parameters>
              </method>
            </class>
            <class name="Crate" c:type="GstCrate" parent="GObject.Object" glib:type-name="GstCrate" glib:get-type="gst_crate_get_type">
              <method name="take_segment" c:identifier="gst_crate_take_segment">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="crate" transfer-ownership="none">
                    <type name="Crate" c:type="GstCrate*"/>
                  </instance-parameter>
                  <parameter name="segment" transfer-ownership="full">
                    <type name="Segment" c:type="GstSegment*"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body, overlays: null, extraNamespaces: GLibNamespace),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// Every key of the table names a <c>&lt;record&gt;</c> of the vendored
    /// reference girs. A record that upstream removed or renamed leaves an
    /// entry that documents a type nothing can reach, which is what this
    /// refuses.
    /// </summary>
    [Fact]
    public void EveryRefCountedBoxedEntryNamesARecordOfTheReferenceGirs()
    {
        Assert.NotEmpty(MarshalPlanner.RefCountedBoxedTypes);
        foreach (string name in MarshalPlanner.RefCountedBoxedTypes.Keys)
        {
            GirSymbol symbol = GirFixture.Symbol(name);
            Assert.Equal(GirSymbolKind.Record, symbol.Kind);
        }
    }

    /// <summary>
    /// Every copy function of the table is spelled as the C function it is: a
    /// name that ends in <c>_ref</c>, which is the property the whole table
    /// stands for.
    /// </summary>
    [Fact]
    public void EveryRefCountedBoxedEntryNamesAReferencingCopyFunction()
    {
        foreach (string copyFunction in MarshalPlanner.RefCountedBoxedTypes.Values)
        {
            Assert.EndsWith("_ref", copyFunction, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A consumed record of the table is documented as handed a reference of
    /// its own, and the remark names the copy function that makes it one.
    /// </summary>
    [Fact]
    public void AConsumedRefCountedBoxedArgumentIsDocumentedAsTakingAReference()
    {
        string source = Run.File("Stamp.cs");

        Assert.Contains(
            "handed a reference of its own and the wrapper is disposed afterwards, which",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<c>GDateTime</c> is a boxed type whose registered copy function is",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<c>g_date_time_ref</c>, so copying it is taking a reference.",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("no reference count to raise", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A consumed boxed record the table does not list keeps the wording of a
    /// value that is really copied.
    /// </summary>
    [Fact]
    public void AConsumedPlainBoxedArgumentIsDocumentedAsTakingACopy()
    {
        string source = Run.File("Crate.cs");

        Assert.Contains(
            "handed a copy of the value and the wrapper is disposed afterwards, which",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "value has no reference count to raise, so the copy is what a reference is",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("whose registered copy function is", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The split is documentation only: a plain boxed argument is minted with
    /// the same <c>g_boxed_copy</c> as a listed one, which dispatches to
    /// whatever the type registered. The other half of the claim — a listed
    /// record minted the same way — is pinned by
    /// <c>GLibDateTimeRuntimeTypeTests</c>.
    /// </summary>
    [Fact]
    public void APlainBoxedArgumentIsMintedWithTheSameBoxedCopy()
    {
        Assert.Equal(
            """
            public void TakeSegment(Gst.Segment segment)
            {
                ArgumentNullException.ThrowIfNull(segment);
                nint instanceHandle = Handle;
                nint segmentNative = segment.Handle;
                nuint segmentType = segment.BoxedType.Value;
                nint segmentOwned = Gst.Interop.GObjectNative.BoxedCopy(segmentType, segmentNative);
                GstCrateTakeSegment(instanceHandle, segmentOwned);
                segment.Dispose();
                System.GC.KeepAlive(this);
            }
            """,
            Run.Member("Crate.cs", "public void TakeSegment("),
            StringComparer.Ordinal);
    }
}
