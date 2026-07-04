using EchoBench.Abstractions;

namespace EchoBench.Orchestrator;

/// <summary>Оси нагрузки фиксированы на весь прогон матрицы; меняются только эксперим. оси.</summary>
public sealed record MatrixOptions
{
    public int BasePort { get; init; } = 18000;

    /// <summary>
    /// Свип по числу соединений — ось флагманского графика «working set от conns» (§11).
    /// Должен содержать ≥2 значения, иначе флагман вырождается в точку. Лог-шкала X на графике
    /// предполагает геометрический шаг.
    /// </summary>
    public IReadOnlyList<int> ConnsSweep { get; init; } = new[] { 64, 128, 256, 512, 1024 };

    public int DurationSeconds { get; init; } = 4;
    public int WarmupSeconds { get; init; } = 1;
    public int PayloadBytes { get; init; } = 128;
}

/// <summary>
/// Полная декартова матрица: {модель×2} × {gcServer×2} × {gcConcurrent×2} × {pooled×2} × {conns×N}.
/// При N значениях свипа — 16·N ячеек. conns — самая внутренняя ось, поэтому подряд идущие
/// ячейки образуют готовую серию «один конфиг × все conns» (удобно для частичного прогона
/// и для флагманского графика). Каждой ячейке — инкрементный порт от <see cref="MatrixOptions.BasePort"/>.
/// </summary>
public static class ExperimentMatrix
{
    public static IReadOnlyList<RunSpec> Build(MatrixOptions options)
    {
        if (options.ConnsSweep.Count == 0)
            throw new ArgumentException("ConnsSweep пуст — нужно ≥1 значение conns.", nameof(options));

        var specs = new List<RunSpec>(16 * options.ConnsSweep.Count);
        int port = options.BasePort;

        foreach (var model in new[] { ServerModel.ThreadPerConnection, ServerModel.Async })
            foreach (var gcServer in new[] { false, true })
                foreach (var gcConcurrent in new[] { false, true })
                    foreach (var pooled in new[] { false, true })
                        foreach (var conns in options.ConnsSweep)
                        {
                            specs.Add(new RunSpec
                            {
                                Model = model,
                                GcServer = gcServer,
                                GcConcurrent = gcConcurrent,
                                Pooled = pooled,
                                Conns = conns,
                                DurationSeconds = options.DurationSeconds,
                                WarmupSeconds = options.WarmupSeconds,
                                PayloadBytes = options.PayloadBytes,
                                Port = port++,
                            });
                        }

        return specs;
    }
}
