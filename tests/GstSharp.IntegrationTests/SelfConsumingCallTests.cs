using Gst;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The conversions that consume the reference of the instance they are called
/// on and answer a value of their own: <c>gst_caps_truncate</c> and its
/// relatives, <c>gst_buffer_append</c> and <c>gst_memory_make_mapped</c>, plus
/// the hand written <c>Caps.Fixate</c> that guards the one path of the family
/// which consumes nothing.
/// </summary>
/// <remarks>
/// The binding hands the call a reference minted for it, so the interesting
/// question is what is left afterwards: the wrapper the member was called on
/// has to be alive and unchanged, the answer has to be a second wrapper, and
/// the two may stand for the very same native object.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SelfConsumingCallTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SelfConsumingCallTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The wrapper the conversion was called on keeps the caps it stands for,
    /// and the answer is a second wrapper with the truncated caps in it.
    /// </summary>
    [Fact]
    public void TruncateAnswersASecondWrapperAndLeavesTheFirstOneAlone()
    {
        using Caps caps = Assert.IsType<Caps>(
            Caps.FromString("video/x-raw,width=(int)320;audio/x-raw,rate=(int)44100"));
        nint before = caps.Handle;
        Assert.Equal(2u, caps.GetSize());

        using Caps truncated = caps.Truncate();

        // The mint made the caps shared for the length of the call, so the C
        // side copied rather than rewriting them in place.
        Assert.NotSame(caps, truncated);
        Assert.NotEqual(before, truncated.Handle);
        Assert.Equal(1u, truncated.GetSize());

        // Nothing happened to the wrapper the call was made on.
        Assert.Equal(before, caps.Handle);
        Assert.Equal(2u, caps.GetSize());

        using Structure first = truncated.GetStructure(0);
        Assert.Equal("video/x-raw", first.GetName());
    }

    /// <summary>
    /// The documented shared path: caps of one structure are already truncated,
    /// so the C function answers the very caps it was given and the two
    /// wrappers stand for one object.
    /// </summary>
    [Fact]
    public void TruncateCanAnswerTheSameNativeCapsAsTheInstance()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString("video/x-raw,width=(int)320"));
        nint before = caps.Handle;

        using Caps truncated = caps.Truncate();

        _output.WriteLine(FormattableString.Invariant(
            $"truncate: {before:X} -> {truncated.Handle:X}"));
        Assert.NotSame(caps, truncated);
        Assert.Equal(before, truncated.Handle);

        // Two wrappers hold two references of one object, so neither of them
        // may write it. Both are disposed by the using declarations, which is
        // what makes the books balance.
        Assert.False(caps.IsWritable);
        Assert.False(truncated.IsWritable);
    }

    /// <summary>
    /// The second caps are consumed and the instance is not: the wrapper that
    /// was merged in is disposed, the one the call was made on is not.
    /// </summary>
    [Fact]
    public void MergeConsumesTheArgumentAndKeepsTheInstance()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString("video/x-raw,width=(int)320"));
        Caps other = Assert.IsType<Caps>(Caps.FromString("audio/x-raw,rate=(int)44100"));

        using Caps merged = caps.Merge(other);

        Assert.True(other.IsDisposed);
        Assert.False(caps.IsDisposed);
        Assert.Equal(1u, caps.GetSize());
        Assert.Equal(2u, merged.GetSize());
    }

    /// <summary>
    /// Appending answers a buffer that carries the memory of both, and leaves
    /// the wrapper it was called on holding its own buffer.
    /// </summary>
    [Fact]
    public void AppendAnswersASecondBufferAndConsumesTheArgument()
    {
        using Buffer first = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 8, null));
        Buffer second = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 4, null));

        using Buffer joined = first.Append(second);

        Assert.True(second.IsDisposed);
        Assert.False(first.IsDisposed);
        Assert.Equal((nuint)8, first.GetSize());
        Assert.Equal((nuint)12, joined.GetSize());
    }

    /// <summary>
    /// The answer of <c>gst_memory_make_mapped</c> is mapped, and unmapping it
    /// is the caller's. The wrapper the call was made on keeps its own memory.
    /// </summary>
    [Fact]
    public void MakeMappedAnswersMappedMemoryTheCallerUnmaps()
    {
        using Allocator allocator = Assert.IsAssignableFrom<Allocator>(Allocator.Find(null));
        using Memory memory = Assert.IsType<Memory>(allocator.Alloc(16, null));

        Memory? mapped = memory.MakeMapped(out MapInfo info, MapFlags.Read);

        // NULL is a normal answer of this call - it means the memory could
        // neither be mapped nor copied - and the default allocator maps, so
        // this run has to see a value.
        Assert.NotNull(mapped);

        using (mapped)
        {
            Assert.False(memory.IsDisposed);
            Assert.Equal((nuint)16, info.Size);

            // The mapping is on the memory that was answered, which is this one
            // because it could be mapped as it was.
            mapped.Unmap(info);
        }
    }

    /// <summary>
    /// Fixating replaces the ranges of the first structure by one value each
    /// and drops every other structure.
    /// </summary>
    [Fact]
    public void FixateAnswersFixedCaps()
    {
        using Caps caps = Assert.IsType<Caps>(
            Caps.FromString("video/x-raw,width=(int)[1,320];audio/x-raw,rate=(int)44100"));

        using Caps fixated = caps.Fixate();

        Assert.NotSame(caps, fixated);
        Assert.True(fixated.IsFixed());
        Assert.Equal(1u, fixated.GetSize());
        Assert.False(caps.IsDisposed);

        using Structure structure = fixated.GetStructure(0);
        Assert.True(structure.GetInt("width", out int width));
        Assert.Equal(1, width);
    }

    /// <summary>
    /// ANY caps are the one input <c>gst_caps_fixate</c> refuses, and it
    /// refuses them without consuming anything. The hand written member makes
    /// the check before it mints, so the wrapper is untouched afterwards.
    /// </summary>
    [Fact]
    public void FixateRefusesAnyCaps()
    {
        using Caps caps = Caps.NewAny();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => caps.Fixate());

        Assert.Contains("ANY caps", error.Message, StringComparison.Ordinal);
        Assert.False(caps.IsDisposed);
        Assert.True(caps.IsAny());
    }
}
