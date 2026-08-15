using Gst.Analyzers;
using Xunit;

namespace GstSharp.Analyzers.Tests;

/// <summary>Tests for GST0001.</summary>
public sealed class UndisposedWrapperAnalyzerTests
{
    private static Task VerifyAsync(string source) =>
        AnalyzerVerifier<UndisposedWrapperAnalyzer>.VerifyAsync(source);

    [Fact]
    public Task UndisposedLocal_IsReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                var {|GST0001:sample|} = sink.PullSample();
            }
        }
        """);

    [Fact]
    public Task ObjectCreation_IsReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Create()
            {
                var {|GST0001:caps|} = new Caps();
            }
        }
        """);

    [Fact]
    public Task BoxedDerivedLocal_IsReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Create()
            {
                var {|GST0001:structure|} = new Structure();
            }
        }
        """);

    [Fact]
    public Task NullCheckedButUndisposedLocal_IsReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                var {|GST0001:sample|} = sink.TryPullSample(100);
                if (sample == null)
                {
                    return;
                }

                Use(sample.Caps);
            }

            private static void Use(Caps caps)
            {
            }
        }
        """);

    [Fact]
    public Task UsingDeclaration_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                using var sample = sink.PullSample();
            }
        }
        """);

    [Fact]
    public Task UsingStatement_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                using (var sample = sink.PullSample())
                {
                }
            }
        }
        """);

    [Fact]
    public Task UsingStatementOverExistingLocal_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                using (sample)
                {
                }
            }
        }
        """);

    [Fact]
    public Task DisposeCall_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                sample.Dispose();
            }
        }
        """);

    [Fact]
    public Task NullConditionalDispose_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                var sample = sink.TryPullSample(100);
                sample?.Dispose();
            }
        }
        """);

    [Fact]
    public Task Returned_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public Sample Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                return sample;
            }
        }
        """);

    [Fact]
    public Task YieldReturned_IsNotReported() => VerifyAsync("""
        using System.Collections.Generic;
        using Gst;

        public class Consumer
        {
            public IEnumerable<Sample> Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                yield return sample;
            }
        }
        """);

    [Fact]
    public Task AssignedToField_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            private Sample _sample;

            public void Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                _sample = sample;
            }
        }
        """);

    [Fact]
    public Task AssignedToProperty_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public Sample Current { get; set; }

            public void Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                Current = sample;
            }
        }
        """);

    [Fact]
    public Task AssignedToOutParameter_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink, out Sample result)
            {
                var sample = sink.PullSample();
                result = sample;
            }
        }
        """);

    [Fact]
    public Task PassedAsArgument_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                Keep(sample);
            }

            private static void Keep(Sample sample)
            {
            }
        }
        """);

    [Fact]
    public Task CapturedByLambda_IsNotReported() => VerifyAsync("""
        using System;
        using Gst;

        public class Consumer
        {
            public Action Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                return () => sample.Dispose();
            }
        }
        """);

    [Fact]
    public Task DeclaredAndDisposedInsideLambda_IsNotReported() => VerifyAsync("""
        using System;
        using Gst;

        public class Consumer
        {
            public Action Pull(AppSink sink)
            {
                return () =>
                {
                    var sample = sink.PullSample();
                    sample.Dispose();
                };
            }
        }
        """);

    [Fact]
    public Task UndisposedInsideLambda_IsReported() => VerifyAsync("""
        using System;
        using Gst;

        public class Consumer
        {
            public Action Pull(AppSink sink)
            {
                return () =>
                {
                    var {|GST0001:sample|} = sink.PullSample();
                };
            }
        }
        """);

    [Fact]
    public Task DisposedOnOneBranchOnly_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink, bool flush)
            {
                var sample = sink.PullSample();
                if (flush)
                {
                    sample.Dispose();
                }
            }
        }
        """);

    [Fact]
    public Task Reassigned_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Pull(AppSink sink)
            {
                var sample = sink.PullSample();
                sample = sink.PullSample();
            }
        }
        """);

    [Fact]
    public Task PropertyInitializedLocal_IsNotReported() => VerifyAsync("""
        using Gst;

        public class Consumer
        {
            public void Read(Sample sample)
            {
                var buffer = sample.Buffer;
            }
        }
        """);

    [Fact]
    public Task NonWrapperLocal_IsNotReported() => VerifyAsync("""
        public class Consumer
        {
            public void Create()
            {
                var value = new object();
            }
        }
        """);
}
