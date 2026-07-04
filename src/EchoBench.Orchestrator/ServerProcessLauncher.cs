using System.Net.Sockets;

namespace EchoBench.Orchestrator;

/// <summary>
/// Поднимает Host как отдельный процесс с env режима GC, ждёт сигнал READY и проверяет
/// порт TCP-пробой. Запуск идёт через `dotnet Host.dll`, чтобы PID процесса совпадал с PID
/// приложения (иначе dotnet-counters прицепится не к тому процессу — см. ARCHITECTURE §4).
/// </summary>
public sealed class ServerProcessLauncher
{
    private readonly string _dotnet;
    private readonly string _hostDll;
    private readonly IReadOnlyList<string> _commandPrefix;

    /// <param name="commandPrefix">
    /// Префикс CPU-пиннинга, напр. ["taskset","-c","0-3"]. Сервер должен жить на ядрах,
    /// не пересекающихся с нагрузчиком (§10), иначе клиент ворует у него CPU. Пусто — без пиннинга.
    /// </param>
    public ServerProcessLauncher(string dotnetPath, string hostDllPath, IReadOnlyList<string>? commandPrefix = null)
    {
        _dotnet = dotnetPath;
        _hostDll = hostDllPath;
        _commandPrefix = commandPrefix ?? Array.Empty<string>();
    }

    public async Task<ServerProcessHandle> LaunchAsync(RunSpec spec, TimeSpan readyTimeout, CancellationToken ct)
    {
        var env = new Dictionary<string, string>
        {
            // Режим GC фиксируется на старте процесса — только так, не через csproj.
            ["DOTNET_gcServer"] = spec.GcServer ? "1" : "0",
            ["DOTNET_gcConcurrent"] = spec.GcConcurrent ? "1" : "0",
        };

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var proc = ProcessRunner.Start(
            new ProcessSpec
            {
                FileName = _dotnet,
                Arguments = new[]
                {
                    _hostDll,
                    "--model", spec.Model.ToString(),
                    "--pooled", spec.Pooled ? "true" : "false",
                    "--port", spec.Port.ToString(),
                },
                Environment = env,
                CommandPrefix = _commandPrefix,
            },
            onStdOutLine: line =>
            {
                if (line.StartsWith("READY", StringComparison.Ordinal))
                    ready.TrySetResult();
            });

        try
        {
            // READY против преждевременного выхода против таймаута.
            var exited = proc.WaitForExitAsync(ct);
            var winner = await Task.WhenAny(ready.Task, exited, Task.Delay(readyTimeout, ct)).ConfigureAwait(false);

            if (winner == exited)
                throw new InvalidOperationException(
                    $"Host '{spec.Label}' завершился до READY (код {SafeExitCode(proc)}).\nstderr:\n{proc.StdErr}");

            if (!ready.Task.IsCompleted)
                throw new TimeoutException(
                    $"Host '{spec.Label}' не сигналил READY за {readyTimeout.TotalSeconds:F0}s.\nstdout:\n{proc.StdOut}\nstderr:\n{proc.StdErr}");

            // Подтверждаем, что порт реально принимает соединения, прежде чем пускать нагрузку.
            await ProbePortAsync(spec.Port, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

            return new ServerProcessHandle(proc, spec);
        }
        catch
        {
            proc.Kill();
            proc.Dispose();
            throw;
        }
    }

    private static int SafeExitCode(RunningProcess p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }

    private static async Task ProbePortAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", port, ct).ConfigureAwait(false);
                return;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Ручка живого Host: PID для counters + управляемый teardown с дренажём порта.</summary>
public sealed class ServerProcessHandle : IDisposable
{
    private readonly RunningProcess _proc;

    internal ServerProcessHandle(RunningProcess proc, RunSpec spec)
    {
        _proc = proc;
        Spec = spec;
    }

    public RunSpec Spec { get; }
    public int Pid => _proc.Id;
    public string StdOut => _proc.StdOut;
    public string StdErr => _proc.StdErr;

    /// <summary>Мягко гасит Host, затем выдерживает паузу на дренаж порта/устаканивание GC.</summary>
    public async Task StopAsync(TimeSpan graceful, TimeSpan drainPause, CancellationToken ct = default)
    {
        await _proc.StopAsync(graceful, ct).ConfigureAwait(false);
        if (drainPause > TimeSpan.Zero)
            await Task.Delay(drainPause, ct).ConfigureAwait(false);
    }

    public void Dispose() => _proc.Dispose();
}
