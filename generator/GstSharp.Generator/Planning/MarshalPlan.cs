using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Planning;

/// <summary>
/// The C# shape a callable is emitted in.
/// </summary>
internal enum CallableForm
{
    /// <summary>An instance method; the gir instance parameter becomes <c>this</c>.</summary>
    InstanceMethod,

    /// <summary>A static method.</summary>
    StaticMethod,

    /// <summary>A static factory method that wraps a gir constructor.</summary>
    Constructor,

    /// <summary>An extension method; the instance is the first parameter.</summary>
    ExtensionMethod,
}

/// <summary>
/// How one argument crosses the interop boundary.
/// </summary>
internal enum ArgumentKind
{
    /// <summary>The instance the method is called on.</summary>
    Instance,

    /// <summary>Passed through unchanged.</summary>
    Value,

    /// <summary>A <c>gboolean</c>.</summary>
    Boolean,

    /// <summary>A generated enumeration or bitfield.</summary>
    Enumeration,

    /// <summary>A blittable value with a wrapper on the public surface, such as <c>GstClockTime</c>.</summary>
    Wrapper,

    /// <summary>An untyped pointer, passed as a native integer.</summary>
    Pointer,

    /// <summary>A string the callee only reads.</summary>
    Utf8,

    /// <summary>A string the callee takes ownership of.</summary>
    Utf8Owned,

    /// <summary>An instance behind a handle.</summary>
    Handle,

    /// <summary>A blittable structure, passed by value or through a pointer.</summary>
    PlainStruct,

    /// <summary>A C array the callee only reads, passed as a span.</summary>
    Span,

    /// <summary>
    /// A fixed size array the callee writes into storage the caller provides,
    /// passed as an <c>out</c> parameter of the inline storage type named by
    /// <see cref="ArgumentPlan.FixedArray"/>.
    /// </summary>
    FixedArrayOut,

    /// <summary>A C array that the call produces.</summary>
    ArrayOut,

    /// <summary>A <c>NULL</c> terminated array of strings.</summary>
    Strv,

    /// <summary>
    /// A <c>GList</c> that a call returned, materialized into a read only list.
    /// The element projection is carried by <see cref="ReturnPlan.ElementKind"/>
    /// and <see cref="ReturnPlan.Flavor"/>.
    /// </summary>
    GListReturn,

    /// <summary>A managed callback handed to native code.</summary>
    Callback,

    /// <summary>The user data of a callback; never visible.</summary>
    UserData,

    /// <summary>The <c>GDestroyNotify</c> of a callback; never visible.</summary>
    DestroyNotify,

    /// <summary>The length of an array; never visible.</summary>
    ArrayLength,

    /// <summary>The <c>GError</c> of a callable that throws; never visible.</summary>
    Error,

    /// <summary>Nothing at all, for a <c>void</c> return.</summary>
    Void,
}

/// <summary>
/// How an argument is passed in C#.
/// </summary>
internal enum ArgumentDirection
{
    /// <summary>By value.</summary>
    In,

    /// <summary>As an <c>out</c> parameter.</summary>
    Out,

    /// <summary>As a <c>ref</c> parameter.</summary>
    Ref,
}

/// <summary>
/// The kind of wrapper a handle is turned into.
/// </summary>
internal enum HandleFlavor
{
    /// <summary>Not a handle.</summary>
    None,

    /// <summary>A <c>GObject</c>, wrapped through <c>Gst.GObject.Object.FromNative</c>.</summary>
    GObject,

    /// <summary>A mini object or a boxed type, which has a typed <c>FromNative</c>.</summary>
    Wrapper,

    /// <summary>An opaque record, whose wrapper is a bare pointer holder.</summary>
    Opaque,

    /// <summary>
    /// A <c>GParamSpec</c>, wrapped by the hand written
    /// <c>Gst.GObject.ParamSpec</c>. It is not a <c>GObject</c> and has no
    /// generated wrapper, so it is constructed directly; the constructor takes
    /// a reference of its own, which makes the wrapper owned by whoever created
    /// it. Only the signal planner produces this flavour.
    /// </summary>
    ParamSpec,
}

/// <summary>
/// The inline storage of a fixed size array, emitted as an
/// <c>[InlineArray]</c> struct beside the declaration that needs it.
/// </summary>
/// <param name="TypeName">The simple C# name of the storage type.</param>
/// <param name="ElementTypeName">The C# type of one element.</param>
/// <param name="Length">The number of elements.</param>
/// <remarks>
/// Two declarations use it. A fixed size <em>field</em> of a generated
/// structure is spelled with one, and so is a <em>parameter</em> that the
/// callee fills with a fixed number of elements; both are nested in the type
/// that declares them and are named after the field or the parameter with an
/// <c>Array</c> suffix, so that the storage a caller has to allocate says how
/// large it is.
/// </remarks>
internal sealed record InlineArrayInfo(string TypeName, string ElementTypeName, int Length);

/// <summary>
/// One argument of a planned callable, visible or hidden.
/// </summary>
internal sealed class ArgumentPlan
{
    /// <summary>Gets the gir parameter, or <see langword="null"/> for the instance.</summary>
    internal GirParameter? Source { get; init; }

    /// <summary>Gets how the argument is marshalled.</summary>
    internal required ArgumentKind Kind { get; init; }

    /// <summary>Gets the C# name of the parameter, which is also the base name of its locals.</summary>
    internal required string Name { get; init; }

