using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed push source whose pad template names a managed pad type, so that
/// <c>GstBaseSrc</c> builds a <see cref="ProbeManagedPad"/> from C's side while
/// the element is being constructed.
/// </summary>
internal sealed class ProbeManagedPadSrc : PushSrc
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestManagedPadSrc";

    /// <summary>
    /// The pad template, built before the registration: a class initialiser may
    /// only add one, never build one.
    /// </summary>
    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        CreateOverride);

    /// <summary>Creates a managed source.</summary>
    internal ProbeManagedPadSrc()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the source is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <inheritdoc/>
    protected override FlowReturn OnCreate(out Gst.Buffer? buffer)
    {
        buffer = null;
        return FlowReturn.Eos;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe managed pad source",
            "Source/Testing",
            "A source whose src pad is of a managed pad type",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);
    }

    private static PadTemplate NewSrcTemplate()
    {
        using Caps caps = Caps.NewAny();

        return PadTemplate.NewWithGtype(
                "src",
                PadDirection.Src,
                PadPresence.Always,
                caps,
                ProbeManagedPad.RegisteredType)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }
}
