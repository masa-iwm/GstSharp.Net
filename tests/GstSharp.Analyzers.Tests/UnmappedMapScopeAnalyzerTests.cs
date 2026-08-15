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
