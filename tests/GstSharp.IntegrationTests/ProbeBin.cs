using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed container: it records every message its children post and can
/// swallow the end of stream instead of passing it on.
/// </summary>
/// <remarks>
/// <c>handle_message</c> is the one vfunc where the override <em>owns</em> what
/// it is given: the C signature hands the reference over, so chaining up passes
/// the message on and returning without chaining up drops it. That is what
/// makes this the hook for filtering what a subtree of the pipeline reports.
/// </remarks>
internal class ProbeBin : Bin
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeBin";

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        HandleMessageOverride);

    private readonly List<MessageType> _handled = [];

    /// <summary>Creates a managed bin.</summary>
    internal ProbeBin()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the bin is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>
    /// Gets or sets a value indicating whether the end of stream is dropped
    /// rather than passed on.
    /// </summary>
    internal bool DropEos { get; set; }

    /// <summary>Gets the messages the override has seen, oldest first.</summary>
    internal IReadOnlyList<MessageType> Handled
    {
        get
        {
            lock (_handled)
            {
                return _handled.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnHandleMessage(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        MessageType type = message.Type;

        lock (_handled)
        {
            _handled.Add(type);
        }

        if (DropEos && type == MessageType.Eos)
        {
            // Not chaining up is what drops the message: the wrapper is
            // released when this returns and its reference goes with it.
            return;
        }

        ChainUpHandleMessage(message);
    }

    /// <summary>Describes the class. A bin needs no pad template of its own.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config) =>
        config.SetMetadata(
            "GstSharp probe bin",
            "Generic/Bin/Testing",
            "Records what its children post",
            "GstSharp.Net integration tests");
}
