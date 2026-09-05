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

    /// <summary>
    /// Attaches a word to an object under a quark. The runtime uses it for one
    /// marker only — that the wrapper of a managed subclass was disposed — and
    /// passes no destroy notification, so nothing managed is kept alive by it.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_object_set_qdata")]
    internal static partial void ObjectSetQdata(nint instance, uint quark, nint data);

    /// <summary>Reads back what <see cref="ObjectSetQdata"/> attached.</summary>
    [LibraryImport("GObject", EntryPoint = "g_object_get_qdata")]
    internal static partial nint ObjectGetQdata(nint instance, uint quark);

    [LibraryImport("GObject", EntryPoint = "g_object_get_property")]
    internal static partial void ObjectGetProperty(nint instance, byte* name, ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_object_set_property")]
    internal static partial void ObjectSetProperty(nint instance, byte* name, ref GValueNative value);

    [LibraryImport("GObject", EntryPoint = "g_object_class_find_property")]
    internal static partial nint ObjectClassFindProperty(nint objectClass, byte* name);

    /// <summary>
    /// Tells whether every value of one type can be turned into a value of
    /// another, which is the exact question <c>g_object_set_property</c>
    /// decides a write on: a transformable pair goes through, anything else is
    /// a console warning and no write at all.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_value_type_transformable")]
    internal static partial int ValueTypeTransformable(nuint sourceType, nuint targetType);

    /// <summary>
    /// Lists the properties of a class. The array is a fresh allocation the
    /// caller frees with <c>g_free</c>; the specifications in it belong to the
    /// class and are not reffed.
    /// </summary>
    /// <param name="objectClass">The <c>GObjectClass</c> to inspect.</param>
    /// <param name="count">The number of entries of the array.</param>
    /// <returns>The array of <c>GParamSpec *</c>.</returns>
    [LibraryImport("GObject", EntryPoint = "g_object_class_list_properties")]
    internal static partial nint* ObjectClassListProperties(nint objectClass, out uint count);

    [LibraryImport("GObject", EntryPoint = "g_object_new_with_properties")]
    internal static partial nint ObjectNewWithProperties(
        nuint objectType,
        uint propertyCount,
        byte** names,
        GValueNative* values);

    [LibraryImport("GObject", EntryPoint = "g_type_register_static")]
    internal static partial nuint TypeRegisterStatic(
        nuint parentType,
        byte* typeName,
        GTypeInfo* info,
        int flags);

    [LibraryImport("GObject", EntryPoint = "g_type_add_interface_static")]
    internal static partial void TypeAddInterfaceStatic(
        nuint instanceType,
        nuint interfaceType,
        GInterfaceInfo* info);

    [LibraryImport("GObject", EntryPoint = "g_type_query")]
    internal static partial void TypeQuery(nuint type, out GTypeQuery query);

    [LibraryImport("GObject", EntryPoint = "g_type_class_peek_parent")]
    internal static partial nint TypeClassPeekParent(nint gClass);

    [LibraryImport("GObject", EntryPoint = "g_type_class_ref")]
    internal static partial nint TypeClassRef(nuint type);

    [LibraryImport("GObject", EntryPoint = "g_type_class_unref")]
    internal static partial void TypeClassUnref(nint gClass);

    [LibraryImport("GObject", EntryPoint = "g_type_class_peek")]
    internal static partial nint TypeClassPeek(nuint type);

    /// <summary>
    /// Builds the default vtable of an interface type, which is what runs its
    /// <c>base_init</c> and so registers the signals it declares.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_type_default_interface_ref")]
    internal static partial nint DefaultInterfaceRef(nuint type);

    /// <summary>Releases what <see cref="DefaultInterfaceRef"/> returned.</summary>
    [LibraryImport("GObject", EntryPoint = "g_type_default_interface_unref")]
    internal static partial void DefaultInterfaceUnref(nint gInterface);

    /// <summary>
    /// Tests the flags of a type, that is what <c>G_TYPE_IS_INSTANTIATABLE</c>
    /// and its siblings expand to.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_type_test_flags")]
    internal static partial int TypeTestFlags(nuint type, uint flags);

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

    /// <summary>
    /// Reads the nickname of a specification. The string belongs to the
    /// specification and is never null: GObject falls back to the nickname of
    /// the redirect target and then to the name.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_get_nick")]
    internal static partial nint ParamSpecGetNick(nint pspec);

    /// <summary>
    /// Reads the description of a specification. The string belongs to the
    /// specification and may be null, which is what a property installed
    /// without one answers.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_get_blurb")]
    internal static partial nint ParamSpecGetBlurb(nint pspec);

    /// <summary>
    /// Reads the specification a <c>GParamSpecOverride</c> stands for, and null
    /// for every other class of specification. The reference is not the
    /// caller's.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_get_redirect_target")]
    internal static partial nint ParamSpecGetRedirectTarget(nint pspec);

    /// <summary>
    /// Reads the default value of a property. The value belongs to the
    /// specification, is built once on first use and lives as long as the
    /// specification does, so it is borrowed rather than copied.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_get_default_value")]
    internal static partial GValueNative* ParamSpecGetDefaultValue(nint pspec);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_ref_sink")]
    internal static partial nint ParamSpecRefSink(nint pspec);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_unref")]
    internal static partial void ParamSpecUnref(nint pspec);

    /// <summary>
    /// Answers whether a string may name a property. GObject terminates the
    /// process on an invalid name inside every constructor below, because they
    /// dereference the result of <c>g_param_spec_internal</c> without testing
    /// it, so the managed side asks this first.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_is_valid_name")]
    internal static partial int ParamSpecIsValidName(byte* name);

    /// <summary>
    /// Answers whether a type may be carried by a <c>GValue</c>, which is what
    /// a boxed property additionally requires of its type.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_type_check_is_value_type")]
    internal static partial int TypeCheckIsValueType(nuint type);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_boolean")]
    internal static partial nint ParamSpecBoolean(byte* name, byte* nick, byte* blurb, int defaultValue, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_char")]
    internal static partial nint ParamSpecChar(
        byte* name,
        byte* nick,
        byte* blurb,
        sbyte minimum,
        sbyte maximum,
        sbyte defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_uchar")]
    internal static partial nint ParamSpecUChar(
        byte* name,
        byte* nick,
        byte* blurb,
        byte minimum,
        byte maximum,
        byte defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_int")]
    internal static partial nint ParamSpecInt(
        byte* name,
        byte* nick,
        byte* blurb,
        int minimum,
        int maximum,
        int defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_uint")]
    internal static partial nint ParamSpecUInt(
        byte* name,
        byte* nick,
        byte* blurb,
        uint minimum,
        uint maximum,
        uint defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_long")]
    internal static partial nint ParamSpecLong(
        byte* name,
        byte* nick,
        byte* blurb,
        CLong minimum,
        CLong maximum,
        CLong defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_ulong")]
    internal static partial nint ParamSpecULong(
        byte* name,
        byte* nick,
        byte* blurb,
        CULong minimum,
        CULong maximum,
        CULong defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_int64")]
    internal static partial nint ParamSpecInt64(
        byte* name,
        byte* nick,
        byte* blurb,
        long minimum,
        long maximum,
        long defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_uint64")]
    internal static partial nint ParamSpecUInt64(
        byte* name,
        byte* nick,
        byte* blurb,
        ulong minimum,
        ulong maximum,
        ulong defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_float")]
    internal static partial nint ParamSpecFloat(
        byte* name,
        byte* nick,
        byte* blurb,
        float minimum,
        float maximum,
        float defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_double")]
    internal static partial nint ParamSpecDouble(
        byte* name,
        byte* nick,
        byte* blurb,
        double minimum,
        double maximum,
        double defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_unichar")]
    internal static partial nint ParamSpecUnichar(byte* name, byte* nick, byte* blurb, uint defaultValue, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_enum")]
    internal static partial nint ParamSpecEnum(
        byte* name,
        byte* nick,
        byte* blurb,
        nuint enumType,
        int defaultValue,
        uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_flags")]
    internal static partial nint ParamSpecFlags(
        byte* name,
        byte* nick,
        byte* blurb,
        nuint flagsType,
        uint defaultValue,
        uint flags);

    /// <summary>
    /// Builds the specification of a string property. The default is copied by
    /// GObject, so the buffer it is passed may be released once the call has
    /// returned.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_string")]
    internal static partial nint ParamSpecString(byte* name, byte* nick, byte* blurb, byte* defaultValue, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_param")]
    internal static partial nint ParamSpecParam(byte* name, byte* nick, byte* blurb, nuint paramType, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_boxed")]
    internal static partial nint ParamSpecBoxed(byte* name, byte* nick, byte* blurb, nuint boxedType, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_pointer")]
    internal static partial nint ParamSpecPointer(byte* name, byte* nick, byte* blurb, uint flags);

    [LibraryImport("GObject", EntryPoint = "g_param_spec_object")]
    internal static partial nint ParamSpecObject(byte* name, byte* nick, byte* blurb, nuint objectType, uint flags);

    /// <summary>
    /// Builds the specification of a type property. <c>G_TYPE_NONE</c> as the
    /// type stands for every type, which is how GObject spells "no bound".
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_gtype")]
    internal static partial nint ParamSpecGType(byte* name, byte* nick, byte* blurb, nuint isAType, uint flags);

    /// <summary>
    /// Builds the specification of a variant property. The type is copied and a
    /// default is referenced and sunk. Neither <c>GVariant</c> nor
    /// <c>GVariantType</c> is bound, so nothing calls this yet; it is imported
    /// beside its siblings so the set is complete.
    /// </summary>
    [LibraryImport("GObject", EntryPoint = "g_param_spec_variant")]
    internal static partial nint ParamSpecVariant(
        byte* name,
        byte* nick,
        byte* blurb,
        nint type,
        nint defaultValue,
        uint flags);

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

    [LibraryImport("GObject", EntryPoint = "g_gtype_get_type")]
    internal static partial nuint GTypeGetType();

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

    /// <summary>The <c>get_type</c> of <c>GObject</c> itself.</summary>
    /// <remarks>
    /// It is imported so that the two property slots the runtime hands out as
    /// <c>VfuncOverride</c> values can name the class that declares them, the
    /// way every generated slot names its own class.
    /// </remarks>
    [LibraryImport("GObject", EntryPoint = "g_object_get_type")]
    internal static partial nuint ObjectGetType();

    /// <summary>
    /// Installs a property on a class that is being initialised.
    /// </summary>
    /// <remarks>
    /// The call sinks the specification and the pool takes a reference of its
    /// own, so the caller may still release the wrapper it built the
    /// specification with.
    /// </remarks>
    [LibraryImport("GObject", EntryPoint = "g_object_class_install_property")]
    internal static partial void ObjectClassInstallProperty(nint objectClass, uint propertyId, nint pspec);

    /// <summary>Emits <c>notify</c> for one property, from any thread.</summary>
    [LibraryImport("GObject", EntryPoint = "g_object_notify_by_pspec")]
    internal static partial void ObjectNotifyByPspec(nint instance, nint pspec);

    /// <summary>Tests a signal name without the crash a bad one would cause.</summary>
    [LibraryImport("GObject", EntryPoint = "g_signal_is_valid_name")]
    internal static partial int SignalIsValidName(byte* name);

    /// <summary>
    /// Creates a signal on a type, in the non variadic form.
    /// </summary>
    /// <remarks>
    /// <c>g_signal_new</c> is variadic in its parameter types, so the array
    /// form is the only one that can be imported. A <c>NULL</c> C marshaller
    /// asks GObject for its generic marshaller, which serves the plain and the
    /// <c>va_list</c> path alike.
    /// </remarks>
    [LibraryImport("GObject", EntryPoint = "g_signal_newv")]
    internal static partial uint SignalNewV(
        byte* signalName,
        nuint itype,
        uint signalFlags,
        nint classClosure,
        nint accumulator,
        nint accumulatorData,
        nint cMarshaller,
        nuint returnType,
        uint parameterCount,
        nuint* parameterTypes);
}
