using System.Runtime.InteropServices;
using Gst.GObject;

namespace Gst.Interop;

/// <summary>
/// Raw entry points of <c>libgobject-2.0</c> that the runtime needs.
/// </summary>
/// <remarks>
/// Every signature is blittable. <c>glong</c> and <c>gulong</c> are 32 bit wide
/// on Windows and 64 bit wide everywhere else, so they are imported as
/// <see cref="CLong"/> and <see cref="CULong"/> rather than as
/// <see cref="nint"/>.
/// </remarks>
internal static unsafe partial class GObjectNative
{
    /// <summary>Value of <c>G_CONNECT_AFTER</c>.</summary>
    internal const int ConnectAfter = 1;

    [LibraryImport("GObject", EntryPoint = "g_object_ref")]
    internal static partial nint ObjectRef(nint instance);

    [LibraryImport("GObject", EntryPoint = "g_object_unref")]
    internal static partial void ObjectUnref(nint instance);

    [LibraryImport("GObject", EntryPoint = "g_object_ref_sink")]
    internal static partial nint ObjectRefSink(nint instance);

    [LibraryImport("GObject", EntryPoint = "g_object_is_floating")]
    internal static partial int ObjectIsFloating(nint instance);

    [LibraryImport("GObject", EntryPoint = "g_object_add_toggle_ref")]
    internal static partial void ObjectAddToggleRef(
        nint instance,
        delegate* unmanaged[Cdecl]<nint, nint, int, void> notify,
        nint data);

    [LibraryImport("GObject", EntryPoint = "g_object_remove_toggle_ref")]
    internal static partial void ObjectRemoveToggleRef(
        nint instance,
        delegate* unmanaged[Cdecl]<nint, nint, int, void> notify,
        nint data);

