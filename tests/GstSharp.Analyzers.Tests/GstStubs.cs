namespace GstSharp.Analyzers.Tests;

/// <summary>
/// Minimal stand-ins for the product types the rules match on.
/// </summary>
/// <remarks>
/// The analyzers match by metadata name and never reference the product
/// assemblies, so the tests do not need them either. Reproducing the shapes here
/// keeps the test compilations tiny and independent of the binding build.
/// </remarks>
internal static class GstStubs
{
    /// <summary>The stub compilation unit added to every test.</summary>
    internal const string Source = """
        namespace Gst
        {
            public abstract class MiniObject : System.IDisposable
            {
                public void Dispose()
                {
                }
            }

            public enum MapFlags
            {
                Read = 1,
                Write = 2,
            }

            public sealed class Sample : MiniObject
            {
                public Buffer Buffer => null;

                public Caps Caps => null;
            }

            public sealed class Caps : MiniObject
            {
            }

            public sealed class Buffer : MiniObject
            {
                public MapScope Map(MapFlags flags) => default;

                public ref struct MapScope
                {
                    public int Size => 0;

                    public void Dispose()
                    {
                    }
                }
            }

            public sealed class Structure : GObject.Boxed
            {
            }

            public sealed class AppSink
            {
                public Sample PullSample() => null;

                public Sample TryPullSample(ulong timeout) => null;

                public Buffer PullBuffer() => null;
            }

            namespace GObject
            {
                public abstract class Boxed : System.IDisposable
                {
                    public void Dispose()
                    {
                    }
                }
            }
        }
        """;
}
