using System;
using Gst;
using Gst.Audio;
using Gst.Gio;
using Gst.Net;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>preconditions</c> guard on the meta adders whose C body uses the NULL
/// that <c>gst_buffer_add_meta</c> answers for a shared buffer.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_buffer_add_meta</c> refuses a buffer that is not writable and answers
/// NULL (gstbuffer.c:2335). Several adders of the library write a field of that
/// answer, or assert it is not NULL and then write one, so a shared buffer is a
/// crash rather than an error a caller could see. The binding refuses the call
/// first instead, and these tests are what says so: the process survives the
/// attempt, and the message names the C function the fault is in.
/// </para>
/// <para>
/// One member per module carries the test, because the guard is one helper and
/// one overlay entry rather than a body per member: <see cref="Buffer"/> itself,
/// GstAudio, GstNet and GstVideo, with both region of interest overloads of
/// GstVideo, which is where the crash was found.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class BufferMetaWritabilityTests
{
    /// <summary>
    /// Attaching protection metadata to a buffer somebody else holds is refused
    /// rather than crashing.
    /// </summary>
    [Fact]
    public void ProtectionMetaRefusesASharedBuffer()
    {
        using Buffer buffer = Buffer.New();
        using Structure info = Structure.NewEmpty("application/x-cenc");

        InvalidOperationException error = AssertRefusesWhileShared(
            buffer,
            () => buffer.AddProtectionMeta(info));

        Assert.Contains("gst_buffer_add_protection_meta", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same member on a buffer nobody else holds still attaches the item.
    /// </summary>
    [Fact]
    public void ProtectionMetaAttachesToAWritableBuffer()
    {
        using Buffer buffer = Buffer.New();
        using Structure info = Structure.NewEmpty("application/x-cenc");

        Assert.True(buffer.IsWritable);
        Assert.NotNull(buffer.AddProtectionMeta(info));
    }

    /// <summary>
    /// Attaching clipping metadata to a buffer somebody else holds is refused.
    /// </summary>
    [Fact]
    public void AudioClippingMetaRefusesASharedBuffer()
    {
        using Buffer buffer = Buffer.New();

        InvalidOperationException error = AssertRefusesWhileShared(
            buffer,
            () => AudioGlobal.BufferAddAudioClippingMeta(buffer, Format.Default, start: 10, end: 20));

        Assert.Contains("gst_buffer_add_audio_clipping_meta", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same member on a buffer nobody else holds still attaches the item.
    /// </summary>
    [Fact]
    public void AudioClippingMetaAttachesToAWritableBuffer()
    {
        using Buffer buffer = Buffer.New();

        Assert.True(buffer.IsWritable);
        Assert.NotNull(AudioGlobal.BufferAddAudioClippingMeta(buffer, Format.Default, start: 10, end: 20));
    }

    /// <summary>
    /// Attaching a network address to a buffer somebody else holds is refused.
    /// </summary>
    [Fact]
    public void NetAddressMetaRefusesASharedBuffer()
    {
        using Buffer buffer = Buffer.New();
        using SocketAddress address = LoopbackAddress();

        InvalidOperationException error = AssertRefusesWhileShared(
            buffer,
            () => NetGlobal.BufferAddNetAddressMeta(buffer, address));

        Assert.Contains("gst_buffer_add_net_address_meta", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same member on a buffer nobody else holds still attaches the item.
    /// </summary>
    [Fact]
    public void NetAddressMetaAttachesToAWritableBuffer()
    {
        using Buffer buffer = Buffer.New();
        using SocketAddress address = LoopbackAddress();

        Assert.True(buffer.IsWritable);
        Assert.NotNull(NetGlobal.BufferAddNetAddressMeta(buffer, address));
    }

    /// <summary>
    /// Both region of interest overloads refuse a buffer somebody else holds.
    /// This is the pair the upstream NULL dereference was found on.
    /// </summary>
    [Fact]
    public void BothRegionOfInterestOverloadsRefuseASharedBuffer()
    {
        using Buffer buffer = Buffer.New();

        InvalidOperationException byName = AssertRefusesWhileShared(
            buffer,
            () => VideoGlobal.BufferAddVideoRegionOfInterestMeta(buffer, "face", 1, 2, 3, 4));

        Assert.Contains(
            "gst_buffer_add_video_region_of_interest_meta",
            byName.Message,
            StringComparison.Ordinal);

        InvalidOperationException byQuark = AssertRefusesWhileShared(
            buffer,
            () => VideoGlobal.BufferAddVideoRegionOfInterestMetaId(
                buffer,
                Gst.GLib.Quark.FromString("face"),
                1,
                2,
                3,
                4));

        Assert.Contains(
            "gst_buffer_add_video_region_of_interest_meta_id",
            byQuark.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Both overloads still attach to a buffer nobody else holds.
    /// </summary>
    [Fact]
    public void BothRegionOfInterestOverloadsAttachToAWritableBuffer()
    {
        using Buffer buffer = Buffer.New();

        Assert.True(buffer.IsWritable);

        VideoRegionOfInterestMeta byName =
            VideoGlobal.BufferAddVideoRegionOfInterestMeta(buffer, "face", 1, 2, 3, 4);
        Assert.NotNull(byName);

        VideoRegionOfInterestMeta byQuark = VideoGlobal.BufferAddVideoRegionOfInterestMetaId(
            buffer,
            Gst.GLib.Quark.FromString("face"),
            5,
            6,
            7,
            8);
        Assert.NotNull(byQuark);
    }

    /// <summary>
    /// Builds a loopback address, which is the cheapest argument the network
    /// address adder accepts.
    /// </summary>
    /// <returns>The address, which the caller disposes.</returns>
    private static SocketAddress LoopbackAddress()
    {
        System.Net.SocketAddress wanted =
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5004).Serialize();

        SocketAddress? address = SocketAddress.FromSystemAddress(wanted);
        Assert.NotNull(address);
        return address;
    }

    /// <summary>
    /// Takes a second reference on the buffer, runs the adder, and reports the
    /// exception it refused the call with.
    /// </summary>
    /// <param name="buffer">The buffer to share for the duration of the call.</param>
    /// <param name="attach">The adder to run.</param>
    /// <returns>The exception the adder threw.</returns>
    /// <remarks>
    /// The extra reference is taken and released around the call rather than
    /// through a second wrapper, so that nothing but the buffer under test is
    /// left behind: the reference is the whole of what makes the buffer shared,
    /// and dropping it makes the wrapper writable again for the rest of the
    /// test. No GLib critical is printed, because the C is never reached.
    /// </remarks>
    private static InvalidOperationException AssertRefusesWhileShared(Buffer buffer, Action attach)
    {
        nint shared = TestNatives.MiniObjectRef(buffer.Handle);
        try
        {
            Assert.False(buffer.IsWritable);
            return Assert.Throws<InvalidOperationException>(attach);
        }
        finally
        {
            TestNatives.MiniObjectUnref(shared);
        }
    }
}
