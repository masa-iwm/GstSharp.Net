using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Gst;
using Gst.GLib;
using Gst.Pbutils;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The serialisation pair of a discovery result, against the library that is
/// installed: a result turns into a <c>GVariant</c>, the variant turns into
/// bytes, and the bytes turn back into a result that says the same things.
/// </summary>
/// <remarks>
/// The WAV is built here rather than shared: the two other tests that need one
/// keep their builder private, and a fixture file would have to be committed.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class DiscovererVariantTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public DiscovererVariantTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A discovery survives the round trip through bytes, and the variant the
    /// binding hands out is owned rather than floating.
    /// </summary>
    [RequiresElementFact("wavparse")]
    public void ADiscoveryRoundTripsThroughBytes()
    {
        GstPbutils.Initialize();

        string path = Path.Combine(
            Path.GetTempPath(),
            FormattableString.Invariant($"gstsharp-variant-{Guid.NewGuid():N}.wav"));

        File.WriteAllBytes(path, SilentWave(sampleCount: 8000));
        try
        {
            using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(10));
            using DiscovererInfo info = discoverer.DiscoverUri(new System.Uri(path).AbsoluteUri);

            Assert.Equal(DiscovererResult.Ok, info.GetResult());

            using Variant variant = info.ToVariant(DiscovererSerializeFlags.All);

            _output.WriteLine(FormattableString.Invariant($"variant type = {variant.TypeString}"));
            Assert.Equal("v", variant.TypeString);

            // The C hands back the floating reference of g_variant_new_variant
            // and the wrapper claims it, so what the test holds is an owned
            // value rather than one that is still looking for an owner.
            Assert.Equal(0, VariantNatives.IsFloating(variant.Handle));

            using Bytes bytes = variant.ToBytes();
            Assert.True(bytes.Size > 0);

            using Variant read = Variant.FromBytes("v", bytes);
            using DiscovererInfo? copy = DiscovererInfo.FromVariant(read);

            Assert.NotNull(copy);
            Assert.Equal(info.GetUri(), copy.GetUri());
            Assert.Equal(info.GetDuration(), copy.GetDuration());
            Assert.Equal(info.GetResult(), copy.GetResult());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Disposing a variant twice releases its reference once, which is what
    /// makes a <c>using</c> around one that was handed on stay correct.
    /// </summary>
    [RequiresElementFact("wavparse")]
    public void DisposingAVariantTwiceIsSafe()
    {
        GstPbutils.Initialize();

        string path = Path.Combine(
            Path.GetTempPath(),
            FormattableString.Invariant($"gstsharp-variant-dispose-{Guid.NewGuid():N}.wav"));

        File.WriteAllBytes(path, SilentWave(sampleCount: 8000));
        try
        {
            using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(10));
            using DiscovererInfo info = discoverer.DiscoverUri(new System.Uri(path).AbsoluteUri);

            Variant variant = info.ToVariant(DiscovererSerializeFlags.Basic);
            variant.Dispose();
            variant.Dispose();

            Assert.Throws<ObjectDisposedException>(() => variant.Handle);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Only the output of <c>ToVariant</c> is read back: a value of another
    /// type is refused before the library could raise a critical over it.
    /// </summary>
    [Fact]
    public void AVariantOfTheWrongTypeIsRefused()
    {
        GstPbutils.Initialize();

        using Bytes bytes = Bytes.New([1, 2, 3, 4]);
        using Variant array = Variant.FromBytes("ay", bytes);

        Assert.Equal("ay", array.TypeString);
        Assert.Throws<ArgumentException>(() => DiscovererInfo.FromVariant(array));
    }

    /// <summary>
    /// A type string GLib does not accept is refused by
    /// <c>Variant.FromBytes</c> rather than handed to
    /// <c>g_variant_type_new</c>, which asserts on one.
    /// </summary>
    [Fact]
    public void AnInvalidTypeStringIsRefused()
    {
        GstPbutils.Initialize();

        using Bytes bytes = Bytes.New([1, 2, 3, 4]);

        Assert.Throws<ArgumentException>(() => Variant.FromBytes("this is not a type", bytes));
    }

    /// <summary>
    /// An indefinite type string is refused as well, although GLib calls it a
    /// valid one: a value cannot have such a type and the call that would
    /// build one asserts over it rather than reporting it.
    /// </summary>
    /// <param name="typeString">The indefinite type string to try.</param>
    [Theory]
    [InlineData("*")]
    [InlineData("a*")]
    public void AnIndefiniteTypeStringIsRefused(string typeString)
    {
        GstPbutils.Initialize();

        using Bytes bytes = Bytes.New([1, 2, 3, 4]);

        Assert.Throws<ArgumentException>(() => Variant.FromBytes(typeString, bytes));
    }

    /// <summary>
    /// A result with no stream tree is refused before the call: the C admits
    /// the result and then dereferences the tree it did not get.
    /// </summary>
    /// <remarks>
    /// A fresh <c>GstDiscovererInfo</c> is what this measures against. Its
    /// result is <see cref="DiscovererResult.Ok"/>, which is zero, and it has
    /// no stream tree, which is the state a discovery that reported a missing
    /// plugin before any topology was posted also ends in.
    /// </remarks>
    [Fact]
    public void AResultWithoutAStreamTreeIsRefused()
    {
        GstPbutils.Initialize();

        nint handle = TestNatives.ObjectNewWithProperties(
            DiscovererNatives.InfoGetType(),
            0,
            nint.Zero,
            nint.Zero);

        Assert.NotEqual(nint.Zero, handle);

        using DiscovererInfo info = Gst.GObject.Object.FromNative<DiscovererInfo>(
            handle,
            Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("g_object_new_with_properties returned no info.");

        Assert.Equal(DiscovererResult.Ok, info.GetResult());
        Assert.Null(info.GetStreamInfo());

        Assert.Throws<InvalidOperationException>(() => info.ToVariant(DiscovererSerializeFlags.All));
    }

    /// <summary>Builds a silent mono WAV file of the given length.</summary>
    /// <param name="sampleCount">How many samples the file carries.</param>
    /// <returns>The bytes of the file.</returns>
    private static byte[] SilentWave(int sampleCount)
    {
        const int SampleRate = 8000;
        const int BitsPerSample = 16;
        const int Channels = 1;

        int dataBytes = sampleCount * Channels * (BitsPerSample / 8);
        byte[] file = new byte[44 + dataBytes];
        Span<byte> span = file;

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * Channels * (BitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], Channels * (BitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        return file;
    }
}

/// <summary>
/// The <c>GstDiscovererInfo</c> entry point a test needs to build one of its
/// own: the type, so that <c>g_object_new_with_properties</c> can construct a
/// fresh result. <c>gst_discoverer_info_new</c> itself is static in the
/// library and not exported.
/// </summary>
internal static partial class DiscovererNatives
{
    /// <summary>Answers the type <c>GstDiscovererInfo</c> is registered under.</summary>
    /// <returns>The type.</returns>
    [LibraryImport("GstPbutils", EntryPoint = "gst_discoverer_info_get_type")]
    internal static partial nuint InfoGetType();
}

/// <summary>
/// The <c>GVariant</c> entry point the ownership half of these tests needs and
/// the binding does not offer: whether a value is still floating is a fact
/// about the reference the C handed over, not something a caller of the
/// binding ever has to ask.
/// </summary>
internal static partial class VariantNatives
{
    /// <summary>Answers whether a value still carries a floating reference.</summary>
    /// <param name="value">The value to ask about.</param>
    /// <returns>Non-zero when the reference is still floating.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_is_floating")]
    internal static partial int IsFloating(nint value);
}
