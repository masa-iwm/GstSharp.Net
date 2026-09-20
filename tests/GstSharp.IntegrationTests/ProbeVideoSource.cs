using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESVideoSource</c> whose <c>create_source</c> override answers
/// a <c>videotestsrc</c>, and which records the two slots that say something
/// about how the editing services build and unbuild it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in C# constructs one of these in the paths that matter. The library
/// builds it, through <c>ges_asset_extract</c>, when a clip of a managed type
/// is added to a layer, split or pasted, so the wrapper is fabricated and every
/// override below runs for an instance the test never made. See
/// <c>docs/subclassing.md</c> §11.
/// </para>
/// <para>
/// <see cref="New"/> exists for the negative case only:
/// <see cref="ProbeNewChildSourceClip"/> answers such an instance, which has no
/// asset and therefore no <c>nleobject</c>.
/// </para>
/// </remarks>
internal sealed class ProbeVideoSource : GES.VideoSource, IManagedSubclass<ProbeVideoSource>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesVideoSource";

    /// <summary>The name of the child property the source registers for itself.</summary>
    internal const string TagName = "probe-tag";

    /// <summary>
    /// The name of the specification the <c>lookup_child</c> override answers
    /// and nothing registers.
    /// </summary>
    internal const string AliasName = "probe-alias";

    /// <summary>The identifier of the <c>probe-tag</c> property.</summary>
    internal const uint TagId = 1;

    private static readonly ParamSpecString TagSpec = ParamSpecString.New(
        TagName,
        "Probe tag",
        "A string the child property slots write and read",
        null,
        ParamFlags.Readable | ParamFlags.Writable);

    /// <summary>
    /// A specification no property of any class backs: it is what the
    /// <c>list_children_properties</c> override adds to the block it answers
    /// and what the <c>lookup_child</c> override answers for a name of its
    /// own, so both are the produced half of the contract and nothing else.
    /// </summary>
    private static readonly ParamSpecInt AliasSpec = ParamSpecInt.New(
        AliasName,
        "Probe alias",
        "A specification the overrides hand out and nothing registers",
        0,
        10,
        3,
        ParamFlags.Readable);

    private static readonly SubclassType Definition = DefineSubclass<ProbeVideoSource>(
        GTypeName,
        ConfigureClass,
        CreateSourceOverride,
        SetMaxDurationOverride,
        SetParentOverride,
        ListChildrenPropertiesOverride,
        LookupChildOverride,
        SetChildPropertyOverride,
        SetChildPropertyFullOverride,
        SetPropertyOverride,
        GetPropertyOverride);

    private static int _wrappersBuilt;

    private readonly List<string?> _maxDurationNames = [];

    private int _maxDurationCalls;
    private bool _sawUnnamedMaxDuration;
    private int _setParentCalls;
    private bool _lastParentWasNull;

    private readonly List<string> _childPropertyWrites = [];

    private int _childPropertyFullCalls;

    private string? _tag;

    private int _lookupCalls;

    private ProbeVideoSource(SubclassCtorArgs args)
        : base(args)
    {
        // A track element owns the registry its child property slots read, and
        // the element itself is a child the C registration allows
        // (ges-timeline-element.c:909-910).
        _ = AddChildProperty(TagSpec, this);
    }

    /// <summary>Gets the registration of the source.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how many wrappers were fabricated since the last reset.</summary>
    internal static int WrappersBuilt => Volatile.Read(ref _wrappersBuilt);

    /// <summary>Gets how often <c>set_max_duration</c> reached this wrapper.</summary>
    internal int MaxDurationCalls => Volatile.Read(ref _maxDurationCalls);

    /// <summary>
    /// Gets a value indicating whether <c>set_max_duration</c> ever reached
    /// this wrapper while the instance was still unnamed, which is what the
    /// construction-time call would look like.
    /// </summary>
    internal bool SawUnnamedMaxDuration => Volatile.Read(ref _sawUnnamedMaxDuration);

    /// <summary>Gets the name the instance had at each <c>set_max_duration</c>.</summary>
    internal IReadOnlyList<string?> MaxDurationNames
    {
        get
        {
            lock (_maxDurationNames)
            {
                return _maxDurationNames.ToArray();
            }
        }
    }

    /// <summary>Gets how often <c>set_parent</c> reached this wrapper.</summary>
    internal int SetParentCalls => Volatile.Read(ref _setParentCalls);

    /// <summary>Gets a value indicating whether the last parent handed over was none.</summary>
    internal bool LastParentWasNull => Volatile.Read(ref _lastParentWasNull);

    /// <summary>Gets what the last write of <c>probe-tag</c> stored.</summary>
    internal string? Tag => _tag;

    /// <summary>Gets how often <c>lookup_child</c> reached this wrapper.</summary>
    internal int LookupCalls => Volatile.Read(ref _lookupCalls);

    /// <summary>
    /// Gets the specification the lookup last handed out, which the slot
    /// consumes: the wrapper is disposed once its reference has been taken.
    /// </summary>
    internal ParamSpec? HandedOutSpec { get; private set; }

    /// <summary>Gets the child properties the <c>set_child_property</c> override saw.</summary>
    internal IReadOnlyList<string> ChildPropertyWrites
    {
        get
        {
            lock (_childPropertyWrites)
            {
                return [.. _childPropertyWrites];
            }
        }
    }

    /// <summary>Gets how often <c>set_child_property_full</c> reached this wrapper.</summary>
    internal int ChildPropertyFullCalls => Volatile.Read(ref _childPropertyFullCalls);

    /// <summary>
    /// Gets or sets the reason the <c>set_child_property_full</c> override
    /// refuses a write with, or <see langword="null"/> to let the write through.
    /// </summary>
    /// <remarks>
    /// Every hook here is per instance: each test makes a source of its own, so
    /// nothing a test sets is seen by the source of another one.
    /// </remarks>
    internal Gst.GLib.GException? RefuseWith { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the override refuses a write
    /// without saying why, which is an answer GES itself gives.
    /// </summary>
    internal bool RefuseWithoutReason { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the override throws instead of
    /// answering.
    /// </summary>
    internal bool ThrowOnChildPropertyFull { get; set; }

    /// <summary>
    /// Gets or sets the error the override throws instead of answering with,
    /// which is the spelling a caller of the forward binding is used to.
    /// </summary>
    internal Gst.GLib.GException? ThrowRefusalWith { get; set; }

    /// <summary>Builds an instance no asset describes, for the negative case.</summary>
    /// <returns>The new source, which has no asset.</returns>
    internal static ProbeVideoSource New() => new(Definition.NewInstance());

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset() => Volatile.Write(ref _wrappersBuilt, 0);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeVideoSource CreateWrapper(SubclassCtorArgs args)
    {
        ProbeVideoSource wrapper = new(args);
        _ = Interlocked.Increment(ref _wrappersBuilt);
        return wrapper;
    }

    /// <inheritdoc/>
    protected override Gst.Element OnCreateSource()
    {
        // The element must have no parent: a failed gst_bin_add releases both
        // the answer and the nlesource (ges-track-element.c:1073-1078). The
        // wrapper keeps the reference it made and the topbin takes one of its
        // own.
        return Gst.ElementFactory.Make("videotestsrc", null)
            ?? throw new InvalidOperationException("videotestsrc is not installed.");
    }

    /// <inheritdoc/>
    protected override bool OnSetMaxDuration(Gst.ClockTime maxduration)
    {
        _ = Interlocked.Increment(ref _maxDurationCalls);

        string? name = Name;

        lock (_maxDurationNames)
        {
            _maxDurationNames.Add(name);
        }

        if (string.IsNullOrEmpty(name))
        {
            Volatile.Write(ref _sawUnnamedMaxDuration, true);
        }

        return ChainUpSetMaxDuration(maxduration);
    }

    /// <inheritdoc/>
    protected override bool OnSetParent(GES.TimelineElement? newParent)
    {
        _ = Interlocked.Increment(ref _setParentCalls);
        Volatile.Write(ref _lastParentWasNull, newParent is null);

        return ChainUpSetParent(newParent);
    }

    /// <inheritdoc/>
    protected override ParamSpec[] OnListChildrenProperties()
    {
        ParamSpec[] registered = ChainUpListChildrenProperties();
        ParamSpec[] answered = new ParamSpec[registered.Length + 1];
        registered.CopyTo(answered, 0);

        // The block is consumed: the trampoline references every element and
        // disposes the wrapper it took the reference from. The class keeps
        // AliasSpec, so what is handed over is a wrapper of its own.
        answered[^1] = ParamSpec.FromNative(AliasSpec.Handle, Gst.Interop.Transfer.None);
        return answered;
    }

    /// <inheritdoc/>
    protected override bool OnLookupChild(string propName, out Gst.GObject.Object? child, out ParamSpec? pspec)
    {
        _ = Interlocked.Increment(ref _lookupCalls);

        if (string.Equals(propName, AliasName, StringComparison.Ordinal))
        {
            // The child of a child property may be the element itself, and the
            // interning hands the caller this very wrapper back.
            child = this;
            pspec = ParamSpec.FromNative(AliasSpec.Handle, Gst.Interop.Transfer.None);

            // Kept so that a test can see what became of it: the wrapper is
            // consumed, so this one answers nothing once the slot returned.
            HandedOutSpec = pspec;
            return true;
        }

        return ChainUpLookupChild(propName, out child, out pspec);
    }

    /// <inheritdoc/>
    protected override void OnSetChildProperty(Gst.GObject.Object child, ParamSpec pspec, ValueView value)
    {
        lock (_childPropertyWrites)
        {
            _childPropertyWrites.Add(pspec.Name);
        }

        ChainUpSetChildProperty(child, pspec, value);
    }

    /// <inheritdoc/>
    protected override bool OnSetChildPropertyFull(
        Gst.GObject.Object child,
        ParamSpec pspec,
        ValueView value,
        out Gst.GLib.GException? error)
    {
        _ = Interlocked.Increment(ref _childPropertyFullCalls);

        if (ThrowOnChildPropertyFull)
        {
            throw new InvalidOperationException($"The element refuses to write {pspec.Name}.");
        }

        if (ThrowRefusalWith is { } thrown)
        {
            throw thrown;
        }

        if (RefuseWithoutReason)
        {
            error = null;
            return false;
        }

        if (RefuseWith is { } refusal)
        {
            error = refusal;
            return false;
        }

        // Chaining up is what keeps the set_child_property slot reachable: the
        // default implementation of this one, which GES installs on every
        // class, is its only caller.
        return ChainUpSetChildPropertyFull(child, pspec, value, out error);
    }

    /// <inheritdoc/>
    protected override void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
    {
        if (propertyId == TagId)
        {
            _tag = value.GetString();
            return;
        }

        base.OnSetProperty(propertyId, value, pspec);
    }

    /// <inheritdoc/>
    protected override void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
    {
        if (propertyId == TagId)
        {
            value.SetString(_tag);
            return;
        }

        base.OnGetProperty(propertyId, value, pspec);
    }

    private static void ConfigureClass(ObjectClassConfig config) =>
        config.InstallProperty(TagId, TagSpec);
}
