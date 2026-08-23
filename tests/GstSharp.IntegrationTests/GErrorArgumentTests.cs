using Gst;
using Gst.GLib;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The in direction of the <c>GError</c> projection: a
/// <see cref="GException"/> handed to a call is built into a temporary the
/// member frees again, and the library keeps only what it copied.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GErrorProbeTests"/> is the other half of the pair and covers the
/// <c>throws</c> side, where a call produces an error the binding raises and
/// frees. The seven members here go the other way, and the round trip through
/// <c>gst_message_parse_error</c> is what says the three fields survived the
/// crossing whole.
/// </para>
/// <para>
/// The domain of the error is <c>gst_core_error_quark</c>, reached through the
/// generated <c>Gst.CoreErrorExtensions.Quark()</c>, so the test uses a
/// registered quark of the library rather than interning one of its own.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GErrorArgumentTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public GErrorArgumentTests(ITestOutputHelper output) => _output = output;

    private static Quark CoreErrors => CoreErrorExtensions.Quark();

    /// <summary>
    /// An error message carries back the domain, the code and the message it
    /// was built from, and the debug string beside them.
    /// </summary>
    [Fact]
    public void AnErrorMessageRoundTripsTheErrorItWasBuiltFrom()
    {
        GException error = new(CoreErrors, (int)CoreError.Failed, "boom");

        using Message message = Message.NewError(null, error, "the debug string");

        Assert.Equal(MessageType.Error, message.Type);

        (GException parsed, string? debug) = message.ParseError();

        _output.WriteLine(FormattableString.Invariant(
            $"domain={parsed.Domain} code={parsed.Code} message={parsed.Message}"));

        Assert.Equal(CoreErrors, parsed.Domain);
        Assert.Equal((int)CoreError.Failed, parsed.Code);
        Assert.Equal("boom", parsed.Message);
        Assert.Equal("the debug string", debug);
    }

    /// <summary>The warning and the info message take the same argument.</summary>
    [Fact]
    public void AWarningAndAnInfoMessageRoundTripTheirErrorAsWell()
    {
        GException warning = new(CoreErrors, (int)CoreError.Negotiation, "careful");
        using (Message message = Message.NewWarning(null, warning, null))
        {
            Assert.Equal(MessageType.Warning, message.Type);
            (GException parsed, string? debug) = message.ParseWarning();
            Assert.Equal(CoreErrors, parsed.Domain);
            Assert.Equal((int)CoreError.Negotiation, parsed.Code);
            Assert.Equal("careful", parsed.Message);
            Assert.Null(debug);
        }

        GException info = new(CoreErrors, (int)CoreError.TooLazy, "by the way");
        using (Message message = Message.NewInfo(null, info, "note"))
        {
            Assert.Equal(MessageType.Info, message.Type);
            (GException parsed, string? debug) = message.ParseInfo();
            Assert.Equal(CoreErrors, parsed.Domain);
            Assert.Equal((int)CoreError.TooLazy, parsed.Code);
            Assert.Equal("by the way", parsed.Message);
            Assert.Equal("note", debug);
        }
    }

    /// <summary>
    /// The <c>_with_details</c> overload is the one member where the new kind
    /// and a consumed handle meet in one call: the error crosses as a
    /// temporary and the structure is handed over, and both arrive.
    /// </summary>
    [Fact]
    public void TheDetailedOverloadRoundTripsTheErrorAndTheStructure()
    {
        GException error = new(CoreErrors, (int)CoreError.Failed, "detailed boom");
        Structure details = Structure.NewEmpty("gstsharp-details");
        using (Value answerValue = Value.New(GType.Int))
        {
            answerValue.SetInt(42);
            details.SetValue("answer", answerValue);
        }

        using Message message = Message.NewErrorWithDetails(null, error, "debug", details);

        // The structure was consumed: the wrapper is disposed and the value
        // now belongs to the message.
        Assert.True(details.IsDisposed);

        (GException parsed, string? debug) = message.ParseError();
        Assert.Equal(CoreErrors, parsed.Domain);
        Assert.Equal("detailed boom", parsed.Message);
        Assert.Equal("debug", debug);

        message.ParseErrorDetails(out Structure? carried);
        Assert.NotNull(carried);
        Assert.Equal("gstsharp-details", carried.GetName());
        Assert.True(carried.GetInt("answer", out int answer));
        Assert.Equal(42, answer);
    }

    /// <summary>
    /// An exception that carries no error domain is refused, and refused
    /// before anything is allocated: the structure the same call would have
    /// consumed is still the caller's.
    /// </summary>
    /// <remarks>
    /// The guard phase of a member that materializes an argument runs before
    /// every allocation, and a disposed structure would be the visible proof
    /// that it did not. <c>g_error_new_literal</c> answers <c>NULL</c> with a
    /// critical for a zero domain, so the check has to happen on this side.
    /// </remarks>
    [Fact]
    public void AnErrorWithoutADomainIsRefusedBeforeAnythingIsAllocated()
    {
        GException domainless = new("boom");
        Assert.Equal(Quark.Zero, domainless.Domain);

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => Message.NewError(null, domainless, null));
        Assert.Equal("error", refused.ParamName);

        using Structure details = Structure.NewEmpty("gstsharp-untouched");

        ArgumentException refusedAgain = Assert.Throws<ArgumentException>(
            () => Message.NewErrorWithDetails(null, domainless, null, details));
        Assert.Equal("error", refusedAgain.ParamName);

        // Nothing was minted and nothing was consumed.
        Assert.False(details.IsDisposed);
        Assert.Equal("gstsharp-untouched", details.GetName());
    }

    /// <summary>A null error is refused by the ordinary argument guard.</summary>
    [Fact]
    public void ANullErrorIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Message.NewError(null, null!, null));
    }

    /// <summary>
    /// The other in direction member only reads the error: it prints it and
    /// returns, which is the borrow the scope was written for.
    /// </summary>
    [Fact]
    public void TheDefaultErrorHandlerReadsTheErrorAndReturns()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "default-error"));

        GException error = new(CoreErrors, (int)CoreError.Failed, "printed by the default handler");

        sink.DefaultError(error, null);
        sink.DefaultError(error, "with a debug string");

        // The wrapper survived both calls: nothing of the caller's was freed.
        Assert.Equal("default-error", sink.Name);
    }
}
