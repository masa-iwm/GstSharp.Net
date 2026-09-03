namespace Gst.GObject;

/// <summary>
/// The classes of <c>GParamSpec</c> the binding declares a wrapper for.
/// </summary>
internal enum ParamSpecKind
{
    /// <summary>A class the binding has no wrapper for.</summary>
    Other,

    /// <summary><c>GParamSpecBoolean</c>.</summary>
    Boolean,

    /// <summary><c>GParamSpecChar</c>.</summary>
    Char,

    /// <summary><c>GParamSpecUChar</c>.</summary>
    UChar,

    /// <summary><c>GParamSpecInt</c>.</summary>
    Int,

    /// <summary><c>GParamSpecUInt</c>.</summary>
    UInt,

    /// <summary><c>GParamSpecLong</c>.</summary>
    Long,

    /// <summary><c>GParamSpecULong</c>.</summary>
    ULong,

    /// <summary><c>GParamSpecInt64</c>.</summary>
    Int64,

    /// <summary><c>GParamSpecUInt64</c>.</summary>
    UInt64,

    /// <summary><c>GParamSpecFloat</c>.</summary>
    Float,

    /// <summary><c>GParamSpecDouble</c>.</summary>
    Double,

    /// <summary><c>GParamSpecUnichar</c>.</summary>
    Unichar,

    /// <summary><c>GParamSpecEnum</c>.</summary>
    Enum,

    /// <summary><c>GParamSpecFlags</c>.</summary>
    Flags,

    /// <summary><c>GParamSpecString</c>.</summary>
    String,

    /// <summary><c>GParamSpecGType</c>.</summary>
    GType,

    /// <summary><c>GstParamSpecFraction</c>.</summary>
    Fraction,

    /// <summary><c>GstParamSpecArray</c>.</summary>
    Array,
}

/// <summary>
/// Maps the type of a <c>GParamSpec</c> onto the wrapper class that reads it.
/// </summary>
/// <remarks>
/// <para>
/// GObject publishes its parameter specification types as
/// <c>g_param_spec_types</c>, an exported array of <c>GType</c>. That is a data
/// symbol, and a P/Invoke reaches functions rather than data, so the types are
/// resolved by name instead: every one of them is registered by the time any
/// specification exists, because a specification is an instance of one.
/// </para>
/// <para>
/// The table is built on first use rather than in a field initialiser: the
/// native library has to be loaded and GObject initialised before a name can be
/// resolved, and the first call arrives through
/// <see cref="ParamSpec.FromNative"/>, which by construction happens after
/// both. Two threads that race build the same table twice and one of them wins,
/// which costs a few lookups and is otherwise harmless.
/// </para>
/// </remarks>
internal static class ParamSpecKinds
{
    private static (nuint Type, ParamSpecKind Kind)[]? _table;

    /// <summary>
    /// Finds the wrapper class that matches a specification type.
    /// </summary>
    /// <param name="type">The type of the specification, that is <c>G_PARAM_SPEC_TYPE</c>.</param>
    /// <returns>
    /// The class to wrap it in, or <see cref="ParamSpecKind.Other"/> when the
    /// binding declares none.
    /// </returns>
    /// <remarks>
    /// The exact type answers for everything GLib and GStreamer register today.
    /// A specification type can be derived from further, though — the type
    /// system allows it and a plugin could do it — so a type that matches
    /// nothing is walked up to its parent until <c>GParam</c> is reached, and
    /// the first known ancestor answers. That is the right answer by
    /// construction: a derived specification keeps the fields of the one it
    /// derives from, at the offsets it inherited them at.
    /// </remarks>
    internal static ParamSpecKind Of(GType type)
    {
        (nuint Type, ParamSpecKind Kind)[] table = _table ??= Build();

        for (GType current = type; current.IsValid; current = current.Parent)
        {
            foreach ((nuint candidate, ParamSpecKind kind) in table)
            {
                if (candidate != 0 && candidate == current.Value)
                {
                    return kind;
                }
            }

            if (current.Value == GType.ParamValue)
            {
                break;
            }
        }

        return ParamSpecKind.Other;
    }

    private static (nuint Type, ParamSpecKind Kind)[] Build() =>
    [
        (GType.FromName("GParamBoolean").Value, ParamSpecKind.Boolean),
        (GType.FromName("GParamChar").Value, ParamSpecKind.Char),
        (GType.FromName("GParamUChar").Value, ParamSpecKind.UChar),
        (GType.FromName("GParamInt").Value, ParamSpecKind.Int),
        (GType.FromName("GParamUInt").Value, ParamSpecKind.UInt),
        (GType.FromName("GParamLong").Value, ParamSpecKind.Long),
        (GType.FromName("GParamULong").Value, ParamSpecKind.ULong),
        (GType.FromName("GParamInt64").Value, ParamSpecKind.Int64),
        (GType.FromName("GParamUInt64").Value, ParamSpecKind.UInt64),
        (GType.FromName("GParamFloat").Value, ParamSpecKind.Float),
        (GType.FromName("GParamDouble").Value, ParamSpecKind.Double),
        (GType.FromName("GParamUnichar").Value, ParamSpecKind.Unichar),
        (GType.FromName("GParamEnum").Value, ParamSpecKind.Enum),
        (GType.FromName("GParamFlags").Value, ParamSpecKind.Flags),
        (GType.FromName("GParamString").Value, ParamSpecKind.String),
        (GType.FromName("GParamGType").Value, ParamSpecKind.GType),

        // The two GStreamer types are asked of their own get_type functions
        // rather than of g_type_from_name, because those register the type as
        // well: a process that has not touched a fraction property yet has not
        // registered it, and the name would resolve to nothing.
        (GstNative.ParamSpecFractionGetType(), ParamSpecKind.Fraction),
        (GstNative.ParamSpecArrayGetType(), ParamSpecKind.Array),
    ];
}
