using System.Runtime.InteropServices;
using System.Text;
using Gst.GLib;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The two halves of the <c>GError</c> projection that need no GStreamer
/// installation: reading a borrowed error into a managed value, and refusing
/// a value that cannot be built into one.
/// </summary>
/// <remarks>
/// The other half - <c>GMarshal.AllocError</c> - calls
/// <c>g_error_new_literal</c> and therefore belongs in the integration tests,
/// where a GLib exists to allocate from.
/// </remarks>
public class GExceptionMarshalTests
{
    [Fact]
    public void ANullErrorReadsAsNoValue()
    {
        Assert.Null(GException.FromBorrowed(nint.Zero));
    }

    [Fact]
    public unsafe void ABorrowedErrorIsCopiedFieldByFieldAndLeftAlone()
    {
        byte[] message = Encoding.UTF8.GetBytes("the disc is on fire\0");
        fixed (byte* text = message)
        {
            // The layout of a GError, built by hand: the runtime never
            // allocates one to read, it only reads what the library owns.
            ProbeError native = new()
            {
                Domain = 0x1234u,
                Code = 42,
                Message = (nint)text,
            };

            GException? error = GException.FromBorrowed((nint)(&native));

            Assert.NotNull(error);
            Assert.Equal(0x1234u, error.Domain.Value);
            Assert.Equal(42, error.Code);
            Assert.Equal("the disc is on fire", error.Message);

            // Nothing was freed: the storage is still the caller's, still
            // readable, and still says what it said.
            Assert.Equal(0x1234u, native.Domain);
            Assert.Equal(42, native.Code);
            Assert.Equal("the disc is on fire", Marshal.PtrToStringUTF8(native.Message));
        }
    }

    [Fact]
    public unsafe void AnErrorWithoutAMessageReadsAsTheDefaultOne()
    {
        ProbeError native = new()
        {
            Domain = 7u,
            Code = -1,
            Message = nint.Zero,
        };

        GException? error = GException.FromBorrowed((nint)(&native));

        Assert.NotNull(error);
        Assert.Equal("The operation failed.", error.Message);
    }

    [Fact]
    public void AnErrorWithoutADomainIsRefusedBeforeTheCall()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => GException.ValidateForNative(new GException("boom"), "error"));

        Assert.Equal("error", error.ParamName);
        Assert.Contains("registered GQuark", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnErrorWithoutAMessageIsRefusedBeforeTheCall()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => GException.ValidateForNative(new GException(new Quark(11u), 3, string.Empty), "error"));

        Assert.Equal("error", error.ParamName);
        Assert.Contains("no message", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message crosses as a C string, so a null in the middle of it is
    /// refused here rather than by the string encoder, whose parameter name
    /// means nothing to the caller.
    /// </summary>
    [Fact]
    public void AMessageWithAnEmbeddedNullIsRefusedBeforeTheCall()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => GException.ValidateForNative(
                new GException(new Quark(11u), 3, "boo\0m"), "error"));

        Assert.Equal("error", error.ParamName);
        Assert.Contains("embedded null", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnErrorWithADomainAndAMessagePasses()
    {
        GException.ValidateForNative(new GException(new Quark(11u), 3, "boom"), "error");
    }

    [Fact]
    public void NoErrorAtAllPasses()
    {
        GException.ValidateForNative(null, "error");
    }

    /// <summary>The layout of a <c>GError</c>, writable so a test can build one.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeError
    {
        internal uint Domain;
        internal int Code;
        internal nint Message;
    }
}