    [LibraryImport("GObject", EntryPoint = "g_object_get_property")]
    internal static partial void ObjectGetProperty(nint instance, byte* name, ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_object_set_property")]
    internal static partial void ObjectSetProperty(nint instance, byte* name, ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_object_class_find_property")]
    internal static partial nint ObjectClassFindProperty(nint objectClass, byte* name);

    [LibraryImport("GObject", EntryPoint = "g_type_name")]
    internal static partial nint TypeName(nuint type);

    [LibraryImport("GObject", EntryPoint = "g_type_from_name")]
    internal static partial nuint TypeFromName(byte* name);

    [LibraryImport("GObject", EntryPoint = "g_type_parent")]
    internal static partial nuint TypeParent(nuint type);

    [LibraryImport("GObject", EntryPoint = "g_type_is_a")]
    internal static partial int TypeIsA(nuint type, nuint isAType);

    [LibraryImport("GObject", EntryPoint = "g_type_fundamental")]
    internal static partial nuint TypeFundamental(nuint type);

    [LibraryImport("GObject", EntryPoint = "g_initially_unowned_get_type")]
    internal static partial nuint InitiallyUnownedGetType();

    [LibraryImport("GObject", EntryPoint = "g_signal_connect_data")]
    internal static partial CULong SignalConnectData(
        nint instance,
        byte* detailedSignal,
        nint handler,
        nint data,
        delegate* unmanaged[Cdecl]<nint, nint, void> destroyData,
        int connectFlags);

    [LibraryImport("GObject", EntryPoint = "g_signal_handler_disconnect")]
    internal static partial void SignalHandlerDisconnect(nint instance, CULong handlerId);

    [LibraryImport("GObject", EntryPoint = "g_signal_handler_is_connected")]
    internal static partial int SignalHandlerIsConnected(nint instance, CULong handlerId);

    [LibraryImport("GObject", EntryPoint = "g_boxed_copy")]
    internal static partial nint BoxedCopy(nuint boxedType, nint sourceBoxed);

    [LibraryImport("GObject", EntryPoint = "g_boxed_free")]
    internal static partial void BoxedFree(nuint boxedType, nint boxed);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_get_name")]
    internal static partial nint ParamSpecGetName(nint pspec);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_ref_sink")]
    internal static partial nint ParamSpecRefSink(nint pspec);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_unref")]
    internal static partial void ParamSpecUnref(nint pspec);

    [LibraryImport("GObject", EntryPoint = "g_value_init")]
    internal static partial nint ValueInit(ref GValueNative value, nuint type);

    [LibraryImport("GObject", EntryPoint = "g_value_unset")]
    internal static partial void ValueUnset(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_reset")]
    internal static partial nint ValueReset(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_copy")]
    internal static partial void ValueCopy(ref GValueNative source, ref GValueNative destination);

    [LibraryImport("GObject", EntryPoint = "g_value_set_boolean")]
    internal static partial void ValueSetBoolean(ref GValueNative value, int content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_boolean")]
    internal static partial int ValueGetBoolean(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_int")]
    internal static partial void ValueSetInt(ref GValueNative value, int content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_int")]
    internal static partial int ValueGetInt(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_uint")]
    internal static partial void ValueSetUInt(ref GValueNative value, uint content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_uint")]
    internal static partial uint ValueGetUInt(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_long")]
    internal static partial void ValueSetLong(ref GValueNative value, CLong content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_long")]
    internal static partial CLong ValueGetLong(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_ulong")]
    internal static partial void ValueSetULong(ref GValueNative value, CULong content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_ulong")]
    internal static partial CULong ValueGetULong(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_int64")]
    internal static partial void ValueSetInt64(ref GValueNative value, long content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_int64")]
    internal static partial long ValueGetInt64(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_uint64")]
    internal static partial void ValueSetUInt64(ref GValueNative value, ulong content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_uint64")]
    internal static partial ulong ValueGetUInt64(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_float")]
    internal static partial void ValueSetFloat(ref GValueNative value, float content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_float")]
    internal static partial float ValueGetFloat(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_double")]
    internal static partial void ValueSetDouble(ref GValueNative value, double content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_double")]
    internal static partial double ValueGetDouble(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_string")]
    internal static partial void ValueSetString(ref GValueNative value, byte* content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_string")]
    internal static partial nint ValueGetString(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_pointer")]
    internal static partial void ValueSetPointer(ref GValueNative value, nint content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_pointer")]
    internal static partial nint ValueGetPointer(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_object")]
    internal static partial void ValueSetObject(ref GValueNative value, nint content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_object")]
    internal static partial nint ValueGetObject(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_boxed")]
    internal static partial void ValueSetBoxed(ref GValueNative value, nint content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_boxed")]
    internal static partial nint ValueGetBoxed(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_dup_boxed")]
    internal static partial nint ValueDupBoxed(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_dup_param")]
    internal static partial nint ValueDupParam(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_dup_variant")]
    internal static partial nint ValueDupVariant(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_enum")]
    internal static partial void ValueSetEnum(ref GValueNative value, int content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_enum")]
    internal static partial int ValueGetEnum(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_flags")]
    internal static partial void ValueSetFlags(ref GValueNative value, uint content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_flags")]
    internal static partial uint ValueGetFlags(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_gtype")]
    internal static partial void ValueSetGType(ref GValueNative value, nuint content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_gtype")]
    internal static partial nuint ValueGetGType(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_param")]
    internal static partial void ValueSetParam(ref GValueNative value, nint content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_param")]
    internal static partial nint ValueGetParam(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_variant")]
    internal static partial void ValueSetVariant(ref GValueNative value, nint content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_variant")]
    internal static partial nint ValueGetVariant(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_schar")]
    internal static partial void ValueSetSChar(ref GValueNative value, sbyte content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_schar")]
    internal static partial sbyte ValueGetSChar(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_uchar")]
    internal static partial void ValueSetUChar(ref GValueNative value, byte content);

    [LibraryImport("GObject", EntryPoint = "g_value_get_uchar")]
    internal static partial byte ValueGetUChar(ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_value_set_instance")]
    internal static partial void ValueSetInstance(ref GValueNative value, nint instance);

    [LibraryImport("GObject", EntryPoint = "g_type_interfaces")]
    internal static partial nint TypeInterfaces(nuint type, uint* count);

    [LibraryImport("GObject", EntryPoint = "g_signal_lookup")]
    internal static partial uint SignalLookup(byte* name, nuint itype);

    [LibraryImport("GObject", EntryPoint = "g_signal_query")]
    internal static partial void SignalQueryById(uint signalId, out GSignalQuery query);

    [LibraryImport("GObject", EntryPoint = "g_signal_parse_name")]
    internal static partial int SignalParseName(
        byte* detailedSignal,
        nuint itype,
        uint* signalId,
        uint* detail,
        int forceDetailQuark);

    [LibraryImport("GObject", EntryPoint = "g_signal_list_ids")]
    internal static partial nint SignalListIds(nuint itype, uint* count);

    [LibraryImport("GObject", EntryPoint = "g_signal_emitv")]
    internal static partial void SignalEmitV(
        GValueNative* instanceAndParams,
        uint signalId,
        uint detail,
        GValueNative* returnValue);

    [LibraryImport("GObject", EntryPoint = "g_signal_connect_closure_by_id")]
    internal static partial CULong SignalConnectClosureById(
        nint instance,
        uint signalId,
        uint detail,
        nint closure,
        int after);

    [LibraryImport("GObject", EntryPoint = "g_closure_new_simple")]
    internal static partial nint ClosureNewSimple(uint sizeOfClosure, nint data);

    [LibraryImport("GObject", EntryPoint = "g_closure_set_meta_marshal")]
    internal static partial void ClosureSetMetaMarshal(
        nint closure,
        nint marshalData,
        delegate* unmanaged[Cdecl]<nint, GValueNative*, uint, GValueNative*, nint, nint, void> metaMarshal);

    [LibraryImport("GObject", EntryPoint = "g_closure_add_finalize_notifier")]
    internal static partial void ClosureAddFinalizeNotifier(
        nint closure,
        nint notifyData,
        delegate* unmanaged[Cdecl]<nint, nint, void> notifyFunc);

    [LibraryImport("GObject", EntryPoint = "g_closure_sink")]
    internal static partial void ClosureSink(nint closure);
}
