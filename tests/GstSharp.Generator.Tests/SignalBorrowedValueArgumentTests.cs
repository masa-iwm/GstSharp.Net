using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>GValue</c> a signal lends its handler. It is shown through a view
/// built over the storage the emitter holds, which the arguments refuse to hand
/// out once the emission has ended; a writable <c>GValue</c> keeps the signal
/// off the surface, because an argument a handler writes through is an out
/// parameter of the emission and no signal of the corpus has one.
/// </summary>
public sealed class SignalBorrowedValueArgumentTests
{
    /// <summary>
    /// The shape <c>GESMetaContainer::notify-meta</c> has: a key beside a value
    /// the emission may spell as <c>NULL</c>.
    /// </summary>
    private const string BorrowedBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="notify-field" when="first">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="key" transfer-ownership="none">
                    <type name="utf8" c:type="gchar*"/>
                  </parameter>
                  <parameter name="value" transfer-ownership="none" nullable="1" allow-none="1">
                    <doc xml:space="preserve">The new value under @key</doc>
                    <type name="GObject.Value"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// The same signal with a value the handler would write through, which the
    /// gir spells with the second star on the C type.
    /// </summary>
    private const string WritableBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="notify-field" when="first">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="value" transfer-ownership="none">
                    <type name="GObject.Value" c:type="GValue**"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// A lent <c>GValue</c> reaches the handler as a pointer the arguments hold
    /// and a view they build on every read.
    /// </summary>
    [Fact]
    public void ALentValueIsPlanned()
    {
        FixtureRun run = Fixture.Run(BorrowedBody);
        string source = run.File("Widget.cs");

        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));

        // A ref struct cannot be a field, so what is held is the pointer and
        // the view is built on every read.
        Assert.Contains("private readonly nint _value;", source, StringComparison.Ordinal);
        Assert.Contains("public bool HasValue => _value != 0;", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.GObject.ValueView Value", source, StringComparison.Ordinal);

        // Reading is refused twice over: when the emission carried nothing, and
        // once it has ended.
        Assert.Contains("if (_ended)", source, StringComparison.Ordinal);
        Assert.Contains("if (_value == 0)", source, StringComparison.Ordinal);

        // The end of the borrow is announced on every path out of the handler,
        // and it leaves HasValue saying what the emission carried.
        Assert.Contains("internal void Invalidate() => _ended = true;", source, StringComparison.Ordinal);
        Assert.Contains("args.Invalidate();", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
    }

    /// <summary>A writable value keeps the signal off the surface.</summary>
    [Fact]
    public void AWritableValueIsRefused()
    {
        FixtureRun run = Fixture.Run(WritableBody);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain("NotifyField", run.File("Widget.cs"), StringComparison.Ordinal);
    }
}
