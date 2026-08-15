using System.Xml.Linq;

namespace GstSharp.Generator.GirParsing;

/// <summary>
/// XML namespaces used by the GObject-introspection schema.
/// </summary>
internal static class GirXml
{
    /// <summary>The default gir namespace.</summary>
    internal static readonly XNamespace Core = "http://www.gtk.org/introspection/core/1.0";

    /// <summary>The <c>c:</c> gir namespace.</summary>
    internal static readonly XNamespace C = "http://www.gtk.org/introspection/c/1.0";

    /// <summary>The <c>glib:</c> gir namespace.</summary>
    internal static readonly XNamespace Glib = "http://www.gtk.org/introspection/glib/1.0";
}
