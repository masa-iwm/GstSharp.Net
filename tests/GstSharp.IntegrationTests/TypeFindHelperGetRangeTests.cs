using Gst;
using Gst.Base;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="BaseGlobal.TypeFindHelperGetRange"/> and
/// <see cref="BaseGlobal.TypeFindHelperGetRangeFull"/>: typefinding over data
/// that a managed function reads out range by range.
/// </summary>
/// <remarks>
/// <para>
/// The data is the block of <see cref="TypeFindProbe"/>, and the probe
/// typefinder is what recognises it, so a match proves that the bytes the
/// function handed out reached the typefinder intact.
/// </para>
/// <para>
/// What is measured beyond the match is the ownership of the buffers: the
/// helper must be handed a reference of its own for an <c>Ok</c> answer and
/// none for any other answer, and must have released everything it was handed
/// by the time the member returns. A buffer the test keeps a second wrapper of
/// is writable again exactly when no other reference is left.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class TypeFindHelperGetRangeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public TypeFindHelperGetRangeTests(ITestOutputHelper output)
    {
        _output = output;

        // See Gst.Base.GstBase: the helpers below live in GstBase, and its
        // module initialiser is what puts its types into the registry.
        GstBase.Initialize();

        TypeFindProbe.EnsureRegistered();
    }

    /// <summary>
    /// The function is handed the object and parent the member was called
    /// with, asked for at least 4096 bytes from the start, and the probe
    /// recognises the bytes it handed out.
    /// </summary>
    [Fact]
    public void GetRangeFindsTheTypeOfTheDataTheFunctionReads()
    {
        using Bin obj = Bin.New(null);
        List<(ulong Offset, uint Length)> calls = [];
        bool sameObject = true;
        bool nullParent = true;
        PeekRecord record = new();
        Caps? found;
        TypeFindProbability probability;

        FlowReturn Read(Gst.Object o, Gst.Object? parent, ulong offset, uint length, out Buffer? buffer)
        {
            sameObject &= ReferenceEquals(o, obj);
            nullParent &= parent is null;
            calls.Add((offset, length));
            return Serve(TypeFindProbe.Payload, offset, length, out buffer);
        }

        TypeFindProbe.Current = record;
        try
        {
            found = BaseGlobal.TypeFindHelperGetRange(
                obj, null, Read, (ulong)TypeFindProbe.Payload.Length, null, out probability);
        }
        finally
        {
            TypeFindProbe.Current = null;
        }

        using (found)
        {
            _output.WriteLine(string.Join(", ", calls.Select(c => FormattableString.Invariant($"({c.Offset}, {c.Length})"))));

            Assert.True(record.Calls > 0, "The probe typefinder was never called over the data.");
            Assert.True(record.HeadPeeked);
            Assert.Equal(TypeFindProbe.Magic, record.Head);
            Assert.True(record.FillerPeeked);
            Assert.Equal(
                TypeFindProbe.Payload[TypeFindProbe.Magic.Length..(TypeFindProbe.Magic.Length + 4)],
                record.Filler);

            Assert.Contains(calls, c => c.Offset == 0 && c.Length >= 4096);
            Assert.True(sameObject, "The function was handed another object than the one the member was called with.");
            Assert.True(nullParent, "The function was handed a parent although none was given.");

            Assert.NotNull(found);
            Assert.Equal(TypeFindProbability.Maximum, probability);
            Assert.Contains(TypeFindProbe.MediaType, found.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The parent the member was called with is the instance the function is
    /// handed.
    /// </summary>
    [Fact]
    public void TheParentIsHandedBackAsTheSameInstance()
    {
        using Bin obj = Bin.New(null);
        using Bin parent = Bin.New(null);
        bool sameParent = true;
        int calls = 0;

        FlowReturn Read(Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer)
        {
            calls++;
            sameParent &= ReferenceEquals(p, parent);
            return Serve(TypeFindProbe.Payload, offset, length, out buffer);
        }

        using Caps? found = BaseGlobal.TypeFindHelperGetRange(
            obj, parent, Read, (ulong)TypeFindProbe.Payload.Length, null, out _);

        Assert.True(calls > 0);
        Assert.True(sameParent);
        Assert.NotNull(found);
    }

    /// <summary>The full variant answers <c>Ok</c> together with the caps.</summary>
    [Fact]
    public void GetRangeFullAnswersOkWithTheCaps()
    {
        using Bin obj = Bin.New(null);

        FlowReturn result = BaseGlobal.TypeFindHelperGetRangeFull(
            obj,
            null,
            (Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer) =>
                Serve(TypeFindProbe.Payload, offset, length, out buffer),
            (ulong)TypeFindProbe.Payload.Length,
            null,
            out Caps? caps,
            out TypeFindProbability probability);

        using (caps)
        {
            Assert.Equal(FlowReturn.Ok, result);
            Assert.NotNull(caps);
            Assert.Equal(TypeFindProbability.Maximum, probability);
            Assert.Contains(TypeFindProbe.MediaType, caps.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>Data that no typefinder recognises comes back without caps.</summary>
    [Fact]
    public void DataNoTypefinderRecognisesComesBackWithoutCaps()
    {
        using Bin obj = Bin.New(null);
        byte[] nonsense = BuildNonsense();

        FlowReturn Read(Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer) =>
            Serve(nonsense, offset, length, out buffer);

        using Caps? found = BaseGlobal.TypeFindHelperGetRange(obj, null, Read, (ulong)nonsense.Length, null, out _);
        Assert.Null(found);

        FlowReturn result = BaseGlobal.TypeFindHelperGetRangeFull(
            obj, null, Read, (ulong)nonsense.Length, null, out Caps? caps, out _);
        using (caps)
        {
            _output.WriteLine($"full: {result}");
            Assert.Null(caps);
        }
    }

    /// <summary>
    /// An exception thrown by the function is reported and answered as an
    /// error, which ends the typefinding without caps.
    /// </summary>
    [Fact]
    public void AnExceptionOfTheFunctionIsReportedAndEndsTheTypefinding()
    {
        using Bin obj = Bin.New(null);
        InvalidOperationException thrown = new("The range reading function threw.");
        int reports = 0;

        void OnFailure(Exception exception)
        {
            // The event is process wide, so only the instance this test threw
            // says anything about this test.
            if (ReferenceEquals(exception, thrown))
            {
                Interlocked.Increment(ref reports);
            }
        }

        Caps? found;
        FlowReturn result;
        Caps? fullCaps;

        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            found = BaseGlobal.TypeFindHelperGetRange(
                obj, null, (Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer) => throw thrown,
                (ulong)TypeFindProbe.Payload.Length, null, out _);
            result = BaseGlobal.TypeFindHelperGetRangeFull(
                obj, null, (Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer) => throw thrown,
                (ulong)TypeFindProbe.Payload.Length, null, out fullCaps, out _);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        using (found)
        using (fullCaps)
        {
            Assert.True(reports >= 2, $"The trap saw {reports} reports.");
            Assert.Null(found);
            Assert.NotEqual(FlowReturn.Ok, result);
            Assert.Null(fullCaps);
        }
    }

    /// <summary>
    /// A buffer handed out with an answer other than <c>Ok</c> gets no
    /// reference minted for the helper, and the wrapper of the function is
    /// released: the one wrapper the test kept holds the only reference left.
    /// </summary>
    [Fact]
    public void ABufferHandedOutWithAnErrorIsReleased()
    {
        using Bin obj = Bin.New(null);
        List<Buffer> kept = [];

        FlowReturn Read(Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer)
        {
            Buffer b = Buffer.NewMemdup(TypeFindProbe.Payload);
            kept.Add(Buffer.FromNative(GstNative.MiniObjectRef(b.Handle), Transfer.Full)!);
            buffer = b;
            return FlowReturn.Error;
        }

        try
        {
            FlowReturn result = BaseGlobal.TypeFindHelperGetRangeFull(
                obj, null, Read, (ulong)TypeFindProbe.Payload.Length, null, out Caps? caps, out _);
            using (caps)
            {
                Assert.NotEqual(FlowReturn.Ok, result);
                Assert.Null(caps);
            }

            Assert.NotEmpty(kept);
            Assert.All(kept, buffer => Assert.True(buffer.IsWritable, "A reference of the buffer outlived the call."));
        }
        finally
        {
            kept.ForEach(buffer => buffer.Dispose());
        }
    }

    /// <summary>
    /// A buffer handed out with <c>Ok</c> is given to the helper with a
    /// reference of its own, and the helper has released that reference by the
    /// time the member returns.
    /// </summary>
    [Fact]
    public void ABufferHandedOutWithOkIsReleasedByTheTimeTheMemberReturns()
    {
        using Bin obj = Bin.New(null);
        List<Buffer> kept = [];

        FlowReturn Read(Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer)
        {
            FlowReturn answer = Serve(TypeFindProbe.Payload, offset, length, out buffer);
            if (buffer is not null)
            {
                kept.Add(Buffer.FromNative(GstNative.MiniObjectRef(buffer.Handle), Transfer.Full)!);
            }

            return answer;
        }

        try
        {
            using (Caps? found = BaseGlobal.TypeFindHelperGetRange(
                obj, null, Read, (ulong)TypeFindProbe.Payload.Length, null, out _))
            {
                Assert.NotNull(found);
            }

            Assert.NotEmpty(kept);
            Assert.All(kept, buffer => Assert.True(buffer.IsWritable, "The helper kept a reference of the buffer."));
        }
        finally
        {
            kept.ForEach(buffer => buffer.Dispose());
        }
    }

    /// <summary>
    /// <c>Ok</c> without a buffer is answered as an error rather than handed to
    /// a helper that would read the buffer without checking it.
    /// </summary>
    [Fact]
    public void OkWithoutABufferDoesNotCrash()
    {
        using Bin obj = Bin.New(null);
        int calls = 0;

        FlowReturn Read(Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer)
        {
            calls++;
            buffer = null;
            return FlowReturn.Ok;
        }

        FlowReturn result = BaseGlobal.TypeFindHelperGetRangeFull(
            obj, null, Read, (ulong)TypeFindProbe.Payload.Length, null, out Caps? caps, out _);
        using (caps)
        {
            _output.WriteLine($"full: {result}, calls: {calls}");
            Assert.True(calls > 0);
            Assert.Null(caps);

            // Every answer becomes Error, and the first typefinder that reads
            // ends the walk with it (gsttypefindhelper.c:422-428).
            Assert.Equal(FlowReturn.Error, result);
        }
    }

    /// <summary>
    /// A function may start a typefinding of its own; each is served by its
    /// own function and both find their data.
    /// </summary>
    [Fact]
    public void AFunctionMayStartAnotherTypefinding()
    {
        using Bin outer = Bin.New(null);
        using Bin inner = Bin.New(null);
        Caps? innerFound = null;
        bool innerServed = false;
        bool innerSawOuter = false;

        FlowReturn ReadInner(Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer)
        {
            innerServed = true;
            innerSawOuter |= !ReferenceEquals(o, inner);
            return Serve(TypeFindProbe.Payload, offset, length, out buffer);
        }

        FlowReturn ReadOuter(Gst.Object o, Gst.Object? p, ulong offset, uint length, out Buffer? buffer)
        {
            Assert.Same(outer, o);
            if (!innerServed)
            {
                innerFound = BaseGlobal.TypeFindHelperGetRange(
                    inner, null, ReadInner, (ulong)TypeFindProbe.Payload.Length, null, out _);
            }

            return Serve(TypeFindProbe.Payload, offset, length, out buffer);
        }

        using Caps? outerFound = BaseGlobal.TypeFindHelperGetRange(
            outer, null, ReadOuter, (ulong)TypeFindProbe.Payload.Length, null, out _);
        using (innerFound)
        {
            Assert.True(innerServed);
            Assert.False(innerSawOuter);
            Assert.NotNull(innerFound);
            Assert.NotNull(outerFound);
            Assert.Contains(TypeFindProbe.MediaType, innerFound.ToString(), StringComparison.Ordinal);
            Assert.Contains(TypeFindProbe.MediaType, outerFound.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Serves one range of a block the way a well behaved source does: the
    /// bytes that are there, and <c>Eos</c> past the end.
    /// </summary>
    private static FlowReturn Serve(byte[] data, ulong offset, uint length, out Buffer? buffer)
    {
        if (offset >= (ulong)data.Length)
        {
            buffer = null;
            return FlowReturn.Eos;
        }

        int count = (int)Math.Min(length, (ulong)data.Length - offset);
        buffer = Buffer.NewMemdup(data.AsSpan((int)offset, count));

        // A buffer that says where it starts lets the helper serve later reads
        // of the same range from it instead of calling again.
        buffer.SetOffset(offset);
        return FlowReturn.Ok;
    }

    /// <summary>Builds a block that no typefinder of the registry claims.</summary>
    private static byte[] BuildNonsense()
    {
        byte[] data = new byte[64];
        for (int index = 0; index < data.Length; index++)
        {
            data[index] = (byte)((index * 37) + 11);
        }

        return data;
    }
}
