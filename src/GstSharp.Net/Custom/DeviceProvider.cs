using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// <c>GstDeviceProvider.probe</c>, whose ownership no generated shape expresses.
/// </content>
/// <remarks>
/// The slot answers a <c>GList</c> the caller takes over, together with one
/// reference per device. The reverse planner has no bucket for a list an
/// override hands out, so the declaration, the override, its chain-up and its
/// trampoline are written here and are otherwise the generated ones of every
/// other slot of this class.
/// </remarks>
public abstract unsafe partial class DeviceProvider
{
    /// <summary>
    /// Gets the declaration of <c>GstDeviceProvider.probe</c>, for a subclass that
    /// overrides <see cref="OnProbe"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A provider has to declare this slot or <see cref="StartOverride"/>; the
    /// registration refuses one that declares neither, because
    /// <c>gst_device_provider_start</c> calls <c>klass-&gt;probe</c> with no
    /// <c>NULL</c> check when the start slot is unset
    /// (gstdeviceprovider.c:476-481).
    /// </para>
    /// <para>
    /// <b>What the caller receives.</b> A managed <see cref="Gst.Device"/> is
    /// sunk when its wrapper is built and stands at one reference, the one the
    /// wrapper owns (Core/GObject/Object.cs:216-220). The trampoline adds one
    /// per answered device, so a device leaves the slot at two references and
    /// is not floating.
    /// </para>
    /// <para>
    /// <c>gst_device_provider_get_devices</c> (gstdeviceprovider.c:421-428)
    /// sinks only the entries that are floating and hands the list on, so the
    /// count stays at two and the caller owns one of them. The forward
    /// <see cref="GetDevices"/> adopts that one onto the live interned wrapper,
    /// which leaves one reference and the very same wrapper instance.
    /// </para>
    /// <para>
    /// <c>gst_device_provider_start</c> on a class with no <c>start</c> slot
    /// (gstdeviceprovider.c:479-493) announces every answered device:
    /// <c>gst_object_set_parent</c> takes a reference (three), and the list
    /// reference is released again for a device that was not floating
    /// (:489-490), which leaves two — the wrapper's and the provider's.
    /// <c>gst_device_provider_stop</c> unparents the device (:537) and leaves
    /// one. A DEVICE_ADDED message holds one more for as long as it is alive
    /// (:647-648).
    /// </para>
    /// <para>
    /// A device that is parented already when it is answered on the start path
    /// is refused: <c>gst_object_set_parent</c> fails, a warning is logged
    /// (:634-637), and the list reference is released at :490, which leaves the
    /// count where it was. Nothing leaks either way.
    /// </para>
    /// </remarks>
    public static Gst.GObject.VfuncOverride ProbeOverride { get; } = new(
        &GetGType,
        Gst.DeviceProviderClassRaw.ProbeOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&ProbeTrampoline);

    /// <summary>
    /// Answers the devices that are available right now.
    /// </summary>
    /// <returns>
    /// The devices this provider offers. The list is consumed: the caller
    /// receives a new list and one added reference per device. Every wrapper
    /// keeps the reference it owns, stays usable, and may be answered again. An
    /// empty list is answered as <c>NULL</c>. A null entry is not allowed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The slot has to answer without blocking (gstdeviceprovider.h:156-158):
    /// it reports what the system offers at this moment and is not the place to
    /// wait for a device to appear.
    /// </para>
    /// <para>
    /// Both callers hold the non recursive start lock of the provider across
    /// the slot (gstdeviceprovider.c:413, :466), so calling
    /// <see cref="IsStarted"/>, <see cref="GetDevices"/>, <see cref="Start"/> or
    /// <see cref="Stop"/> on the same provider from inside this override hangs
    /// the thread for ever.
    /// </para>
    /// <para>
    /// It is called by <see cref="GetDevices"/> while the provider is not
    /// started (gstdeviceprovider.c:421-428), and by <see cref="Start"/> when
    /// the class declares no <c>start</c> slot (:479-493), which announces every
    /// answered device as if <see cref="DeviceAdd"/> had been called for it.
    /// </para>
    /// <para>
    /// Answer devices that have no parent. One that is parented already is
    /// refused with a warning on the start path. <see cref="Stop"/> unparents
    /// every device the provider holds (gstdeviceprovider.c:537), so a device
    /// the provider keeps may be answered again by the next start; do not
    /// answer a device that another provider, or an explicit
    /// <see cref="DeviceAdd"/>, has parented.
    /// </para>
    /// <para>
    /// An exception that leaves this override is reported through the exception
    /// trap and the slot answers no devices; <see cref="Start"/> still succeeds
    /// (gstdeviceprovider.c:495).
    /// </para>
    /// <para>
    /// A managed provider declares <see cref="ProbeOverride"/> or
    /// <see cref="StartOverride"/>; the registration refuses a provider with
    /// neither.
    /// </para>
    /// </remarks>
    protected virtual System.Collections.Generic.IReadOnlyList<Gst.Device> OnProbe() =>
        ChainUpProbe();

    /// <summary>Runs the implementation of <c>probe</c> below the managed override.</summary>
    /// <returns>
    /// The devices the implementation below answered, which the caller owns and
    /// disposes.
    /// </returns>
    /// <remarks>
    /// No class below a managed provider implements <c>probe</c>, so this
    /// answers an empty list unless a native parent does. A device a native
    /// parent answers floating at one reference is sunk by the wrapper that
    /// adopts it and is owned exactly once.
    /// </remarks>
    /// <exception cref="System.ObjectDisposedException">
    /// This wrapper was disposed.
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// This instance is not of a registered managed subclass, so the class
    /// below the override cannot be looked up.
    /// </exception>
    protected System.Collections.Generic.IReadOnlyList<Gst.Device> ChainUpProbe()
    {
        nint[] items = GListMarshal.CollectAndFreeSpine(ChainUpProbe(Handle));
        System.Collections.Generic.List<Gst.Device> result = new(items.Length);
        foreach (nint item in items)
        {
            if (item != 0 && Gst.GObject.Object.FromNative<Gst.Device>(item, Transfer.Full) is { } adopted)
            {
                result.Add(adopted);
            }
        }

        System.GC.KeepAlive(this);
        return result;
    }

    private static nint ChainUpProbe(nint provider)
    {
        delegate* unmanaged[Cdecl]<nint, nint> slot =
            (delegate* unmanaged[Cdecl]<nint, nint>)ParentClassOf(provider)->Probe;

        // A class without the slot answers no devices, which is what
        // gst_device_provider_get_devices reads out of one
        // (gstdeviceprovider.c:421): NULL is the empty list, not a failure.
        if (slot is null)
        {
            return nint.Zero;
        }

        return slot(provider);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint ProbeTrampoline(nint provider)
    {
        try
        {
            if (Gst.GObject.Object.TryGetOrFabricate(provider) is not DeviceProvider managed)
            {
                return ChainUpProbe(provider);
            }

            return GListMarshal.BuildOwnedObjectList(managed.OnProbe(), "OnProbe", "probe");
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return default;
        }
    }
}
