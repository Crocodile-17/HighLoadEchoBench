using System.Globalization;

namespace EchoBench.Orchestrator;

/// <summary>Клиентские метрики прогона: пропускная способность и перцентили латентности.</summary>
public sealed record LoadOutcome
{
    public required double Rps { get; init; }
    public required double P50 { get; init; }
    public required double P95 { get; init; }
    public required double P99 { get; init; }
    public required double P999 { get; init; }
    public required long Requests { get; init; }
    public required double DurationSeconds { get; init; }
}

/// <summary>
/// Запускает LoadClient как внешний процесс (без ссылки на его сборку — STRUCTURE.md) и
/// разбирает его машинный вывод. LoadClient печатает две последние строки: CSV-заголовок и
/// строку данных; разбор идёт ПО ИМЕНАМ колонок, а не по позициям, — устойчив к перестановке.
/// </summary>
public sealed class LoadClientRunner
{
    // Должен совпадать с LoadResult.CsvHeader в EchoBench.LoadClient (проверяется при разборе).
    public const string ExpectedHeader =
        "conns,payload_bytes,target_rps,requests,dur_s,rps,p50_us,p95_us,p99_us,p999_us";

    private readonly string _dotnet;
    private readonly string _loadDll;
    private readonly IReadOnlyList<string> _commandPrefix;

    /// <param name="commandPrefix">
    /// Префикс CPU-пиннинга нагрузчика, напр. ["taskset","-c","4-7"]. Ядра не должны пересекаться
    /// с серверными (§10). Пусто — без пиннинга.
    /// </param>
    public LoadClientRunner(string dotnetPath, string loadDllPath, IReadOnlyList<string>? commandPrefix = null)
    {
        _dotnet = dotnetPath;
        _loadDll = loadDllPath;
        _commandPrefix = commandPrefix ?? Array.Empty<string>();
    }

    public async Task<LoadOutcome> RunAsync(RunSpec spec, CancellationToken ct)
    {
        // Таймаут: прогрев + замер + щедрый запас на старт процесса и слияние гистограмм.
        var timeout = TimeSpan.FromSeconds(spec.WarmupSeconds + spec.DurationSeconds + 30);

        var result = await ProcessRunner.RunAsync(
            new ProcessSpec
            {
                FileName = _dotnet,
                Arguments = new[]
                {
                    _loadDll,
                    "--host", "127.0.0.1",
                    "--port", spec.Port.ToString(CultureInfo.InvariantCulture),
                    "--conns", spec.Conns.ToString(CultureInfo.InvariantCulture),
                    "--duration", spec.DurationSeconds.ToString(CultureInfo.InvariantCulture),
                    "--warmup", spec.WarmupSeconds.ToString(CultureInfo.InvariantCulture),
                    "--payload", spec.PayloadBytes.ToString(CultureInfo.InvariantCulture),
                },
                CommandPrefix = _commandPrefix,
            },
            timeout,
            ct).ConfigureAwait(false);

        if (result.TimedOut || result.ExitCode != 0)
            throw new InvalidOperationException(
                $"LoadClient '{spec.Label}' завершился с кодом {result.ExitCode} (timeout={result.TimedOut}).\nstderr:\n{result.StdErr}\nstdout:\n{result.StdOut}");

        return ParseOutput(result.StdOut);
    }

    /// <summary>Находит CSV-заголовок в выводе и читает следующую строку данных по именам колонок.</summary>
    public static LoadOutcome ParseOutput(string stdout)
    {
        var lines = stdout.Split('\n', StringSplitOptions.TrimEntries);

        int headerIdx = Array.FindIndex(lines, l => l == ExpectedHeader);
        if (headerIdx < 0 || headerIdx + 1 >= lines.Length)
            throw new FormatException($"В выводе LoadClient не найден CSV-блок результата.\nВывод:\n{stdout}");

        // Первая непустая строка после заголовка — данные.
        string? dataLine = null;
        for (int i = headerIdx + 1; i < lines.Length; i++)
        {
            if (lines[i].Length > 0) { dataLine = lines[i]; break; }
        }
        if (dataLine is null)
            throw new FormatException($"После заголовка LoadClient нет строки данных.\nВывод:\n{stdout}");

        var names = ExpectedHeader.Split(',');
        var values = dataLine.Split(',');
        if (values.Length != names.Length)
            throw new FormatException($"Число колонок данных ({values.Length}) ≠ заголовку ({names.Length}).\nСтрока: {dataLine}");

        var map = new Dictionary<string, string>(names.Length, StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++)
            map[names[i]] = values[i];

        return new LoadOutcome
        {
            Rps = Num(map, "rps"),
            P50 = Num(map, "p50_us"),
            P95 = Num(map, "p95_us"),
            P99 = Num(map, "p99_us"),
            P999 = Num(map, "p999_us"),
            Requests = (long)Num(map, "requests"),
            DurationSeconds = Num(map, "dur_s"),
        };
    }

    private static double Num(IReadOnlyDictionary<string, string> map, string key) =>
        double.Parse(map[key], NumberStyles.Float, CultureInfo.InvariantCulture);
}
