using System.Globalization;
using System.Runtime.InteropServices;
using Gst.GLib;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The flags of a signal, that is <c>GSignalFlags</c>.
/// </summary>
[Flags]
public enum SignalFlags
{
    /// <summary>No flag at all.</summary>
    None = 0,

    /// <summary>The class handler runs before the connected handlers.</summary>
    RunFirst = 1 << 0,

    /// <summary>The class handler runs after the connected handlers.</summary>
    RunLast = 1 << 1,

    /// <summary>The class handler runs last, after the handlers connected with <c>after</c>.</summary>
    RunCleanup = 1 << 2,

    /// <summary>Emission of the signal must not be recursive.</summary>
    NoRecurse = 1 << 3,

    /// <summary>The signal takes a detail, as in <c>notify::name</c>.</summary>
    Detailed = 1 << 4,

    /// <summary>
    /// The signal is an action: it is meant to be emitted by application code,
    /// which is how GStreamer offers calls such as <c>try-pull-sample</c> or
    /// <c>resize</c> to bindings.
    /// </summary>
    Action = 1 << 5,

    /// <summary>The signal supports no emission hooks.</summary>
    NoHooks = 1 << 6,

    /// <summary>The arguments are copied for the duration of the emission.</summary>
    MustCollect = 1 << 7,

    /// <summary>The signal is deprecated.</summary>
    Deprecated = 1 << 8,

    /// <summary>The accumulator also runs on the return value of the first handler.</summary>
    AccumulatorFirstRun = 1 << 17,
}

/// <summary>
/// The memory layout of a <c>GSignalQuery</c>.
/// </summary>
/// <remarks>
/// <c>return_type</c> and the entries of <c>param_types</c> are mangled with
/// <c>G_SIGNAL_TYPE_STATIC_SCOPE</c>, which is bit zero of the type. The
/// accessors of <see cref="SignalQuery"/> mask it off, so that no caller ever
/// sees a mangled type: <c>g_value_init</c> on one is a fatal warning.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GSignalQuery
{
    internal uint SignalId;
    internal nint SignalName;
    internal nuint IType;
    internal SignalFlags SignalFlags;
    internal nuint ReturnType;
    internal uint NParams;
    internal nint ParamTypes;
}

