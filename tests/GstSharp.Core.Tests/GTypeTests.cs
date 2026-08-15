using Gst.GObject;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The fundamental type constants, which are pure values and need no native
/// library.
/// </summary>
public class GTypeTests
{
    [Fact]
    public void FundamentalConstantsMatchGLib()
    {
        Assert.Equal(0u, (uint)GType.Invalid.Value);
        Assert.Equal(4u, (uint)GType.None.Value);
        Assert.Equal(8u, (uint)GType.Interface.Value);
        Assert.Equal(12u, (uint)GType.Char.Value);
        Assert.Equal(16u, (uint)GType.UChar.Value);
        Assert.Equal(20u, (uint)GType.Boolean.Value);
        Assert.Equal(24u, (uint)GType.Int.Value);
        Assert.Equal(28u, (uint)GType.UInt.Value);
        Assert.Equal(32u, (uint)GType.Long.Value);
        Assert.Equal(36u, (uint)GType.ULong.Value);
        Assert.Equal(40u, (uint)GType.Int64.Value);
        Assert.Equal(44u, (uint)GType.UInt64.Value);
        Assert.Equal(48u, (uint)GType.Enum.Value);
        Assert.Equal(52u, (uint)GType.Flags.Value);
        Assert.Equal(56u, (uint)GType.Float.Value);
        Assert.Equal(60u, (uint)GType.Double.Value);
        Assert.Equal(64u, (uint)GType.String.Value);
        Assert.Equal(68u, (uint)GType.Pointer.Value);
        Assert.Equal(72u, (uint)GType.Boxed.Value);
        Assert.Equal(76u, (uint)GType.Param.Value);
        Assert.Equal(80u, (uint)GType.Object.Value);
        Assert.Equal(84u, (uint)GType.Variant.Value);
    }

    [Fact]
    public void InvalidIsNotValid()
    {
        Assert.False(GType.Invalid.IsValid);
        Assert.True(GType.Int.IsValid);
    }

    [Fact]
    public void EqualityComparesTheRawValue()
    {
        GType left = new(24);
        GType right = GType.Int;

        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.True(left.Equals((object)right));
    }
}
