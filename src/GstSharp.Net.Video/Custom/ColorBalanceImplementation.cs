using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.Video;

/// <summary>
/// Declares that a managed element implements <c>GstColorBalance</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="For{TSelf}"/> builds the entry that goes into
/// <c>SubclassOptions.Interfaces</c>, and the registration attaches the
/// interface and fills its vtable. There is no other way in: GObject refuses to
/// attach an interface once the class of the type is being initialised, so a
/// type that was defined without this is not a color balance and never becomes
/// one.
/// </para>
/// </remarks>
public static unsafe class ColorBalanceImplementation
{
    /// <summary>The log domain of the warnings the slots write.</summary>
    private const string LogDomain = "GStreamer";

    /// <summary>Serialises the reads and writes of the channel lists of every element.</summary>
    /// <remarks>
    /// <para>
    /// <c>list_channels</c> is not rare: besides an application building a
    /// user interface and <c>playsink</c> setting a video chain up,
    /// <c>playsink</c> asks again on every value set on one of its proxy
    /// channels (<c>gstplaysink.c:5538</c>). What the lock covers is short,
    /// though - a comparison of handles and, when the channels changed, one
    /// list built - so one lock for the process is enough.
    /// </para>
    /// <para>
    /// Only leaf locks are taken under it. The members of an implementation
    /// run before it is taken; the <c>g_object_ref</c> of a channel can run
    /// the toggle notification of its wrapper, which takes that wrapper's own
    /// lock and nothing else to switch it between a strong and a weak handle;
    /// and nothing that runs under it takes it again. So it cannot deadlock.
    /// </para>
    /// </remarks>
    private static readonly System.Threading.Lock ListsGate = new();

    private static GType _interfaceType;

    private static uint _listsQuark;

    /// <summary>
    /// Declares that <typeparamref name="TSelf"/> implements
    /// <c>GstColorBalance</c>, for the <c>SubclassOptions</c> of its
    /// registration.
    /// </summary>
    /// <typeparam name="TSelf">The managed element type.</typeparam>
    /// <returns>The entry to put into <c>SubclassOptions.Interfaces</c>.</returns>
    /// <remarks>
    /// The type does not exist yet when this runs - it is being defined - so
    /// nothing here touches a <c>GType</c> of it. Every slot of the interface
    /// answers for an instance, so there is nothing to read off the type and
    /// nothing to pin: the channel list an element lends its callers is kept
    /// per instance, by the runtime, and released with the element.
    /// </remarks>
    public static InterfaceImplementation For<TSelf>()
        where TSelf : Element, IColorBalanceImplementation, IManagedSubclass<TSelf> =>
        new ColorBalance(InterfaceType, typeof(TSelf));

    /// <summary>Gets the type of <c>GstColorBalance</c>, resolved once.</summary>
    private static GType InterfaceType
    {
        get
        {
            GType cached = _interfaceType;
            if (!cached.IsValid)
            {
                cached = new GType(ColorBalanceExtensions.GetGType());
                _interfaceType = cached;
            }

            return cached;
        }
    }

    /// <summary>
    /// Answers the quark the channel lists of an element hang under, resolving
    /// it once.
    /// </summary>
    /// <returns>The quark of <c>gstsharp-color-balance-channel-lists</c>.</returns>
    /// <remarks>
    /// Resolving it twice answers the same quark, which is why the race between
    /// two threads that both find the field unset is not worth a lock.
    /// </remarks>
    private static uint ListsQuark
    {
        get
        {
            uint quark = _listsQuark;
            if (quark != 0)
            {
                return quark;
            }

            quark = Gst.GLib.Quark.FromString("gstsharp-color-balance-channel-lists").Value;
            _listsQuark = quark;
            return quark;
        }
    }

