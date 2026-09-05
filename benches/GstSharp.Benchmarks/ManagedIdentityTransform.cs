using Gst;
using Gst.Base;

namespace GstSharp.Benchmarks;

/// <summary>
/// A managed in place filter that answers <see cref="FlowReturn.Ok"/> and
/// touches nothing else.
/// </summary>
/// <remarks>
/// This is the managed half of <see cref="TrampolineBenchmarks"/>: the shape
/// of <c>SubclassIdentityBufferTests</c> in the integration suite, cut down to
/// the one vfunc the benchmark measures. Every buffer of the pipeline reaches
/// <see cref="OnTransformIp"/> through the generated
/// <c>[UnmanagedCallersOnly]</c> trampoline of <c>GstBaseTransform</c>, so the
/// difference against a native <c>identity</c> in the same pipeline is what
/// that dispatch costs.
/// </remarks>
public sealed class ManagedIdentityTransform : BaseTransform
{
    /// <summary>Creates one managed filter.</summary>
    public ManagedIdentityTransform()
        : base(GstRuntime.IdentityTransformType.NewInstance())
    {
        // In place, and not passthrough: passthrough would hand the buffers
        // straight on without ever calling transform_ip, which is the call the
        // benchmark exists to measure.
        SetInPlace(true);
        SetPassthrough(false);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnTransformIp(Gst.Buffer buffer) => FlowReturn.Ok;
}
