using Gst.Base;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Exercises the mapping glue of <see cref="Adapter"/> against a real adapter.
/// </summary>
/// <remarks>
/// <c>gst_adapter_map</c> and <c>gst_adapter_unmap</c> are both on the skip list
/// of <c>girs/overlays/fixups.json</c>, so <see cref="Adapter.Map(nuint)"/> and
/// the scope it returns are the only mapping surface an adapter has. What is
/// measured here is that the borrowed pointer is read with the right length and
/// given back exactly once.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AdapterMapTests
{
    private static readonly byte[] Payload = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80];

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public AdapterMapTests(ITestOutputHelper output)
    {
        _output = output;

        // See Gst.Base.GstBase: the type registry only knows GstAdapter once the
        // module initialiser of GstSharp.Net.Base has run.
        GstBase.Initialize();
    }

    /// <summary>
    /// What was pushed into the adapter is what the mapping reads back, and the
    /// bytes may only be flushed once the mapping is released.
    /// </summary>
    [Fact]
    public void MapReadsBackWhatWasPushed()
    {
        using Adapter adapter = Adapter.New();
        Push(adapter, Payload);

        Assert.Equal((nuint)Payload.Length, adapter.Available());

        using (Adapter.MapScope map = adapter.Map((nuint)Payload.Length))
        {
            _output.WriteLine(FormattableString.Invariant(
                $"mapping: size={map.Size}, span={map.Span.Length}"));

            Assert.Equal((nuint)Payload.Length, map.Size);
            Assert.Equal(Payload, map.Span.ToArray());
        }

        // Only now, with the mapping released: any call on the adapter
        // invalidates the memory the scope handed out.
        adapter.Flush((nuint)Payload.Length);
        Assert.Equal((nuint)0, adapter.Available());
    }

    /// <summary>
    /// A mapping of fewer bytes than the adapter holds is a prefix of them, so
    /// the size that is asked for is the length of the span and nothing else.
    /// </summary>
    [Fact]
    public void MapOfAPrefixSeesOnlyThatPrefix()
    {
        using Adapter adapter = Adapter.New();
        Push(adapter, Payload);

        using Adapter.MapScope map = adapter.Map(4);

        Assert.Equal((nuint)4, map.Size);
        Assert.Equal(Payload[..4], map.Span.ToArray());
    }

    /// <summary>
    /// Mapping more bytes than the adapter holds is refused. The number to check
    /// against is <see cref="Adapter.Available"/>, and the message says so.
    /// </summary>
    [Fact]
    public void MapBeyondWhatIsAvailableIsRefused()
    {
        using Adapter adapter = Adapter.New();
        Push(adapter, Payload);

        nuint tooMany = (nuint)Payload.Length + 1;
        Assert.Equal((nuint)Payload.Length, adapter.Available());

        // A ref struct cannot be captured by a lambda, so Assert.Throws is out.
        InvalidOperationException? refused = null;
        try
        {
            using Adapter.MapScope map = adapter.Map(tooMany);
            Assert.Fail("An adapter must not map more bytes than it holds.");
        }
        catch (InvalidOperationException error)
        {
            refused = error;
        }

        Assert.NotNull(refused);
        _output.WriteLine(refused.Message);
        Assert.Contains("Available()", refused.Message, StringComparison.Ordinal);

        // The adapter is untouched by the refusal and still maps what it has.
        using Adapter.MapScope second = adapter.Map((nuint)Payload.Length);
        Assert.Equal((nuint)Payload.Length, second.Size);
    }

    /// <summary>
    /// Releasing a mapping twice unmaps once. The second release is a no-op, and
    /// the accessors refuse to hand out memory that was given back.
    /// </summary>
    [Fact]
    public void DisposingTheMappingTwiceUnmapsOnce()
    {
        using Adapter adapter = Adapter.New();
        Push(adapter, Payload);

        Adapter.MapScope map = adapter.Map((nuint)Payload.Length);
        map.Dispose();
        map.Dispose();

        ObjectDisposedException? refused = null;
        try
        {
            _ = map.Size;
        }
        catch (ObjectDisposedException error)
        {
            refused = error;
        }

        Assert.NotNull(refused);

        // The adapter is still usable: the mapping was released exactly once.
        using Adapter.MapScope second = adapter.Map((nuint)Payload.Length);
        Assert.Equal(Payload, second.Span.ToArray());
    }

    /// <summary>
    /// Puts a buffer with the given bytes into the adapter.
    /// </summary>
    /// <param name="adapter">The adapter to push into.</param>
    /// <param name="payload">The bytes of the buffer.</param>
    /// <remarks>
    /// <c>gst_adapter_push</c> takes the reference of the buffer over, and the
    /// wrapper of <see cref="Buffer.NewMemdup"/> owns the only one there is, so
    /// the push gets a reference of its own — this is the
    /// <c>gst_buffer_ref()</c> before the push that the documentation of
    /// <c>GstAdapter</c> asks for. The wrapper then gives its own reference back
    /// as usual, and the adapter is left holding the buffer alone.
    /// </remarks>
    private static void Push(Adapter adapter, ReadOnlySpan<byte> payload)
    {
        using Buffer buffer = Buffer.NewMemdup(payload);

        TestNatives.MiniObjectRef(buffer.Handle);
        TestNatives.AdapterPush(adapter.Handle, buffer.Handle);
        GC.KeepAlive(adapter);
    }
}
