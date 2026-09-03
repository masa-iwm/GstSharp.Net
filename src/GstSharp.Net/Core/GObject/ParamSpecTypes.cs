using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// A <c>GParamSpecBoolean</c>: a property that carries a boolean.
/// </summary>
/// <remarks>
/// The field is read at the offset of the public structure, because GObject
/// exposes it through a macro rather than through an accessor. The base class
/// already reads the description in general — the name, the flags and the type
/// of the values — and this adds what only a boolean property has.
/// </remarks>
public sealed unsafe class ParamSpecBoolean : ParamSpec
{
    internal ParamSpecBoolean(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public bool Default
    {
        get
        {
            bool value = *(int*)((byte*)Handle + FieldsOffset) != 0;
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecChar</c>: a property that carries a signed byte out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecChar : ParamSpec
{
    internal ParamSpecChar(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public sbyte Minimum
    {
        get
        {
            sbyte value = *(sbyte*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public sbyte Maximum
    {
        get
        {
            sbyte value = *(sbyte*)((byte*)Handle + FieldsOffset + 1);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public sbyte Default
    {
        get
        {
            sbyte value = *(sbyte*)((byte*)Handle + FieldsOffset + 2);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecUChar</c>: a property that carries an unsigned byte out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecUChar : ParamSpec
{
    internal ParamSpecUChar(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public byte Minimum
    {
        get
        {
            byte value = *((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public byte Maximum
    {
        get
        {
            byte value = *((byte*)Handle + FieldsOffset + 1);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public byte Default
    {
        get
        {
            byte value = *((byte*)Handle + FieldsOffset + 2);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecInt</c>: a property that carries a 32 bit integer out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecInt : ParamSpec
{
    internal ParamSpecInt(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public int Minimum
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public int Maximum
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 4);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public int Default
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecUInt</c>: a property that carries an unsigned 32 bit integer
/// out of a range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecUInt : ParamSpec
{
    internal ParamSpecUInt(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public uint Minimum
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public uint Maximum
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset + 4);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public uint Default
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecLong</c>: a property that carries a C <c>long</c> out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only. A C <c>long</c> is 32 bits wide on
/// Windows and 64 bits wide everywhere else, so the offsets are computed from
/// <see cref="CLong"/> rather than written out, and the value is widened to
/// <see cref="long"/> on both.
/// </remarks>
public sealed unsafe class ParamSpecLong : ParamSpec
{
    internal ParamSpecLong(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public long Minimum
    {
        get
        {
            long value = Read(Handle, FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public long Maximum
    {
        get
        {
            long value = Read(Handle, FieldsOffset + sizeof(CLong));
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public long Default
    {
        get
        {
            long value = Read(Handle, FieldsOffset + (2 * sizeof(CLong)));
            GC.KeepAlive(this);
            return value;
        }
    }

    private static long Read(nint pspec, int offset) =>
        sizeof(CLong) == 4 ? *(int*)((byte*)pspec + offset) : *(long*)((byte*)pspec + offset);
}

/// <summary>
/// A <c>GParamSpecULong</c>: a property that carries an unsigned C <c>long</c>
/// out of a range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only. An unsigned C <c>long</c> is 32
/// bits wide on Windows and 64 bits wide everywhere else, so the offsets are
/// computed from <see cref="CULong"/> rather than written out, and the value is
/// widened to <see cref="ulong"/> on both.
/// </remarks>
public sealed unsafe class ParamSpecULong : ParamSpec
{
    internal ParamSpecULong(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public ulong Minimum
    {
        get
        {
            ulong value = Read(Handle, FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public ulong Maximum
    {
        get
        {
            ulong value = Read(Handle, FieldsOffset + sizeof(CULong));
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public ulong Default
    {
        get
        {
            ulong value = Read(Handle, FieldsOffset + (2 * sizeof(CULong)));
            GC.KeepAlive(this);
            return value;
        }
    }

    private static ulong Read(nint pspec, int offset) =>
        sizeof(CULong) == 4 ? *(uint*)((byte*)pspec + offset) : *(ulong*)((byte*)pspec + offset);
}

/// <summary>
/// A <c>GParamSpecInt64</c>: a property that carries a 64 bit integer out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecInt64 : ParamSpec
{
    internal ParamSpecInt64(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public long Minimum
    {
        get
        {
            long value = *(long*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public long Maximum
    {
        get
        {
            long value = *(long*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public long Default
    {
        get
        {
            long value = *(long*)((byte*)Handle + FieldsOffset + 16);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecUInt64</c>: a property that carries an unsigned 64 bit
/// integer out of a range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecUInt64 : ParamSpec
{
    internal ParamSpecUInt64(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public ulong Minimum
    {
        get
        {
            ulong value = *(ulong*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public ulong Maximum
    {
        get
        {
            ulong value = *(ulong*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public ulong Default
    {
        get
        {
            ulong value = *(ulong*)((byte*)Handle + FieldsOffset + 16);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecFloat</c>: a property that carries a single precision number
/// out of a range.
/// </summary>
/// <remarks>
/// The four fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecFloat : ParamSpec
{
    internal ParamSpecFloat(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public float Minimum
    {
        get
        {
            float value = *(float*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public float Maximum
    {
        get
        {
            float value = *(float*)((byte*)Handle + FieldsOffset + 4);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public float Default
    {
        get
        {
            float value = *(float*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the distance below which two values of the property count as the
    /// same one, which is what decides whether a write is a change.
    /// </summary>
    public float Epsilon
    {
        get
        {
            float value = *(float*)((byte*)Handle + FieldsOffset + 12);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecDouble</c>: a property that carries a double precision number
/// out of a range.
/// </summary>
/// <remarks>
/// The four fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecDouble : ParamSpec
{
    internal ParamSpecDouble(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public double Minimum
    {
        get
        {
            double value = *(double*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public double Maximum
    {
        get
        {
            double value = *(double*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public double Default
    {
        get
        {
            double value = *(double*)((byte*)Handle + FieldsOffset + 16);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the distance below which two values of the property count as the
    /// same one, which is what decides whether a write is a change.
    /// </summary>
    public double Epsilon
    {
        get
        {
            double value = *(double*)((byte*)Handle + FieldsOffset + 24);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecUnichar</c>: a property that carries one Unicode code point.
/// </summary>
/// <remarks>
/// The field is read at the offset of the public structure, because GObject
/// exposes it through a macro only.
/// </remarks>
public sealed unsafe class ParamSpecUnichar : ParamSpec
{
    internal ParamSpecUnichar(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the code point the property has when nothing was written to it.
    /// </summary>
    public uint Default
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecEnum</c>: a property that carries one member of an
/// enumeration.
/// </summary>
/// <remarks>
/// The default is read at the offset of the public structure, because GObject
/// exposes it through a macro only. The members are asked of the type of the
/// values rather than read out of the class pointer beside it, which is the
/// same table by a route that does not depend on the layout of a
/// <c>GEnumClass</c>.
/// </remarks>
public sealed unsafe class ParamSpecEnum : ParamSpec
{
    internal ParamSpecEnum(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the member the property has when nothing was written to it.</summary>
    public int Default
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the members of the enumeration, in the order the type declares
    /// them.
    /// </summary>
    public EnumValue[] Values => ValueType.GetEnumValues();
}

/// <summary>
/// A <c>GParamSpecFlags</c>: a property that carries a set of flags.
/// </summary>
/// <remarks>
/// The default is read at the offset of the public structure, because GObject
/// exposes it through a macro only. The members are asked of the type of the
/// values rather than read out of the class pointer beside it, which is the
/// same table by a route that does not depend on the layout of a
/// <c>GFlagsClass</c>.
/// </remarks>
public sealed unsafe class ParamSpecFlags : ParamSpec
{
    internal ParamSpecFlags(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the set the property has when nothing was written to it.</summary>
    public uint Default
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the members of the set, in the order the type declares them.
    /// </summary>
    public FlagsValue[] Values => ValueType.GetFlagsValues();
}

/// <summary>
/// A <c>GParamSpecString</c>: a property that carries a string.
/// </summary>
/// <remarks>
/// The default is read at the offset of the public structure, because GObject
/// exposes it through a macro only. The three fields behind it — the sets of
/// characters a value may begin with and continue with, and the character an
/// offending one is replaced by — are not bound; they can be added later
/// without changing anything declared here.
/// </remarks>
public sealed unsafe class ParamSpecString : ParamSpec
{
    internal ParamSpecString(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the string the property has when nothing was written to it, or
    /// <see langword="null"/> when that default is the null pointer — which is
    /// what the <c>name</c> of a <c>GstObject</c> has, for instance.
    /// </summary>
    public string? Default
    {
        get
        {
            string? value = GMarshal.PtrToStringUtf8(*(nint*)((byte*)Handle + FieldsOffset));
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecGType</c>: a property that carries a type.
/// </summary>
/// <remarks>
/// The field is read at the offset of the public structure, because GObject
/// exposes it through a macro only.
/// </remarks>
public sealed unsafe class ParamSpecGType : ParamSpec
{
    internal ParamSpecGType(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the type every value of the property has to be or derive from.
    /// </summary>
    public GType IsAType
    {
        get
        {
            GType value = new(*(nuint*)((byte*)Handle + FieldsOffset));
            GC.KeepAlive(this);
            return value;
        }
    }
}
