using HdrHistogram;

namespace EchoBench.LoadClient;

/// <summary>
/// Обёртка над HdrHistogram: запись round-trip-латентностей (в микросекундах) и расчёт
/// перцентилей. Здесь же живёт коррекция coordinated omission — самое ошибаемое место во
/// всём измерении, поэтому оно изолировано в одном файле и покрыто юнит-тестом.
///
/// <para>
/// <b>Closed-loop против целевого RPS.</b> Коррекция CO осмысленна только тогда, когда у
/// нагрузки есть <i>задуманный</i> темп запросов — ожидаемый интервал между ними. Тогда
/// длинный ответ, застопоривший цикл соединения, означает, что запросы, которые
/// <i>должны</i> были уйти за время затыка, оказались «проглочены» (coordinated omission);
/// <see cref="HistogramBase.RecordValueWithExpectedInterval"/> до-вписывает их синтетически,
/// и хвост (p99/p99.9) перестаёт врать в меньшую сторону.
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Целевой RPS задан</b> → <c>expectedIntervalMicros</c> = время между запросами
///   <i>одного</i> соединения; пишем через <c>RecordValueWithExpectedInterval</c> →
///   хвост честный.
///   </description></item>
///   <item><description>
///   <b>Целевого RPS нет</b> (чистый closed-loop «на максимум») → задуманного темпа не
///   существует, до-вписывать не от чего: пишем сырое значение. Хвост по построению
///   оптимистичен — это и есть тот самый эффект coordinated omission (ARCHITECTURE.md §10).
///   </description></item>
/// </list>
/// </summary>
public sealed class LatencyRecorder
{
    // 1 мкс … 60 с при 3 значащих цифрах: ~0.1% разрешение, сотни КБ на гистограмму.
    private const long LowestMicros = 1;
    private const long HighestMicros = 60_000_000;
    private const int SignificantDigits = 3;

    private readonly LongHistogram _histogram;
    private readonly long _expectedIntervalMicros;

    /// <param name="expectedIntervalMicros">
    /// Ожидаемый интервал между запросами одного соединения, мкс. 0 — коррекция выключена
    /// (чистый closed-loop).
    /// </param>
    public LatencyRecorder(long expectedIntervalMicros = 0)
    {
        _histogram = new LongHistogram(LowestMicros, HighestMicros, SignificantDigits);
        _expectedIntervalMicros = expectedIntervalMicros > 0 ? expectedIntervalMicros : 0;
    }

    public long Count => _histogram.TotalCount;

    /// <summary>Записать одну round-trip-латентность в микросекундах.</summary>
    public void Record(long valueMicros)
    {
        // Клампим к границам гистограммы: иначе HdrHistogram бросит на выходе за диапазон.
        if (valueMicros < LowestMicros) valueMicros = LowestMicros;
        else if (valueMicros > HighestMicros) valueMicros = HighestMicros;

        if (_expectedIntervalMicros > 0)
            _histogram.RecordValueWithExpectedInterval(valueMicros, _expectedIntervalMicros);
        else
            _histogram.RecordValue(valueMicros);
    }

    /// <summary>
    /// Слить рекордеры воркеров в один. Значения уже скорректированы при записи, поэтому
    /// слияние — это просто суммирование счётчиков бакетов (<see cref="HistogramBase.Add"/>).
    /// </summary>
    public static LatencyRecorder Merge(IEnumerable<LatencyRecorder> recorders)
    {
        var merged = new LatencyRecorder();
        foreach (var r in recorders)
            merged._histogram.Add(r._histogram);
        return merged;
    }

    /// <summary>Перцентиль в микросекундах (percentile=0.99 → p99).</summary>
    public double PercentileMicros(double percentile) =>
        _histogram.GetValueAtPercentile(percentile * 100.0);
}
