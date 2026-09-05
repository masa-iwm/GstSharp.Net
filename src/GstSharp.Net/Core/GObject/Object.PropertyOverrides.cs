using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <content>
/// The two property slots of <c>GObjectClass</c>, as a managed subclass takes
/// them over.
/// </content>
/// <remarks>
/// <para>
/// A property a managed subclass installs is answered by the subclass itself:
/// GObject looks the owner of the specification up and calls
/// <c>set_property</c>/<c>get_property</c> on <em>that</em> class, never on the
/// class of the instance when the two differ. So a managed slot is reached only
/// for a specification the managed type installed, and there is nothing to
/// chain up to — which is why this pair of overrides has no
/// <c>ChainUpSetProperty</c>/<c>ChainUpGetProperty</c>, unlike every other slot
/// the binding hands out. See <c>docs/subclassing.md</c> §4.4 and §5.6.
/// </para>
/// </remarks>
public partial class Object
{
    /// <summary>
    /// The log domain the runtime warns under.
    /// </summary>
    /// <remarks>
    /// <c>G_OBJECT_WARN_INVALID_PROPERTY_ID</c> is a macro, so the domain it
    /// warns under is the <c>G_LOG_DOMAIN</c> of whichever file expanded it —
    /// the subclass's own, not GObject's. A managed subclass has no such
    /// per-file domain, so the runtime names the one the macro carries when
    /// GObject itself expands it, which is where a reader of the message will
    /// look for it.
    /// </remarks>
    private const string PropertyLogDomain = "GLib-GObject";

    private static readonly VfuncOverride SetPropertySlot = CreateSetPropertyOverride();

    private static readonly VfuncOverride GetPropertySlot = CreateGetPropertyOverride();

    /// <summary>
    /// Gets the slot that answers <c>g_object_set_property</c> for the
    /// properties this type installed.
    /// </summary>
    /// <remarks>
    /// Declare it in <c>DefineSubclass</c> of any subclass that installs a
    /// writable property; <see cref="ObjectClassConfig.InstallProperty"/>
    /// refuses to install one without it, because GObject would only drop the
    /// value.
    /// </remarks>
    public static VfuncOverride SetPropertyOverride => SetPropertySlot;

    /// <summary>
    /// Gets the slot that answers <c>g_object_get_property</c> for the
    /// properties this type installed.
    /// </summary>
    /// <remarks>
    /// Declare it in <c>DefineSubclass</c> of any subclass that installs a
    /// readable property.
    /// </remarks>
    public static VfuncOverride GetPropertyOverride => GetPropertySlot;

