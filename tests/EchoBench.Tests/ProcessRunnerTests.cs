using System.Diagnostics;
using EchoBench.Orchestrator;
using Xunit;

namespace EchoBench.Tests;

/// <summary>
/// ProcessRunner — фундамент оркестратора: должен надёжно захватывать stdout/stderr (без
/// дедлока на пайпе), уважать таймаут и добивать зависший процесс. Тесты Linux-специфичны
/// (среда стенда — Linux, ARCHITECTURE §12).
/// </summary>
public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task Captures_stdout_and_stderr()
    {
        var spec = new ProcessSpec
        {
            FileName = "/bin/sh",
            Arguments = new[] { "-c", "echo out-line; echo err-line 1>&2" },
        };

        var result = await ProcessRunner.RunAsync(spec, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("out-line", result.StdOut);
        Assert.Contains("err-line", result.StdErr);
    }

    [Fact]
    public async Task Times_out_and_kills_hung_process()
    {
        var spec = new ProcessSpec
        {
            FileName = "/bin/sh",
            Arguments = new[] { "-c", "sleep 30" },
        };

        var sw = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync(spec, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.True(result.TimedOut);
        // Должны вернуться около таймаута, а не ждать полные 30 секунд.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"Заняло {sw.Elapsed.TotalSeconds:F1}s — таймаут не сработал.");
    }

    [Fact]
    public async Task Applies_environment_overrides()
    {
        var spec = new ProcessSpec
        {
            FileName = "/bin/sh",
            Arguments = new[] { "-c", "echo $ECHOBENCH_MARKER" },
            Environment = new Dictionary<string, string> { ["ECHOBENCH_MARKER"] = "gc-server-on" },
        };

        var result = await ProcessRunner.RunAsync(spec, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("gc-server-on", result.StdOut);
    }

    [Fact]
    public async Task Large_output_does_not_deadlock()
    {
        // Если бы вывод не дренировался асинхронно, переполнение пайпа повесило бы дочерний
        // процесс. Льём заведомо больше ёмкости пайпа.
        var spec = new ProcessSpec
        {
            FileName = "/bin/sh",
            Arguments = new[] { "-c", "for i in $(seq 1 20000); do echo line-$i; done" },
        };

        var result = await ProcessRunner.RunAsync(spec, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("line-20000", result.StdOut);
    }
}