    /// <summary>Gets the C# type on the public surface.</summary>
    internal string PublicType { get; init; } = string.Empty;

    /// <summary>Gets the C# type in the <c>LibraryImport</c> signature.</summary>
    internal required string RawType { get; init; }

    /// <summary>Gets how the argument is passed in C#.</summary>
    internal ArgumentDirection Direction { get; init; } = ArgumentDirection.In;

    /// <summary>Gets a value indicating whether the argument is absent from the public signature.</summary>
    internal bool IsHidden { get; init; }

    /// <summary>Gets a value indicating whether the argument accepts a null value.</summary>
    internal bool IsNullable { get; init; }

    /// <summary>Gets the ownership transfer of the argument.</summary>
    internal GirTransfer Transfer { get; init; }

    /// <summary>Gets the wrapper flavour of a handle.</summary>
    internal HandleFlavor Flavor { get; init; }

    /// <summary>Gets the element type of an array, on the public surface.</summary>
    internal string? ElementType { get; init; }

    /// <summary>Gets the index of the argument that carries the length of this array.</summary>
    internal int? LengthArgument { get; init; }

    /// <summary>
    /// Gets the inline storage type of a <see cref="ArgumentKind.FixedArrayOut"/>
    /// argument, which the declaring type declares beside its members.
    /// </summary>
    internal InlineArrayInfo? FixedArray { get; init; }

    /// <summary>
    /// Gets the index of the argument a hidden argument belongs to: the array
    /// of a length, or the callback of a user data pointer and of a destroy
    /// notification. <c>-1</c> stands for the return value.
    /// </summary>
    internal int? OwnerArgument { get; init; }

    /// <summary>Gets the delegate type of a callback argument.</summary>
    internal string? DelegateType { get; init; }

    /// <summary>Gets the trampoline holder of a callback argument.</summary>
    internal string? TrampolineType { get; init; }

    /// <summary>Gets the lifetime of a callback argument.</summary>
    internal GirScope Scope { get; init; }

    /// <summary>Gets the gir documentation of the parameter.</summary>
    internal string? Doc { get; init; }
}

/// <summary>
/// The return value of a planned callable.
/// </summary>
internal sealed class ReturnPlan
{
    /// <summary>Gets how the value is marshalled.</summary>
    internal required ArgumentKind Kind { get; init; }

    /// <summary>Gets the C# type on the public surface.</summary>
    internal required string PublicType { get; init; }

    /// <summary>Gets the C# type in the <c>LibraryImport</c> signature.</summary>
    internal required string RawType { get; init; }

    /// <summary>Gets the ownership transfer of the value.</summary>
    internal GirTransfer Transfer { get; init; }

    /// <summary>Gets a value indicating whether the value may be null.</summary>
    internal bool IsNullable { get; init; }

    /// <summary>Gets the wrapper flavour of a handle.</summary>
    internal HandleFlavor Flavor { get; init; }

    /// <summary>Gets the element type of an array, on the public surface.</summary>
    internal string? ElementType { get; init; }

    /// <summary>
    /// Gets how one element of a container is marshalled. Only
    /// <see cref="ArgumentKind.GListReturn"/> sets it, to
    /// <see cref="ArgumentKind.Handle"/> or <see cref="ArgumentKind.Utf8"/>.
    /// </summary>
    internal ArgumentKind ElementKind { get; init; }

    /// <summary>Gets the index of the argument that carries the length of the returned array.</summary>
    internal int? LengthArgument { get; init; }

    /// <summary>Gets the gir documentation of the return value.</summary>
    internal string? Doc { get; init; }

    /// <summary>Gets a value indicating whether the callable returns nothing.</summary>
    internal bool IsVoid => Kind == ArgumentKind.Void;
}

/// <summary>
/// Everything the emitters need to write one callable: the public signature,
/// the marshalling of each argument and the raw entry point behind it.
/// </summary>
internal sealed class MarshalPlan
{
    /// <summary>Gets the gir callable the plan was built from.</summary>
    internal required GirCallable Callable { get; init; }

    /// <summary>Gets the C# shape of the member.</summary>
    internal required CallableForm Form { get; init; }

    /// <summary>Gets the C# name of the member.</summary>
    internal required string Name { get; init; }

    /// <summary>Gets the <c>c:identifier</c> of the native entry point.</summary>
    internal required string EntryPoint { get; init; }

    /// <summary>Gets the name of the generated <c>LibraryImport</c> declaration.</summary>
    internal required string NativeName { get; init; }

    /// <summary>Gets the arguments, in gir order, including the hidden ones.</summary>
    internal required IReadOnlyList<ArgumentPlan> Arguments { get; init; }

    /// <summary>Gets the return value.</summary>
    internal required ReturnPlan Return { get; init; }

    /// <summary>Gets a value indicating whether the callable reports errors through a <c>GError</c>.</summary>
    internal bool Throws { get; init; }

    /// <summary>Gets the C# type of the instance, for an extension method.</summary>
    internal string? InstanceType { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the member overrides a member of
    /// <c>object</c>. Only <c>ToString</c> is ever overridden, and the emitters
    /// decide that after the plan was built, when they know the members of the
    /// declaring type.
    /// </summary>
    internal bool IsOverride { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the member hides one that a
    /// generated base class already carries, as <c>gst_pipeline_new</c> hides
    /// the factory of <c>GstBin</c>. Hiding is intended there, and saying so is
    /// what keeps the compiler quiet.
    /// </summary>
    internal bool IsNew { get; set; }
}