/// <summary>
/// The description of one signal: what it is called, what it takes and what it
/// returns.
/// </summary>
/// <remarks>
/// <para>
/// This is the introspection that <see cref="Object.EmitSignal(string, object?[])"/>
/// and <see cref="Object.ConnectSignal(string, DynamicSignalHandler, bool)"/>
/// run before they touch a signal, and it is public so that applications can
/// ask the same questions about a signal that no <c>.gir</c> file describes,
/// for example one that a plugin adds.
/// </para>
/// <para>
/// The parameter types are read straight out of the signal node of GObject,
/// which lives as long as the type system does, so a query stays valid for the
/// lifetime of the process.
/// </para>
/// </remarks>
public readonly struct SignalQuery : IEquatable<SignalQuery>
{
    /// <summary>
    /// The value of <c>G_SIGNAL_TYPE_STATIC_SCOPE</c>, which GObject mangles
    /// into the types of a query.
    /// </summary>
    private const nuint StaticScope = 1;

    private const int NameLimit = 40;

    private readonly GSignalQuery _query;

    private SignalQuery(GSignalQuery query) => _query = query;

    /// <summary>
    /// Gets the identifier GObject gave the signal, or zero when the query
    /// found none.
    /// </summary>
    public uint Id => _query.SignalId;

    /// <summary>
    /// Gets a value indicating whether the query describes an existing signal.
    /// </summary>
    public bool IsValid => _query.SignalId != 0;

    /// <summary>
    /// Gets the name of the signal, without a detail.
    /// </summary>
    public string Name => GMarshal.PtrToStringUtf8(_query.SignalName) ?? string.Empty;

    /// <summary>
    /// Gets the type the signal is registered on, which may be a base type of
    /// the one that was queried.
    /// </summary>
    public GType InstanceType => new(_query.IType);

    /// <summary>
    /// Gets the flags of the signal.
    /// </summary>
    public SignalFlags Flags => _query.SignalFlags;

    /// <summary>
    /// Gets a value indicating whether the signal is an action, that is one
    /// that application code is meant to emit itself.
    /// </summary>
    public bool IsAction => (_query.SignalFlags & SignalFlags.Action) != 0;

    /// <summary>
    /// Gets a value indicating whether the signal takes a detail, as in
    /// <c>notify::name</c>.
    /// </summary>
    public bool IsDetailed => (_query.SignalFlags & SignalFlags.Detailed) != 0;

    /// <summary>
    /// Gets the type the signal returns, which is <see cref="GType.None"/> for
    /// a signal that returns nothing.
    /// </summary>
    public GType ReturnType => new(_query.ReturnType & ~StaticScope);

    /// <summary>
    /// Gets the number of arguments of the signal, not counting the instance.
    /// </summary>
    public int ParameterCount => (int)_query.NParams;

    /// <summary>Compares two queries for equality.</summary>
    /// <param name="left">The first query.</param>
    /// <param name="right">The second query.</param>
    /// <returns><see langword="true"/> when both describe the same signal.</returns>
    public static bool operator ==(SignalQuery left, SignalQuery right) => left.Equals(right);

    /// <summary>Compares two queries for inequality.</summary>
    /// <param name="left">The first query.</param>
    /// <param name="right">The second query.</param>
    /// <returns><see langword="true"/> when both describe different signals.</returns>
    public static bool operator !=(SignalQuery left, SignalQuery right) => !left.Equals(right);

    /// <summary>
    /// Looks a signal up on a type and describes it.
    /// </summary>
    /// <param name="instanceType">The type of the instance the signal is emitted on.</param>
    /// <param name="detailedSignal">
    /// The name of the signal, optionally with a detail, for example
    /// <c>notify::name</c>.
    /// </param>
    /// <param name="query">The description of the signal.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="instanceType"/> has such a
    /// signal.
    /// </returns>
    public static bool TryLookup(GType instanceType, string detailedSignal, out SignalQuery query) =>
        TryParse(instanceType, detailedSignal, out query, out Quark _);

    /// <summary>
    /// Describes the signal of an identifier.
    /// </summary>
    /// <param name="signalId">The identifier of the signal.</param>
    /// <returns>The description, which is invalid for an unknown identifier.</returns>
    public static SignalQuery FromId(uint signalId)
    {
        if (signalId == 0)
        {
            return default;
        }

        GObjectNative.SignalQueryById(signalId, out GSignalQuery query);
        return new SignalQuery(query);
    }

    /// <summary>
    /// Reads the type of one argument of the signal.
    /// </summary>
    /// <param name="index">
    /// The index of the argument, where zero is the first one after the
    /// instance.
    /// </param>
    /// <returns>The type of the argument.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an argument of this signal.
    /// </exception>
    public unsafe GType GetParameterType(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ParameterCount);

        return new GType(((nuint*)_query.ParamTypes)[index] & ~StaticScope);
    }

    /// <summary>
    /// Reads the types of every argument of the signal.
    /// </summary>
    /// <returns>The types, in the order the signal takes them.</returns>
    public GType[] GetParameterTypes()
    {
        int count = ParameterCount;
        if (count == 0)
        {
            return [];
        }

        GType[] types = new GType[count];
        for (int i = 0; i < count; i++)
        {
            types[i] = GetParameterType(i);
        }

        return types;
    }

    /// <summary>
    /// Returns the signature of the signal, the way it reads in an error
    /// message.
    /// </summary>
    /// <returns>For example <c>void resize(gint, gint)</c>.</returns>
    public override string ToString()
    {
        if (!IsValid)
        {
            return "<unknown signal>";
        }

        string arguments = string.Join(", ", GetParameterTypes().Select(static type => type.Name));
        GType returnType = ReturnType;
        string result = returnType.IsValid && returnType != GType.None ? returnType.Name : "void";
        return $"{result} {Name}({arguments})";
    }

    /// <inheritdoc/>
    public bool Equals(SignalQuery other) => _query.SignalId == other._query.SignalId;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SignalQuery other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _query.SignalId.GetHashCode();

    /// <summary>
    /// Looks a possibly detailed signal name up and splits the detail off.
    /// </summary>
    /// <param name="instanceType">The type of the instance the signal is emitted on.</param>
    /// <param name="detailedSignal">The name of the signal, optionally with a detail.</param>
    /// <param name="query">The description of the signal.</param>
    /// <param name="detail">The quark of the detail, or zero when there is none.</param>
    /// <returns><see langword="true"/> when the signal exists.</returns>
    internal static unsafe bool TryParse(
        GType instanceType,
        string detailedSignal,
        out SignalQuery query,
        out Quark detail)
    {
        query = default;
        detail = Quark.Zero;

        if (!instanceType.IsValid || string.IsNullOrEmpty(detailedSignal))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(detailedSignal, buffer);

        uint signalId;
        uint detailQuark;

        // The detail quark is forced into existence, so that a detail nothing
        // has used yet still emits.
        if (GObjectNative.SignalParseName(scope.Pointer, instanceType.Value, &signalId, &detailQuark, 1) == 0)
        {
            return false;
        }

        query = FromId(signalId);
        detail = new Quark(detailQuark);
        return query.IsValid;
    }

    /// <summary>
    /// Lists the signals a type has, for the error message of a name that was
    /// not found.
    /// </summary>
    /// <param name="instanceType">The type to list the signals of.</param>
    /// <returns>The sentence to append to the message.</returns>
    internal static unsafe string DescribeAvailable(GType instanceType)
    {
        SortedSet<string> names = new(StringComparer.Ordinal);

        for (GType type = instanceType; type.IsValid; type = type.Parent)
        {
            CollectNames(type, names);
        }

        // The signals of the interfaces of the type are emitted on it as well,
        // and g_type_interfaces lists the inherited interfaces too.
        uint interfaceCount = 0;
        nint interfaces = GObjectNative.TypeInterfaces(instanceType.Value, &interfaceCount);
        if (interfaces != nint.Zero)
        {
            try
            {
                for (uint i = 0; i < interfaceCount; i++)
                {
                    CollectNames(new GType(((nuint*)interfaces)[i]), names);
                }
            }
            finally
            {
                GMarshal.Free(interfaces);
            }
        }

        if (names.Count == 0)
        {
            return "It has no signals at all.";
        }

        string listed = string.Join(", ", names.Take(NameLimit));
        return names.Count > NameLimit
            ? $"Its signals are {listed}, and {(names.Count - NameLimit).ToString(CultureInfo.InvariantCulture)} more."
            : $"Its signals are {listed}.";
    }

    private static unsafe void CollectNames(GType type, SortedSet<string> names)
    {
        if (!type.IsValid)
        {
            return;
        }

        uint count = 0;
        nint ids = GObjectNative.SignalListIds(type.Value, &count);
        if (ids == nint.Zero)
        {
            return;
        }

        try
        {
            for (uint i = 0; i < count; i++)
            {
                SignalQuery query = FromId(((uint*)ids)[i]);
                if (query.IsValid)
                {
                    names.Add(query.Name);
                }
            }
        }
        finally
        {
            GMarshal.Free(ids);
        }
    }
}
