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
        // catch a regression has to settle before it is worth reading. So
        // `dotnet run` with no job on the command line reproduces what the
        // tables in benches/README.md were made from.
        //
        // A job the config marks as its default is the one that runs, whatever
        // `--job` asks for; so the harness adds its in-process default only
        // when the command line names no job. Adding it unconditionally
        // swallowed `--job short`. When a job is named, the command line owns
        // it and has to carry `--inProcess` to keep the toolchain.
        IConfig config = DefaultConfig.Instance;

        if (!args.Any(IsJobArgument))
        {
            config = config.AddJob(
                Job.Default.WithToolchain(InProcessEmitToolchain.Instance).AsDefault());
        }

        IEnumerable<Summary> summaries =
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

        bool failed = summaries.Any(
            summary => summary.HasCriticalValidationErrors
                || summary.Reports.Any(report => !report.Success));

        return failed ? 1 : 0;
    }

    /// <summary>Tells whether an argument selects a BenchmarkDotNet job.</summary>
    /// <param name="argument">One command line argument.</param>
    /// <returns>True for <c>--job</c>, <c>--job=x</c> and <c>-j</c>.</returns>
    private static bool IsJobArgument(string argument)
        => argument.Equals("--job", StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith("--job=", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("-j", StringComparison.OrdinalIgnoreCase);
}