    /// <summary>
    /// Emits <c>notify</c> for one property of this object.
    /// </summary>
    /// <param name="pspec">
    /// The specification of the property, which has to be one this object's
    /// type or one of its ancestors installed.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="pspec"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pspec"/> belongs to a type this object is not an
    /// instance of. GObject checks nothing here, so the runtime does.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Only a property whose specification carries
    /// <see cref="ParamFlags.ExplicitNotify"/> should be notified from its own
    /// setter.</b> Without that flag GObject emits <c>notify</c> itself once
    /// the setter has returned, so a setter that also calls this method
    /// produces two notifications for one change. With the flag GObject stays
    /// silent and the setter is the only thing that can tell anyone — and it
    /// should stay silent in turn when the value did not actually change, which
    /// is the whole point of asking for the flag.
    /// </para>
    /// <para>
    /// The call is safe from any thread and from inside a property setter: a
    /// notification raised while the object is being constructed, or while
    /// notifications are frozen, is queued and delivered when the freeze ends.
    /// </para>
    /// </remarks>
    public void Notify(ParamSpec pspec)
    {
        ArgumentNullException.ThrowIfNull(pspec);

        nint handle = Handle;
        GType owner = pspec.OwnerType;

        if (!owner.IsValid || !NativeType.IsA(owner))
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property '{0}' belongs to '{1}', which '{2}' is not an instance of.",
                    pspec.Name,
                    owner.IsValid ? owner.Name : "an uninstalled specification",
                    NativeType.Name),
                nameof(pspec));
        }

        GObjectNative.ObjectNotifyByPspec(handle, pspec.Handle);
    }

    /// <summary>
    /// Writes one property this type installed.
    /// </summary>
    /// <param name="propertyId">The identifier the property was installed with.</param>
    /// <param name="value">The new value, of the type of the property.</param>
    /// <param name="pspec">The specification of the property.</param>
    /// <remarks>
    /// <para>
    /// The method is reached only for a property this very type installed, so
    /// the usual shape is a <c>switch</c> over <paramref name="propertyId"/>
    /// whose default arm calls <c>base.OnSetProperty(...)</c> — which warns,
    /// the way GObject warns for an identifier no class claims. <b>There is no
    /// chain up.</b>
    /// </para>
    /// <para>
    /// The call can arrive on any thread, and it can arrive while the object is
    /// still inside <c>g_object_new</c>: an element a factory made from a
    /// pipeline description is given its properties before the caller of
    /// <c>gst_parse_launch</c> sees it. The wrapper exists by then — the
    /// runtime builds it here if this is the first managed contact with the
    /// instance — and no lock of the runtime is held, so a setter may call
    /// whatever it likes. What is <em>not</em> finished is the rest of the
    /// world around the instance: it is not in a bin, it has no peers, and
    /// nobody has been handed it yet. Store the value and leave anything that
    /// needs a pipeline to the state change that brings one.
    /// </para>
    /// <para>
    /// Do not notify from here unless the specification carries
    /// <see cref="ParamFlags.ExplicitNotify"/>; see <see cref="Notify"/>.
    /// </para>
    /// </remarks>
    protected virtual void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
    {
        WarnInvalidPropertyId("set", propertyId, pspec);
    }

    /// <summary>
    /// Reads one property this type installed.
    /// </summary>
    /// <param name="propertyId">The identifier the property was installed with.</param>
    /// <param name="value">
    /// The value to write the answer into, already initialised to the type of
    /// the property.
    /// </param>
    /// <param name="pspec">The specification of the property.</param>
    /// <remarks>
    /// As with <see cref="OnSetProperty"/>, the method is reached only for a
    /// property this very type installed, the default arm of the
    /// <c>switch</c> calls <c>base.OnGetProperty(...)</c> to warn, and there is
    /// no chain up. Leaving <paramref name="value"/> untouched answers the
    /// default of its type, which is what GObject does for a property no class
    /// claims.
    /// </remarks>
    protected virtual void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
    {
        WarnInvalidPropertyId("get", propertyId, pspec);
    }

    private static unsafe VfuncOverride CreateSetPropertyOverride() => new(
        &DeclaringObjectType,
        GObjectClassRaw.SetPropertyOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, uint, GValueNative*, nint, void>)&SetPropertyTrampoline);

    private static unsafe VfuncOverride CreateGetPropertyOverride() => new(
        &DeclaringObjectType,
        GObjectClassRaw.GetPropertyOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, uint, GValueNative*, nint, void>)&GetPropertyTrampoline);

    private static nuint DeclaringObjectType() => GObjectNative.ObjectGetType();

    /// <summary>
    /// Warns about an identifier no property of this object was installed with,
    /// the way <c>G_OBJECT_WARN_INVALID_PROPERTY_ID</c> does.
    /// </summary>
    /// <param name="operation">Either <c>set</c> or <c>get</c>.</param>
    /// <param name="propertyId">The identifier nothing claimed.</param>
    /// <param name="pspec">The specification the slot was handed.</param>
    /// <remarks>
    /// The macro names the type of the specification and the type of the
    /// <em>instance</em> (<c>G_OBJECT_TYPE_NAME</c>) — the offending element,
    /// which is the one thing that lets a reader find it. The owner of the
    /// specification is the class that installed it and is often not the same
    /// type at all, so the instance is what is named here as well.
    /// </remarks>
    private void WarnInvalidPropertyId(string operation, uint propertyId, ParamSpec pspec)
    {
        ArgumentNullException.ThrowIfNull(pspec);

        GLibNative.Warn(
            PropertyLogDomain,
            string.Format(
                CultureInfo.InvariantCulture,
                "invalid property id {0} for \"{1}\" of type '{2}' in '{3}' ({4}_property)",
                propertyId,
                pspec.Name,
                ParamSpec.NativeTypeOf(pspec.Handle).Name,
                NativeType.Name,
                operation));
    }

    /// <summary>
    /// The <c>set_property</c> slot of every managed subclass.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void SetPropertyTrampoline(nint instance, uint propertyId, GValueNative* value, nint pspec)
    {
        try
        {
            if (TryGetOrFabricate(instance) is not Object wrapper)
            {
                WarnMissingWrapper("set", propertyId, pspec);
                return;
            }

            if (value is null)
            {
                return;
            }

            ParamSpec? interned = SubclassRegistry.InstalledSpecFor(pspec);
            ParamSpec spec = interned ?? ParamSpec.FromNative(pspec, Transfer.None);

            try
            {
                wrapper.OnSetProperty(propertyId, new ValueView(ref *value), spec);
            }
            finally
            {
                if (interned is null)
                {
                    spec.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }

    /// <summary>
    /// The <c>get_property</c> slot of every managed subclass.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void GetPropertyTrampoline(nint instance, uint propertyId, GValueNative* value, nint pspec)
    {
        try
        {
            if (TryGetOrFabricate(instance) is not Object wrapper)
            {
                WarnMissingWrapper("get", propertyId, pspec);
                return;
            }

            if (value is null)
            {
                return;
            }

            ParamSpec? interned = SubclassRegistry.InstalledSpecFor(pspec);
            ParamSpec spec = interned ?? ParamSpec.FromNative(pspec, Transfer.None);

            try
            {
                wrapper.OnGetProperty(propertyId, new ValueRef(ref *value), spec);
            }
            finally
            {
                if (interned is null)
                {
                    spec.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }

    /// <summary>
    /// Reports the one case the slot cannot answer: an instance whose wrapper
    /// was collected and could not be rebuilt, or one that is still inside the
    /// constructor of its own type.
    /// </summary>
    /// <remarks>
    /// Chaining up is not an option, because the implementation below is the
    /// one of <c>GObject</c>, which knows nothing of this identifier and would
    /// warn as well — after having been handed a value that belongs to a
    /// different class. Dropping the call and saying so is the honest answer.
    /// </remarks>
    private static void WarnMissingWrapper(string operation, uint propertyId, nint pspec)
    {
        string name = pspec == nint.Zero
            ? "?"
            : GMarshal.PtrToStringUtf8(GObjectNative.ParamSpecGetName(pspec)) ?? "?";

        GLibNative.Warn(
            PropertyLogDomain,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}_property for \"{1}\" (id {2}) found no managed wrapper for the instance and dropped the call.",
                operation,
                name,
                propertyId));
    }
}
