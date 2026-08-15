using Xunit;
using Gst.GObject;
using Gst.Interop;

namespace GstSharp.Core.Tests;

public sealed class FreezeResilienceTests
{
    private static nuint ThrowingGetType() =>
        throw new EntryPointNotFoundException("gst_future_api_get_type");

    private static object Factory(nint handle, Transfer transfer) =>
        throw new InvalidOperationException("never reached");

    [Fact]
    public unsafe void AMissingNativeEntryPointSkipsTheEntryInsteadOfFailingFreeze()
    {
        ModuleTypeEntry entry = new(&ThrowingGetType, &Factory);
        TypeRegistry.RegisterModule(new NativeModule("GstFutureTest", [entry]));

        // Must not throw even though the accessor does.
        TypeRegistry.Freeze();
    }
}
