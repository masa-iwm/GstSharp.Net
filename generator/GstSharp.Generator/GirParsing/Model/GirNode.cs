namespace GstSharp.Generator.GirParsing.Model;

/// <summary>
/// Ownership transfer modes of the <c>transfer-ownership</c> attribute.
/// </summary>
internal enum GirTransfer
{
    /// <summary>The attribute was absent.</summary>
    Unspecified,

    /// <summary><c>none</c>: the callee/caller keeps ownership.</summary>
    None,

    /// <summary><c>container</c>: only the container is transferred.</summary>
    Container,

    /// <summary><c>full</c>: the container and its contents are transferred.</summary>
    Full,

    /// <summary><c>floating</c>: a floating reference is transferred.</summary>
    Floating,
}

/// <summary>
/// Parameter directions of the <c>direction</c> attribute.
/// </summary>
internal enum GirDirection
{
    /// <summary><c>in</c>, which is also the default.</summary>
    In,

    /// <summary><c>out</c>.</summary>
    Out,

    /// <summary><c>inout</c>.</summary>
    InOut,
}

/// <summary>
/// Callback lifetimes of the <c>scope</c> attribute.
/// </summary>
internal enum GirScope
{
    /// <summary>The attribute was absent.</summary>
    Unspecified,

    /// <summary><c>call</c>: valid for the duration of the call.</summary>
    Call,

    /// <summary><c>async</c>: valid until the callback fires once.</summary>
    Async,

    /// <summary><c>notified</c>: valid until the destroy notify runs.</summary>
    Notified,

    /// <summary><c>forever</c>: valid until the process exits.</summary>
    Forever,
}

/// <summary>
/// Common attributes shared by all gir elements. Names are kept verbatim; no
/// C# naming happens in this layer.
/// </summary>
internal abstract class GirNode
{
    /// <summary>Gets the verbatim <c>name</c> attribute.</summary>
    internal string Name { get; init; } = string.Empty;

    /// <summary>Gets the text of the <c>&lt;doc&gt;</c> child, if any.</summary>
    internal string? Doc { get; init; }

    /// <summary>Gets the text of the <c>&lt;doc-deprecated&gt;</c> child, if any.</summary>
    internal string? DocDeprecated { get; init; }

    /// <summary>Gets the <c>version</c> attribute, if any.</summary>
    internal string? Version { get; init; }

    /// <summary>Gets a value indicating whether <c>deprecated="1"</c> was set.</summary>
    internal bool IsDeprecated { get; init; }

    /// <summary>Gets the <c>deprecated-version</c> attribute, if any.</summary>
    internal string? DeprecatedVersion { get; init; }

    /// <summary>
    /// Gets a value indicating whether the element is introspectable. Defaults
    /// to <see langword="true"/>; <c>introspectable="0"/</c> clears it.
    /// </summary>
    internal bool IsIntrospectable { get; init; } = true;
}
