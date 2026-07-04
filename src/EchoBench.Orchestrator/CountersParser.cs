using System.Globalization;

namespace EchoBench.Orchestrator;

/// <summary>Агрегированные серверные метрики из dotnet-counters (System.Runtime) для одной ячейки.</summary>
public sealed record CountersSummary
{
    /// <summary>Число сборок Gen0/1/2 за прогон (сумма по интервалам).</summary>
    public required long Gen0 { get; init; }
    public required long Gen1 { get; init; }
    public required long Gen2 { get; init; }

    /// <summary>Суммарная длительность пауз GC, мс. См. примечание в <see cref="CountersParser"/>.</summary>
    public required double GcPauseMs { get; init; }

    /// <summary>Пик working set, МиБ.</summary>
    public required double WsMb { get; init; }

    /// <summary>Средняя скорость аллокаций, МиБ/с.</summary>
    public required double AllocMbps { get; init; }

    /// <summary>Пик числа потоков пула (реконструируется из дельт). См. примечание.</summary>
    public required int Threads { get; init; }
}

/// <summary>
/// Разбор CSV от `dotnet-counters collect --counters System.Runtime --format csv`.
/// Формат проверен на dotnet-counters 9.x / .NET 10 (System.Runtime теперь Meter):
///   <c>Timestamp,Provider,Counter Name,Counter Type,Mean/Increment</c>
/// «Counter Type» = Metric (абсолютное значение-гейдж) либо Rate (приращение за интервал,
/// нормированное «/ 1 sec»; refresh-interval фиксируем в 1с → значение == приращение за интервал).
///
/// Маппинг в метрики §9:
///   gen0/1/2     ← dotnet.gc.collections[gc.heap.generation=genN] (Rate) — СУММА по интервалам.
///   gc_pause_ms  ← dotnet.gc.pause.time (Rate, секунды) — СУММА × 1000. Это РЕАЛЬНОЕ время пауз
///                  (не «% времени в GC»): в .NET 9+ System.Runtime эмитит счётчик пауз напрямую.
///   ws_mb        ← dotnet.process.memory.working_set (Metric, байты) — ПИК.
///   alloc_mbps   ← dotnet.gc.heap.total_allocated (Rate, байты/с) — СРЕДНЕЕ.
///   threads      ← dotnet.thread_pool.thread.count (Rate=ДЕЛЬТА) — пик бегущей суммы дельт.
///                  Это потоки ПУЛА; у ThreadPerConnection выделенные Thread сюда не попадают
///                  (ограничение System.Runtime, не бага парсера).
/// </summary>
public static class CountersParser
{
    public const string ExpectedHeader = "Timestamp,Provider,Counter Name,Counter Type,Mean/Increment";

    public static CountersSummary ParseFile(string path) => Parse(File.ReadLines(path));

    public static CountersSummary Parse(IEnumerable<string> lines)
    {
        long gen0 = 0, gen1 = 0, gen2 = 0;
        double pauseSeconds = 0;
        double wsMaxBytes = 0;
        double allocRateSum = 0;
        int allocRateCount = 0;
        double threadsRunning = 0;
        double threadsPeak = 0;

        bool headerSeen = false;

        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            if (!headerSeen)
            {
                if (raw.StartsWith("Timestamp", StringComparison.Ordinal))
                {
                    if (raw.Trim() != ExpectedHeader)
                        throw new FormatException(
                            $"Неожиданный заголовок counters.csv.\nОжидался: {ExpectedHeader}\nПолучен:  {raw.Trim()}");
                    headerSeen = true;
                    continue;
                }
                // строки до заголовка игнорируем
                continue;
            }

            if (!TryParseRow(raw, out var name, out var generation, out var value))
                continue;

            switch (name)
            {
                case "dotnet.gc.collections":
                    if (generation == "gen0") gen0 += (long)Math.Round(value);
                    else if (generation == "gen1") gen1 += (long)Math.Round(value);
                    else if (generation == "gen2") gen2 += (long)Math.Round(value);
                    break;

                case "dotnet.gc.pause.time":
                    pauseSeconds += value;
                    break;

                case "dotnet.process.memory.working_set":
                    if (value > wsMaxBytes) wsMaxBytes = value;
                    break;

                case "dotnet.gc.heap.total_allocated":
                    allocRateSum += value;
                    allocRateCount++;
                    break;

                case "dotnet.thread_pool.thread.count":
                    threadsRunning += value; // значение — дельта за интервал
                    if (threadsRunning > threadsPeak) threadsPeak = threadsRunning;
                    break;
            }
        }

        if (!headerSeen)
            throw new FormatException("В counters.csv не найден заголовок — файл пуст или формат сменился.");

        const double mib = 1024.0 * 1024.0;
        return new CountersSummary
        {
            Gen0 = gen0,
            Gen1 = gen1,
            Gen2 = gen2,
            GcPauseMs = pauseSeconds * 1000.0,
            WsMb = wsMaxBytes / mib,
            AllocMbps = allocRateCount > 0 ? allocRateSum / allocRateCount / mib : 0,
            Threads = (int)Math.Round(threadsPeak),
        };
    }

    /// <summary>
    /// Разбор строки данных. Имя счётчика содержит пробелы/скобки, но НЕ запятые, поэтому
    /// парсим с краёв: value — последнее поле, type — предпоследнее, имя — всё между провайдером
    /// и типом (на случай будущих запятых в имени склеиваем обратно).
    /// </summary>
    private static bool TryParseRow(string line, out string name, out string generation, out double value)
    {
        name = string.Empty;
        generation = string.Empty;
        value = 0;

        var parts = line.Split(',');
        if (parts.Length < 5)
            return false;

        if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        // parts[0]=Timestamp, parts[1]=Provider, parts[^2]=Type, середина = Counter Name
        var nameField = string.Join(',', parts[2..^2]).Trim();

        // Имя до " (" (отбрасываем единицы вида " (By)" / " (s / 1 sec)").
        int unitAt = nameField.IndexOf(" (", StringComparison.Ordinal);
        name = unitAt >= 0 ? nameField[..unitAt] : nameField;

        // Тег измерения вида [gc.heap.generation=gen0].
        int tagAt = nameField.IndexOf('[', StringComparison.Ordinal);
        if (tagAt >= 0)
        {
            var tag = nameField[tagAt..];
            if (tag.Contains("gen0", StringComparison.Ordinal)) generation = "gen0";
            else if (tag.Contains("gen1", StringComparison.Ordinal)) generation = "gen1";
            else if (tag.Contains("gen2", StringComparison.Ordinal)) generation = "gen2";
        }

        return true;
    }
}
