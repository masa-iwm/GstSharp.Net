using Gst;
using Gst.Base;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed push source with four installed properties of four kinds, one of
/// which notifies for itself.
/// </summary>
/// <remarks>
/// It is a source with a pad template rather than a bare element so that a
/// pipeline description can name it and link it, which is the one way to see
/// <c>set_property</c> arrive from inside <c>g_object_new</c> — before anything
/// managed has ever touched the instance.
/// </remarks>
internal sealed class ProbePropertyElement : PushSrc, IManagedSubclass<ProbePropertyElement>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestPropertyElement";

    /// <summary>The name the element factory is registered under.</summary>
    internal const string FactoryName = "gstsharptestpropertyelement";

    /// <summary>The identifier of the <c>value</c> property.</summary>
    internal const uint ValueId = 1;

    /// <summary>The identifier of the <c>label</c> property.</summary>
    internal const uint LabelId = 2;

    /// <summary>The identifier of the <c>target-state</c> property.</summary>
    internal const uint TargetStateId = 3;

    /// <summary>The identifier of the <c>peer</c> property.</summary>
    internal const uint PeerId = 4;

    /// <summary>The identifier of the <c>counter</c> property, which notifies for itself.</summary>
    internal const uint CounterId = 5;

    private const ParamFlags ReadWrite = ParamFlags.Readable | ParamFlags.Writable;

    private const string MediaType = "application/x-gstsharp-property-element";

    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly ParamSpecInt ValueSpec =
        ParamSpecInt.New("value", "Value", "An integer", -100, 100, 0, ReadWrite);

    private static readonly ParamSpecString LabelSpec =
        ParamSpecString.New("label", "Label", "A string", null, ReadWrite);

    private static readonly ParamSpecEnum TargetStateSpec =
        ParamSpecEnum.New("target-state", "Target state", "An enum", GType.FromName("GstState"), 0, ReadWrite);

    private static readonly ParamSpecObject PeerSpec =
        ParamSpecObject.New("peer", "Peer", "An object", new GType(Element.GetGType()), ReadWrite);

    private static readonly ParamSpecInt CounterSpec = ParamSpecInt.New(
        "counter",
        "Counter",
        "An integer that notifies for itself",
        0,
        100,
        0,
        ReadWrite | ParamFlags.ExplicitNotify);

    private static readonly SubclassType Definition = DefineSubclass<ProbePropertyElement>(
        GTypeName,
        ConfigureClass,
        CreateOverride,
        SetPropertyOverride,
        GetPropertyOverride);

    private static readonly bool Registered =
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    private static int _wrappersBuilt;

    private int _value;
    private string? _label;
    private State _targetState;
    private GObjectObject? _peer;
    private int _counter;
    private int _setCalls;
    private int _getCalls;
    private int _lastSetThreadId;
    private bool _lastSetSawWrapper;

    private ProbePropertyElement(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets a value indicating whether the factory was registered.</summary>
    internal static bool IsRegistered => Registered;

    /// <summary>Gets the registration, for the tests that construct from a dictionary.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how many wrappers were fabricated since the last reset.</summary>
    internal static int WrappersBuilt => Volatile.Read(ref _wrappersBuilt);

    /// <summary>Gets the specification of the <c>value</c> property.</summary>
    internal static ParamSpecInt SpecOfValue => ValueSpec;

    /// <summary>Gets the specification of the <c>counter</c> property.</summary>
    internal static ParamSpecInt SpecOfCounter => CounterSpec;

    /// <summary>Gets what the last <c>value</c> write stored.</summary>
    internal int Value => _value;

    /// <summary>Gets what the last <c>label</c> write stored.</summary>
    internal string? Label => _label;

    /// <summary>Gets what the last <c>target-state</c> write stored.</summary>
    internal State TargetState => _targetState;

    /// <summary>Gets what the last <c>peer</c> write stored.</summary>
    internal GObjectObject? Peer => _peer;

    /// <summary>Gets what the last <c>counter</c> write stored.</summary>
    internal int Counter => _counter;

    /// <summary>Gets how many times a property was written.</summary>
    internal int SetCalls => Volatile.Read(ref _setCalls);

    /// <summary>Gets how many times a property was read.</summary>
    internal int GetCalls => Volatile.Read(ref _getCalls);

    /// <summary>Gets the managed thread the last write ran on.</summary>
    internal int LastSetThreadId => Volatile.Read(ref _lastSetThreadId);

    /// <summary>Gets a value indicating whether the last write had a wrapper.</summary>
    internal bool LastSetSawWrapper => Volatile.Read(ref _lastSetSawWrapper);

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset() => Volatile.Write(ref _wrappersBuilt, 0);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbePropertyElement CreateWrapper(SubclassCtorArgs args)
    {
        ProbePropertyElement wrapper = new(args);
        _ = Interlocked.Increment(ref _wrappersBuilt);
        return wrapper;
    }

    /// <summary>Calls the base implementation, which warns and does nothing else.</summary>
    /// <param name="propertyId">An identifier no property was installed with.</param>
    /// <param name="pspec">The specification to name in the warning.</param>
    internal void WarnForUnknownId(uint propertyId, ParamSpec pspec)
    {
        // The base implementation only names the specification in its warning,
        // so an uninitialised value is all it needs — and there is no other way
        // to reach it: GObject never dispatches an identifier no class claims.
        GValueNative carrier = default;
        base.OnSetProperty(propertyId, new ValueView(ref carrier), pspec);
    }

    /// <inheritdoc/>
    protected override void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
    {
        _ = Interlocked.Increment(ref _setCalls);
        Volatile.Write(ref _lastSetThreadId, Environment.CurrentManagedThreadId);
        Volatile.Write(ref _lastSetSawWrapper, true);

        switch (propertyId)
        {
            case ValueId:
                _value = value.GetInt();
                break;

            case LabelId:
                _label = value.GetString();
                break;

            case TargetStateId:
                _targetState = (State)value.GetEnum();
                break;

            case PeerId:
                _peer = value.GetObject();
                break;

            case CounterId:
                int updated = value.GetInt();
                if (updated != _counter)
                {
                    _counter = updated;

                    // The specification carries EXPLICIT_NOTIFY, so GObject
                    // stays silent and this is the only notification there is.
                    Notify(pspec);
                }

                break;

            default:
                base.OnSetProperty(propertyId, value, pspec);
                break;
        }
    }

    /// <inheritdoc/>
    protected override void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
    {
        _ = Interlocked.Increment(ref _getCalls);

        switch (propertyId)
        {
            case ValueId:
                value.SetInt(_value);
                break;

            case LabelId:
                value.SetString(_label);
                break;

            case TargetStateId:
                value.SetEnum((int)_targetState);
                break;

            case PeerId:
                value.SetObject(_peer);
                break;

            case CounterId:
                value.SetInt(_counter);
                break;

            default:
                base.OnGetProperty(propertyId, value, pspec);
                break;
        }
    }

    /// <inheritdoc/>
    protected override FlowReturn OnCreate(out Gst.Buffer? buffer)
    {
        buffer = null;
        return FlowReturn.Eos;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe property element",
            "Source/Testing",
            "A managed source that installs properties",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);

        config.InstallProperty(ValueId, ValueSpec);
        config.InstallProperty(LabelId, LabelSpec);
        config.InstallProperty(TargetStateId, TargetStateSpec);
        config.InstallProperty(PeerId, PeerSpec);
        config.InstallProperty(CounterId, CounterSpec);
    }

    private static PadTemplate NewSrcTemplate()
    {
        using Caps caps = Caps.NewEmptySimple(MediaType);

        return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }
}
