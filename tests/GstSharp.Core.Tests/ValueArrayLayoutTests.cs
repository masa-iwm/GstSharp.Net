using System.Runtime.CompilerServices;
using Gst.GObject;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The one fact that makes a <see cref="System.Span{T}"/> of
/// <see cref="Value"/> a <c>GValue</c> array: the two have the same size.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_control_binding_get_g_value_array</c> and
/// <c>gst_object_get_g_value_array</c> are handed the address of such a span
/// and a count beside it, and write the count at the stride of a
/// <c>GValue</c>. A <see cref="Value"/> that grew a second field would be
/// written across two slots from the second one on, and nothing about the
/// call would fail: the two members check the length the cast answers for
/// exactly that reason, and this pins the size itself, here where no
/// GStreamer installation is needed.
/// </para>
/// <para>
/// Nothing native is touched: <see cref="Unsafe.SizeOf{T}"/> is a property of
/// the two types and of the runtime that lays them out.
/// </para>
/// </remarks>
public class ValueArrayLayoutTests
{
    /// <summary>A value is one native value and nothing else.</summary>
    [Fact]
    public void AValueHasTheSizeOfAGValue() =>
        Assert.Equal(Unsafe.SizeOf<GValueNative>(), Unsafe.SizeOf<Value>());
}