    /// <summary>
    /// Answers the list <c>list_channels</c> lends, building a new one only
    /// when the channels changed.
    /// </summary>
    /// <param name="balance">The element.</param>
    /// <param name="handles">
    /// The channels the implementation answered, or <see langword="null"/> when
    /// it answered nothing usable and the caller is to get the list it got
    /// before.
    /// </param>
    /// <returns>The list, which the element owns, or <c>NULL</c> for no channels.</returns>
    /// <remarks>
    /// <para>
    /// The lists hang off the element as native memory with a
    /// <c>GDestroyNotify</c>, so they live exactly as long as the element does
    /// and nothing managed is rooted by it. A list is never freed while the
    /// element lives: a caller on another thread may still be walking it,
    /// C has no way to say when it is done, and the channels of an element
    /// change rarely enough - a device opened, a format negotiated - that
    /// keeping the superseded lists costs a few nodes per change.
    /// </para>
    /// </remarks>
    private static nint Answer(nint balance, nint[]? handles)
    {
        lock (ListsGate)
        {
            ChannelLists* lists = (ChannelLists*)GObjectNative.ObjectGetQdata(balance, ListsQuark);
            nint newest = lists is null || lists->Newest is null ? nint.Zero : lists->Newest->List;

            if (handles is null || GListMarshal.Collect(newest).AsSpan().SequenceEqual(handles))
            {
                return newest;
            }

            // Everything that can fail is built first and attached to the
            // element only once it all exists, and the references are taken
            // last, so a failure on the way leaves nothing allocated and no
            // reference behind, and the element keeps the list it had.
            nint spine = GListMarshal.BuildSpine(handles, singly: false);
            ListNode* node = null;
            ChannelLists* created = null;

            try
            {
                node = (ListNode*)NativeMemory.AllocZeroed((nuint)sizeof(ListNode));

                if (lists is null)
                {
                    created = (ChannelLists*)NativeMemory.AllocZeroed((nuint)sizeof(ChannelLists));
                }
            }
            catch
            {
                NativeMemory.Free(node);
                GListMarshal.FreeSpine(spine, singly: false);
                throw;
            }

            node->List = spine;

            if (created is not null)
            {
                GObjectNative.ObjectSetQdataFull(balance, ListsQuark, (nint)created, &ReleaseLists);
                lists = created;
            }

            for (int i = 0; i < handles.Length; i++)
            {
                _ = GObjectNative.ObjectRef(handles[i]);
            }

            node->Older = lists->Newest;
            lists->Newest = node;
            return node->List;
        }
    }

    /// <summary>Answers the list an element lent last, without asking it again.</summary>
    /// <param name="balance">The element.</param>
    /// <returns>The list, or <c>NULL</c> when it never lent one.</returns>
    /// <remarks>
    /// This is the answer of last resort, for a failure in the runtime itself:
    /// it only reads, so it cannot fail the way building a list can.
    /// </remarks>
    private static nint Newest(nint balance)
    {
        lock (ListsGate)
        {
            ChannelLists* lists = (ChannelLists*)GObjectNative.ObjectGetQdata(balance, ListsQuark);
            return lists is null || lists->Newest is null ? nint.Zero : lists->Newest->List;
        }
    }

    /// <summary>
    /// Reads the handles of the channels an implementation answered.
    /// </summary>
    /// <param name="channels">What <c>ListChannels</c> answered.</param>
    /// <param name="type">The managed type, which the message names.</param>
    /// <returns>The handles, in order.</returns>
    /// <exception cref="InvalidOperationException">
    /// The answer is <see langword="null"/> or holds a <see langword="null"/>
    /// entry.
    /// </exception>
    /// <exception cref="ObjectDisposedException">One of the channels was disposed.</exception>
    private static nint[] HandlesOf(IReadOnlyList<ColorBalanceChannel>? channels, Type type)
    {
        if (channels is null)
        {
            throw new InvalidOperationException(
                $"{type}.ListChannels answered null. An element without channels answers an empty list.");
        }

        nint[] handles = new nint[channels.Count];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = channels[i]?.Handle
                ?? throw new InvalidOperationException(
                    $"{type}.ListChannels answered a list with an empty entry, which list_channels does not "
                    + "allow.");
        }

