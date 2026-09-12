using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>signalDocNotes</c> overlay, which adds a sentence to the generated
/// documentation of an event.
/// </summary>
/// <remarks>
/// A signal carries no <c>c:identifier</c>, so the note of a callable cannot
/// name one: the key is the GObject spelling the nullable signal argument
/// overrides already use, without the <c>#argument</c> suffix. The note reaches
/// both shapes a signal is emitted in - the event of a class and the pair of
/// extension methods of an interface - and a key that names no planned signal
/// is reported rather than dropped, as the note of a callable is.
/// </remarks>
public sealed class SignalDocNoteTests
{
    /// <summary>
    /// One class with a signal and one interface with a signal, which is the
    /// two shapes a note can land on.
    /// </summary>
    private const string Body =
        """
            <interface name="Sizer" c:type="GstSizer" glib:type-name="GstSizer" glib:get-type="gst_sizer_get_type">
              <glib:signal name="resized" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
              </glib:signal>
            </interface>
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="changed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
              </glib:signal>
            </class>
        """;

    [Fact]
    public void ANoteIsWrittenIntoTheDocumentationOfTheEvent()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "signalDocNotes": { "Gst.Widget::changed": "The emission holds the object lock." }
            }
            """);

        Assert.Contains(
            "The emission holds the object lock.",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0048", StringComparison.Ordinal));
    }

    [Fact]
    public void ANoteIsWrittenIntoTheDocumentationOfTheHandlerAccessorsOfAnInterface()
    {
        // The signal of an interface is emitted as a pair of extension methods
        // rather than as an event, and the note belongs on the one that
        // connects a handler.
        FixtureRun run = RunWithOverlay(
            """
            {
              "signalDocNotes": { "Gst.Sizer::resized": "Only the sizer that changed raises it." }
            }
            """);

        Assert.Contains(
            "Only the sizer that changed raises it.",
            run.File("ISizer.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteThatNamesNoSignalIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "signalDocNotes": { "Gst.Widget::vanished": "Nothing names this." }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0048", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Gst.Widget::vanished", StringComparison.Ordinal));
    }

    private static FixtureRun RunWithOverlay(string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(Body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
