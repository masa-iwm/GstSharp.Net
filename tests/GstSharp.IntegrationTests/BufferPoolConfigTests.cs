using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The configuration round trip of a <c>GstBufferPool</c> against the library
/// that is installed: read the configuration out, fill the mandatory
/// parameters in, apply it, and allocate from the pool it configured.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_buffer_pool_set_config</c> takes its structure over, which is a shape
/// the emitter refuses, so the whole <c>Config*</c> builder surface of the pool
/// used to be a dead end: a configuration could be read and filled in and never
/// applied. The call is hand written next to the generated class and this is
/// where its ownership is measured.
/// </para>
/// <para>
/// The refusal path is measured as well, because it is the one that is easy to
/// get wrong: the C function consumes the structure whether it accepts it or
/// not, so a wrapper that survived a <see langword="false"/> answer would be
/// holding a value the library has already freed.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class BufferPoolConfigTests
{
    [Fact]
    public void AConfigurationIsAppliedAndConsumed()
    {
        using BufferPool pool = BufferPool.New();
        using Caps caps = Caps.NewEmptySimple("video/x-raw");

        Structure config = pool.GetConfig();

        // The mandatory set the default pool parses out of a configuration.
        // Without them default_set_config refuses it.
        BufferPool.ConfigSetParams(config, caps, size: 1024, minBuffers: 2, maxBuffers: 4);

        Assert.True(pool.SetConfig(config));

        // What the caller had is gone: the call consumed the value of the
        // wrapper, so the wrapper is disposed and its handle is unreachable.
        Assert.True(config.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => config.Handle);

        // Disposing a second time is a no-op, so a using declaration around a
        // configuration that was handed over stays correct.
        config.Dispose();

        // And the pool kept what it was given, which is what reading the
        // configuration back says.
        using Structure applied = pool.GetConfig();

        Assert.True(BufferPool.ConfigGetParams(
            applied,
            out Caps? readBack,
            out uint size,
            out uint minBuffers,
            out uint maxBuffers));

        using (readBack)
        {
            Assert.NotNull(readBack);
            Assert.True(readBack.IsEqual(caps));
        }

        Assert.Equal(1024u, size);
        Assert.Equal(2u, minBuffers);
        Assert.Equal(4u, maxBuffers);

        // The configuration is real: the pool activates on it and hands out a
        // buffer of the size it was configured for.
        Assert.True(pool.SetActive(true));
        Assert.True(pool.IsActive());

        Assert.Equal(
            FlowReturn.Ok,
            pool.AcquireBuffer(out Gst.Buffer? buffer, default));

        using (buffer)
        {
            Assert.NotNull(buffer);
            Assert.Equal(1024u, (uint)buffer.GetSize());
        }

        Assert.True(pool.SetActive(false));
    }

    [Fact]
    public void ABufferNamesThePoolItWasAcquiredFrom()
    {
        using BufferPool pool = BufferPool.New();
        using Caps caps = Caps.NewEmptySimple("video/x-raw");

        Structure config = pool.GetConfig();
        BufferPool.ConfigSetParams(config, caps, size: 64, minBuffers: 1, maxBuffers: 2);
        Assert.True(pool.SetConfig(config));
        Assert.True(pool.SetActive(true));

        Assert.Equal(FlowReturn.Ok, pool.AcquireBuffer(out Gst.Buffer? pooled, default));

        using (pooled)
        {
            Assert.NotNull(pooled);

            // The field holds a reference of its own and the wrapper of a
            // GObject is interned, so the read answers the very instance the
            // pool was created as.
            BufferPool? answered = pooled.Pool;
            Assert.NotNull(answered);
            Assert.Same(pool, answered);
        }

        // A buffer no pool handed out says so with null: that is the normal
        // state of a buffer rather than a failure to read the field.
        using Gst.Buffer plain = Gst.Buffer.New();
        Assert.Null(plain.Pool);

        Assert.True(pool.SetActive(false));
    }

    [Fact]
    public void ARefusedConfigurationIsConsumedAsWell()
    {
        using BufferPool pool = BufferPool.New();

        // A configuration with no params at all: gst_buffer_pool_config_get_params
        // fails on it, so the default pool declines.
        Structure config = Structure.NewEmpty("nothing-the-pool-can-use");

        Assert.False(pool.SetConfig(config));

        // The C function frees the structure on the paths it declines on, and
        // stores it on the one where its own set_config declines, so the answer
        // never means that the caller still owns what it passed.
        Assert.True(config.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => config.Handle);
    }

    [Fact]
    public void AnActivePoolRefusesToBeReconfigured()
    {
        using BufferPool pool = BufferPool.New();
        using Caps caps = Caps.NewEmptySimple("video/x-raw");

        Structure first = pool.GetConfig();
        BufferPool.ConfigSetParams(first, caps, size: 512, minBuffers: 1, maxBuffers: 2);
        Assert.True(pool.SetConfig(first));
        Assert.True(pool.SetActive(true));

        Structure second = pool.GetConfig();
        BufferPool.ConfigSetParams(second, caps, size: 2048, minBuffers: 1, maxBuffers: 2);

        // "can't change config, we are active" - and the structure is freed on
        // that path too.
        Assert.False(pool.SetConfig(second));
        Assert.True(second.IsDisposed);

        // The configuration it was activated with is the one that is still in
        // force, which is what the refusal is there to protect.
        using Structure still = pool.GetConfig();
        Assert.True(BufferPool.ConfigGetParams(still, out Caps? unused, out uint size, out _, out _));
        unused?.Dispose();
        Assert.Equal(512u, size);

        Assert.True(pool.SetActive(false));
    }
}
