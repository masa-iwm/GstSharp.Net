using System.Runtime.CompilerServices;
using Gst.GObject;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The parts of the dynamic signal layer that hold without a GStreamer
/// installation: the mirrors of the two GObject structures it allocates or
/// reads, the flags it exposes, and the argument checks that happen before any
/// native call.
/// </summary>
/// <remarks>
/// Everything else about emitting and connecting by name needs a real object,
/// so it lives in the integration suite.
/// </remarks>
public class SignalIntrospectionTests
{
    /// <summary>
    /// <c>struct _GSignalQuery</c> of <c>gsignal.h</c>: <c>guint signal_id</c>,
    /// <c>const gchar *signal_name</c>, <c>GType itype</c>,
    /// <c>GSignalFlags signal_flags</c>, <c>GType return_type</c>,
    /// <c>guint n_params</c> and <c>const GType *param_types</c>, every one of
    /// them aligned to a pointer.
    /// </summary>
    [Fact]
    public unsafe void SignalQueryMirrorsTheHeaderLayout()
    {
        GSignalQuery query = default;
        int slot = nint.Size;

        Assert.Equal(7 * slot, Unsafe.SizeOf<GSignalQuery>());

        Assert.Equal(0L, Offset(&query, &query.SignalId));
        Assert.Equal((long)slot, Offset(&query, &query.SignalName));
        Assert.Equal(2L * slot, Offset(&query, &query.IType));
        Assert.Equal(3L * slot, Offset(&query, &query.SignalFlags));
        Assert.Equal(4L * slot, Offset(&query, &query.ReturnType));
        Assert.Equal(5L * slot, Offset(&query, &query.NParams));
        Assert.Equal(6L * slot, Offset(&query, &query.ParamTypes));
    }

    /// <summary>
    /// <c>struct _GClosure</c> of <c>gclosure.h</c>: the bit fields in one
    /// <c>guint</c>, followed by the <c>marshal</c>, <c>data</c> and
    /// <c>notifiers</c> pointers. Only the size matters, because it is what
    /// <c>g_closure_new_simple</c> allocates.
    /// </summary>
    [Fact]
    public void ClosureMirrorMatchesTheHeaderSize() =>
        Assert.Equal(4 * nint.Size, Unsafe.SizeOf<DynamicSignalClosure.GClosureLayout>());

    /// <summary>
    /// The flags are the values of <c>GSignalFlags</c>, and the one the action
    /// signals of GStreamer are recognised by is bit five.
    /// </summary>
    [Fact]
    public void SignalFlagsMatchGObject()
    {
        Assert.Equal(0, (int)SignalFlags.None);
        Assert.Equal(1, (int)SignalFlags.RunFirst);
        Assert.Equal(2, (int)SignalFlags.RunLast);
        Assert.Equal(4, (int)SignalFlags.RunCleanup);
        Assert.Equal(8, (int)SignalFlags.NoRecurse);
        Assert.Equal(16, (int)SignalFlags.Detailed);
        Assert.Equal(32, (int)SignalFlags.Action);
        Assert.Equal(64, (int)SignalFlags.NoHooks);
        Assert.Equal(128, (int)SignalFlags.MustCollect);
        Assert.Equal(256, (int)SignalFlags.Deprecated);
        Assert.Equal(1 << 17, (int)SignalFlags.AccumulatorFirstRun);
    }

    /// <summary>
    /// A query that found nothing describes itself as such rather than as a
    /// signal without arguments.
    /// </summary>
    [Fact]
    public void AnEmptyQueryIsInvalid()
    {
        SignalQuery query = default;

        Assert.False(query.IsValid);
        Assert.Equal(0u, query.Id);
        Assert.Equal(0, query.ParameterCount);
        Assert.Empty(query.GetParameterTypes());
        Assert.Equal("<unknown signal>", query.ToString());
        Assert.Equal(default, query);
    }

    /// <summary>
    /// A value cannot hold an invalid type, and the argument is refused before
    /// anything native is called.
    /// </summary>
    [Fact]
    public void CreateForRejectsATypeNoValueCanHold()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Value.CreateFor(42, GType.Invalid));

        Assert.Equal("expected", exception.ParamName);
        Assert.Contains("invalid", exception.Message, StringComparison.Ordinal);
    }

    private static unsafe long Offset(void* start, void* field) => (byte*)field - (byte*)start;
}
