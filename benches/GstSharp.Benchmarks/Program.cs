using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace GstSharp.Benchmarks;

/// <summary>The entry point of the benchmark harness.</summary>
public static class Program
{
    /// <summary>Runs the benchmarks the command line selects.</summary>
    /// <param name="args">
    /// The BenchmarkDotNet command line, for example
    /// <c>--filter *Trampoline*</c> or <c>--list flat</c>.
    /// </param>
    /// <returns>Zero when every selected benchmark ran, one otherwise.</returns>
    public static int Main(string[] args)
    {
        // In process, because the default toolchain writes a generated child
        // project underneath this repository, where it would inherit
        // Directory.Build.props and its TreatWarningsAsErrors. Otherwise the
        // standard job: these benchmarks drive a native library whose variance
        // three iterations cannot see through, and a harness that exists to
        // catch a regression has to settle before it is worth reading. The
        // toolchain is the default job rather than a command line flag, so
        // `dotnet run` reproduces what the tables in benches/README.md were
        // made from.
        IConfig config = DefaultConfig.Instance
            .AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance).AsDefault());

        IEnumerable<Summary> summaries =
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

        bool failed = summaries.Any(
            summary => summary.HasCriticalValidationErrors
                || summary.Reports.Any(report => !report.Success));

        return failed ? 1 : 0;
    }
}
