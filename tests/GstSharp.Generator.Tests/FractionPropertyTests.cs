using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>Gst.Fraction</c> properties. <c>GST_TYPE_FRACTION</c> is a
/// fundamental GStreamer registers itself rather than one of GLib's, so it is
/// the one entry of the value accessor table that is not a GLib type, and it
/// projects onto the <c>Gst.Fraction</c> pair of the runtime.
/// </summary>
public sealed class FractionPropertyTests
{
    /// <summary>
    /// The fundamental as the vendored <c>Gst</c> gir declares it, and one
    /// class carrying a fraction in the two shapes a property has: the writable
    /// one and the construct-only one. The properties spell the type
    /// <c>Gst.Fraction</c>, the cross namespace spelling the one live fraction
    /// property carries — <c>GstAudio-1.0.gir</c> names
    /// <c>AudioAggregator:output-buffer-duration-fraction</c> that way — and
    /// the only spelling the value accessor table matches. A gir never
    /// qualifies a type of its own namespace, so no same-namespace fraction
    /// property exists to bind.
    /// </summary>
    private const string Body =
        """
            <class name="Fraction" c:symbol-prefix="fraction" glib:type-name="GstFraction" glib:get-type="gst_fraction_get_type" glib:fundamental="1">
            </class>
            <class name="Mixer" c:type="GstMixer" parent="GObject.Object" glib:type-name="GstMixer" glib:get-type="gst_mixer_get_type">
              <property name="output-buffer-duration-fraction" writable="1" transfer-ownership="none" default-value="1/100">
                <doc xml:space="preserve">the output block size, as a fraction</doc>
                <type name="Gst.Fraction"/>
              </property>
              <property name="pixel-aspect-ratio" writable="1" construct-only="1" transfer-ownership="none">
                <type name="Gst.Fraction"/>
              </property>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A writable fraction reads and writes through the typed accessors of the
    /// runtime, and the pair owns nothing, so the property is the plain struct
    /// rather than a nullable wrapper the caller has to dispose.
    /// </summary>
    [Fact]
    public void AFractionPropertyIsPlannedOntoTheFractionAccessors()
    {
        Assert.Equal(
            """
            public Gst.Fraction OutputBufferDurationFraction
            {
                get
                {
                    using Gst.GObject.Value holder = GetProperty("output-buffer-duration-fraction");
                    return holder.GetFraction();
                }

                set
                {
                    using Gst.GObject.Value holder = NewPropertyValue("output-buffer-duration-fraction");
                    holder.SetFraction(value);
                    SetPropertyValue("output-buffer-duration-fraction", in holder);
                }
            }
            """,
            Run.Member("Mixer.cs", "public Gst.Fraction OutputBufferDurationFraction"),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Construct-only leaves a fraction read only, as it leaves every other
    /// value backed property.
    /// </summary>
    [Fact]
    public void AConstructOnlyFractionPropertyIsGetOnly()
    {
        Assert.Equal(
            """
            public Gst.Fraction PixelAspectRatio
            {
                get
                {
                    using Gst.GObject.Value holder = GetProperty("pixel-aspect-ratio");
                    return holder.GetFraction();
                }
            }
            """,
            Run.Member("Mixer.cs", "public Gst.Fraction PixelAspectRatio"),
            StringComparer.Ordinal);
    }
}