        return handles;
    }

    /// <summary>
    /// Names the type whose declaration attached the interface to an instance.
    /// </summary>
    /// <param name="balance">The native instance.</param>
    /// <returns>The managed type the declaration was made for, or a placeholder.</returns>
    private static string DeclaredFor(nint balance)
    {
        GType instanceType = TypeRegistry.GetInstanceType(balance);

        return SubclassRegistry.TryGetInterface(instanceType, InterfaceType, out InterfaceImplementation? found)
            && found is ColorBalance implementation
                ? implementation.DeclaredFor.ToString()
                : "another type";
    }

    /// <summary>
    /// Warns that an instance cannot answer for a slot, the way GLib warns.
    /// </summary>
    /// <param name="balance">The native instance.</param>
    /// <param name="wrapper">The wrapper, which may be null.</param>
    /// <param name="slot">The C function the call came through.</param>
    /// <remarks>
    /// The two cases are not the same mistake and must not read as if they
    /// were. No wrapper at all is the window of §5.4 - a disposed wrapper, or
    /// an instance of the type being constructed on this thread. A wrapper that
    /// is not an <see cref="IColorBalanceImplementation"/> is a
    /// misconfiguration: the declaration of one type was put into the
    /// registration of another.
    /// </remarks>
    private static void WarnUnanswered(nint balance, Gst.GObject.Object? wrapper, string slot)
    {
        GType type = TypeRegistry.GetInstanceType(balance);
        string name = type.IsValid ? type.Name : "a color balance";

        string reason = wrapper is null
            ? string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "\"{0}\" has no managed instance to answer for it.",
                name)
            : string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "\"{0}\" does not implement IColorBalanceImplementation: the GstColorBalance declaration in its "
                    + "registration was made for {1}.",
                name,
                DeclaredFor(balance));

        GLibNative.Warn(LogDomain, slot + ": " + reason);
    }

    /// <summary>
    /// Returns the managed channel a slot was handed, as the element holds it.
    /// </summary>
    /// <param name="channel">The native channel, which is not null.</param>
    /// <returns>
    /// The interned wrapper, or a new one, which takes a reference of its own
    /// the way every wrapper of an object does.
    /// </returns>
    private static ColorBalanceChannel? ChannelOf(nint channel) =>
        Gst.GObject.Object.FromNative<ColorBalanceChannel>(channel, Transfer.None);

    /// <summary>The <c>list_channels</c> slot.</summary>
    /// <param name="balance">The instance.</param>
    /// <returns>A list the element owns, or <c>NULL</c> for no channels.</returns>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint ListChannelsTrampoline(nint balance)
    {
        try
        {
            Gst.GObject.Object? wrapper = Gst.GObject.Object.TryGetOrFabricate(balance);

            if (wrapper is not IColorBalanceImplementation managed)
            {
                WarnUnanswered(balance, wrapper, "gst_color_balance_list_channels");
                return Answer(balance, null);
            }

            nint[]? handles = null;
            IReadOnlyList<ColorBalanceChannel>? channels = null;

            try
            {
                channels = managed.ListChannels();
                handles = HandlesOf(channels, wrapper.GetType());
            }
            catch (Exception exception)
            {
                ExceptionTrap.Report(exception);
            }

            nint answer = Answer(balance, handles);

            // The channels have to outlive the references Answer takes on them:
            // a wrapper the collector finalized before then would have released
            // the only reference the channel had.
            GC.KeepAlive(channels);
            return answer;
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return Newest(balance);
        }
    }

    /// <summary>The <c>set_value</c> slot.</summary>
    /// <param name="balance">The instance.</param>
    /// <param name="channel">The channel, borrowed for the call.</param>
    /// <param name="value">The new value.</param>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void SetValueTrampoline(nint balance, nint channel, int value)
    {
        try
        {
            if (channel == nint.Zero)
            {
                GLibNative.Warn(LogDomain, "gst_color_balance_set_value: the channel is NULL.");
                return;
            }

            Gst.GObject.Object? wrapper = Gst.GObject.Object.TryGetOrFabricate(balance);

            if (wrapper is not IColorBalanceImplementation managed)
            {
                WarnUnanswered(balance, wrapper, "gst_color_balance_set_value");
                return;
            }

            if (ChannelOf(channel) is { } managedChannel)
            {
                managed.SetValue(managedChannel, value);
            }
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }

    /// <summary>The <c>get_value</c> slot.</summary>
    /// <param name="balance">The instance.</param>
    /// <param name="channel">The channel, borrowed for the call.</param>
    /// <returns>The value, or the minimum of the channel when there is no answer.</returns>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetValueTrampoline(nint balance, nint channel)
    {
        if (channel == nint.Zero)
        {
            try
            {
                GLibNative.Warn(LogDomain, "gst_color_balance_get_value: the channel is NULL.");
            }
            catch (Exception exception)
            {
                ExceptionTrap.Report(exception);
            }

            return 0;
        }

        // What gst_color_balance_get_value answers for an element that
        // implements no get_value.
        int fallback = ((ColorBalanceChannelRaw*)channel)->MinValue;

        try
        {
            Gst.GObject.Object? wrapper = Gst.GObject.Object.TryGetOrFabricate(balance);

            if (wrapper is not IColorBalanceImplementation managed)
            {
                WarnUnanswered(balance, wrapper, "gst_color_balance_get_value");
                return fallback;
            }

            return ChannelOf(channel) is { } managedChannel ? managed.GetValue(managedChannel) : fallback;
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return fallback;
        }
    }

    /// <summary>The <c>get_balance_type</c> slot.</summary>
    /// <param name="balance">The instance.</param>
    /// <returns>The balance type, or software when there is no answer.</returns>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetBalanceTypeTrampoline(nint balance)
    {
        try
        {
            Gst.GObject.Object? wrapper = Gst.GObject.Object.TryGetOrFabricate(balance);

            if (wrapper is not IColorBalanceImplementation managed)
            {
                WarnUnanswered(balance, wrapper, "gst_color_balance_get_balance_type");
                return (int)ColorBalanceType.Software;
            }

            return (int)managed.BalanceType;
        }
        catch (Exception exception)
        {
            // What gst_color_balance_get_balance_type answers for an element
            // that implements no get_balance_type.
            ExceptionTrap.Report(exception);
            return (int)ColorBalanceType.Software;
        }
    }

    /// <summary>
    /// Releases every channel list of an element, which GObject calls when the
    /// element is finalized.
    /// </summary>
    /// <param name="data">The <see cref="ChannelLists"/> of the element.</param>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ReleaseLists(nint data)
    {
        try
        {
            ChannelLists* lists = (ChannelLists*)data;
            ListNode* node = lists->Newest;

            while (node is not null)
            {
                foreach (nint channel in GListMarshal.Collect(node->List))
                {
                    GObjectNative.ObjectUnref(channel);
                }

                GListMarshal.FreeSpine(node->List, singly: false);

                ListNode* older = node->Older;
                NativeMemory.Free(node);
                node = older;
            }

            NativeMemory.Free(lists);
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }

    /// <summary>The channel lists one element has lent, newest first.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ChannelLists
    {
        /// <summary>The list <c>list_channels</c> answers now, or null before the first one.</summary>
        internal ListNode* Newest;
    }

    /// <summary>One list an element lent, with the one it replaced.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ListNode
    {
        /// <summary>
        /// The <c>GList</c> of channels, each holding a reference of the list's
        /// own, or <c>NULL</c> for no channels.
        /// </summary>
        internal nint List;

        /// <summary>The list this one replaced, or null.</summary>
        internal ListNode* Older;
    }

    /// <summary>
    /// The implementation of <c>GstColorBalance</c> for one managed type.
    /// </summary>
    private sealed class ColorBalance : InterfaceImplementation
    {
        /// <summary>Records which type the declaration was made for.</summary>
        /// <param name="interfaceType">The type of <c>GstColorBalance</c>.</param>
        /// <param name="declaredFor">The managed type the declaration was made for.</param>
        internal ColorBalance(GType interfaceType, Type declaredFor)
            : base(interfaceType) => DeclaredFor = declaredFor;

        /// <summary>Gets the managed type <c>For</c> was called on.</summary>
        internal Type DeclaredFor { get; }

        /// <inheritdoc/>
        internal override void InitializeVTable(void* iface, GType instanceType)
        {
            _ = instanceType;

            // value_changed is the class handler of the signal, which the
            // interface initialised to NULL and which stays that way: the
            // signal is emitted by the implementation, not answered by it.
            byte* vtable = (byte*)iface;
            *(nint*)(vtable + GstColorBalanceInterfaceRaw.ListChannelsOffset) =
                (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&ListChannelsTrampoline;
            *(nint*)(vtable + GstColorBalanceInterfaceRaw.SetValueOffset) =
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, void>)&SetValueTrampoline;
            *(nint*)(vtable + GstColorBalanceInterfaceRaw.GetValueOffset) =
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&GetValueTrampoline;
            *(nint*)(vtable + GstColorBalanceInterfaceRaw.GetBalanceTypeOffset) =
                (nint)(delegate* unmanaged[Cdecl]<nint, int>)&GetBalanceTypeTrampoline;
        }
    }
}
