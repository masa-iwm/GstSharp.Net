using Gst.Analyzers;
using Xunit;

namespace GstSharp.Analyzers.Tests;

/// <summary>Tests for GST0002.</summary>
public sealed class UnmappedMapScopeAnalyzerTests
{
    private static Task VerifyAsync(string source) =>
        AnalyzerVerifier<UnmappedMapScopeAnalyzer>.VerifyAsync(source);

    [Fact]
    public Task DiscardedResult_IsReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Map(Buffer buffer)
            {
                {|GST0002:buffer.Map(MapFlags.Read)|};
            }
        }
        """);

    [Fact]
    public Task DiscardAssignment_IsReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Map(Buffer buffer)
            {
                _ = {|GST0002:buffer.Map(MapFlags.Read)|};
            }
        }
        """);

    [Fact]
    public Task LocalWithoutDispose_IsReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public int Map(Buffer buffer)
            {
                var map = {|GST0002:buffer.Map(MapFlags.Read)|};
                return map.Size;
            }
        }
        """);

    [Fact]
    public Task UsingDeclaration_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public int Map(Buffer buffer)
            {
                using var map = buffer.Map(MapFlags.Read);
                return map.Size;
            }
        }
        """);

    [Fact]
    public Task UsingStatement_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Map(Buffer buffer)
            {
                using (var map = buffer.Map(MapFlags.Read))
                {
                }
            }
        }
        """);

    [Fact]
    public Task LocalWithDispose_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Map(Buffer buffer)
            {
                var map = buffer.Map(MapFlags.Read);
                map.Dispose();
            }
        }
        """);

    [Fact]
    public Task PassedByRef_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Map(Buffer buffer)
            {
                var map = buffer.Map(MapFlags.Read);
                Consume(ref map);
            }

            private static void Consume(ref Buffer.MapScope scope)
            {
                scope.Dispose();
            }
        }
        """);

    [Fact]
    public Task TemporaryMemberAccess_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public int Map(Buffer buffer)
            {
                return buffer.Map(MapFlags.Read).Size;
            }
        }
        """);

    /// <summary>
    /// The rule matches a scope of any module, not only the one of
    /// <c>Gst.Buffer</c>: <c>Gst.Base.Adapter.MapScope</c> sits one namespace
    /// deeper and is reported the same way.
    /// </summary>
    [Fact]
    public Task ScopeInANestedGstNamespace_IsReported() => VerifyAsync("""
        using Gst.Base;

        public class Consumer
        {
            public void Map(Adapter adapter)
            {
                {|GST0002:adapter.Map(16)|};
            }
        }
        """);

    /// <summary>
    /// And it stays silent for the same scope when it is disposed, so the
    /// nested namespace does not turn the rule into a false positive either.
    /// </summary>
    [Fact]
    public Task ScopeInANestedGstNamespaceWithUsing_IsNotReported() => VerifyAsync("""
        using Gst.Base;

        public class Consumer
        {
            public nuint Map(Adapter adapter)
            {
                using var map = adapter.Map(16);
                return map.Size;
            }
        }
        """);

    [Fact]
    public Task ScopeOutsideGst_IsNotReported() => VerifyAsync("""
        namespace Other
        {
            public sealed class Holder
            {
                public MapScope Map() => default;

                public ref struct MapScope
                {
                    public void Dispose()
                    {
                    }
                }
            }

            public class Consumer
            {
                public void Map(Holder holder)
                {
                    holder.Map();
                }
            }
        }
        """);
}
