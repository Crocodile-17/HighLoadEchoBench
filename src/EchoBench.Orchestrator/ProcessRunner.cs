using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EchoBench.Orchestrator;

/// <summary>Что запускать: команда, аргументы, env-оверрайды, рабочий каталог, опц. префикс (taskset).</summary>
public sealed record ProcessSpec
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>Env поверх унаследованного окружения (например DOTNET_gcServer).</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>Командный префикс для пиннинга CPU, напр. ["taskset","-c","2-5"]. Пусто — без него.</summary>
    public IReadOnlyList<string> CommandPrefix { get; init; } = Array.Empty<string>();
}

/// <summary>Итог процесса, отработавшего до конца.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

/// <summary>
/// Хелпер запуска внешних процессов. Захват stdout/stderr идёт асинхронно (через
/// событийные обработчики + дренаж пайпа), чтобы переполнение пайпа не вешало дочерний
/// процесс. Таймаут → tree-kill. На Linux умеет мягкий SIGINT для graceful-выхода.
/// </summary>
public static class ProcessRunner
{
    /// <summary>Стартует процесс и сразу возвращает управление; вывод копится и стримится в колбэки.</summary>
    public static RunningProcess Start(
        ProcessSpec spec,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = spec.WorkingDirectory ?? Directory.GetCurrentDirectory(),
        };

        // Префикс (taskset …) становится самой командой, реальная команда уезжает в аргументы.
        if (spec.CommandPrefix.Count > 0)
        {
            psi.FileName = spec.CommandPrefix[0];
            for (int i = 1; i < spec.CommandPrefix.Count; i++)
                psi.ArgumentList.Add(spec.CommandPrefix[i]);
            psi.ArgumentList.Add(spec.FileName);
        }
        else
        {
            psi.FileName = spec.FileName;
        }

        foreach (var arg in spec.Arguments)
            psi.ArgumentList.Add(arg);

        if (spec.Environment is not null)
            foreach (var (key, value) in spec.Environment)
                psi.Environment[key] = value;

        return new RunningProcess(psi, onStdOutLine, onStdErrLine);
    }

    /// <summary>Запускает процесс и ждёт завершения с таймаутом; по таймауту — tree-kill.</summary>
    public static async Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var proc = Start(spec);
        bool timedOut = false;
        try
        {
            await proc.WaitForExitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            timedOut = true;
            proc.Kill();
            // Дать вывести добитый процесс и дренировать стримы.
            try { await proc.WaitForExitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
            catch (TimeoutException) { /* уже добили — выходим с тем, что есть */ }
        }

        return new ProcessResult(
            timedOut ? -1 : proc.ExitCode,
            proc.StdOut,
            proc.StdErr,
            timedOut);
    }
}

/// <summary>Живой процесс: PID, потоковый вывод, ожидание выхода, мягкая и жёсткая остановка.</summary>
public sealed class RunningProcess : IDisposable
{
    private const int SIGINT = 2;

    private readonly Process _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly object _gate = new();
    private readonly TaskCompletionSource _stdoutClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stderrClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal RunningProcess(ProcessStartInfo psi, Action<string>? onStdOutLine, Action<string>? onStdErrLine)
    {
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { _stdoutClosed.TrySetResult(); return; }
            lock (_gate) _stdout.AppendLine(e.Data);
            onStdOutLine?.Invoke(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { _stderrClosed.TrySetResult(); return; }
            lock (_gate) _stderr.AppendLine(e.Data);
            onStdErrLine?.Invoke(e.Data);
        };

        if (!_process.Start())
            throw new InvalidOperationException($"Не удалось запустить процесс '{psi.FileName}'.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;

    public string StdOut { get { lock (_gate) return _stdout.ToString(); } }
    public string StdErr { get { lock (_gate) return _stderr.ToString(); } }

    /// <summary>Ждёт выхода процесса И полного слива обоих стримов (без потери хвоста вывода).</summary>
    public async Task WaitForExitAsync(CancellationToken ct = default)
    {
        await _process.WaitForExitAsync(ct).ConfigureAwait(false);
        await Task.WhenAll(_stdoutClosed.Task, _stderrClosed.Task).ConfigureAwait(false);
    }

    /// <summary>То же с таймаутом; по истечении бросает <see cref="TimeoutException"/> (процесс не трогает).</summary>
    public async Task WaitForExitAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Процесс {Id} не завершился за {timeout.TotalSeconds:F0}s.");
        }
    }

    /// <summary>Жёсткая остановка всего дерева процессов (SIGKILL).</summary>
    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* уже завершился */ }
    }

    /// <summary>
    /// Мягкая остановка: SIGINT (на Linux — как Ctrl+C, чтобы Host штатно отменился и
    /// дренировал порт), ожидание graceful-выхода; если не успел — tree-kill.
    /// Возвращает true, если процесс вышел сам по сигналу.
    /// </summary>
    public async Task<bool> StopAsync(TimeSpan graceful, CancellationToken ct = default)
    {
        if (_process.HasExited)
            return true;

        bool signalled = false;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            signalled = kill(_process.Id, SIGINT) == 0;

        if (signalled)
        {
            try
            {
                await WaitForExitAsync(graceful, ct).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException) { /* не успел — добиваем ниже */ }
        }

        Kill();
        try { await WaitForExitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
        catch (TimeoutException) { /* ничего больше сделать нельзя */ }
        return false;
    }

    public void Dispose()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch { /* подавляем при освобождении */ }
        _process.Dispose();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);
}
