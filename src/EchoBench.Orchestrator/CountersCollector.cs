using System.Globalization;

namespace EchoBench.Orchestrator;

/// <summary>
/// Снимает серверные метрики ВНЕ процесса Host через dotnet-counters (эффект наблюдателя —
/// ARCHITECTURE §4). Стартует сразу после READY и работает фиксированную длительность
/// (--duration), сам останавливаясь и сбрасывая CSV — это надёжнее, чем ловить SIGINT
/// у внешнего инструмента.
/// </summary>
public sealed class CountersCollector
{
    private readonly string _countersExe;

    public CountersCollector(string countersExe) => _countersExe = countersExe;

    /// <summary>dotnet-counters в PATH или в global-tools (~/.dotnet/tools).</summary>
    public static string ResolveExe()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (home is not null)
        {
            var toolPath = Path.Combine(home, ".dotnet", "tools", "dotnet-counters");
            if (File.Exists(toolPath))
                return toolPath;
        }
        return "dotnet-counters"; // расчёт на PATH
    }

    /// <summary>
    /// Запускает сбор. dotnet-counters добавляет «.csv» к <paramref name="outputBase"/>,
    /// поэтому фактический файл — <c>outputBase + ".csv"</c> (его и парсит <see cref="CountersParser"/>).
    /// </summary>
    public RunningProcess Start(int pid, string outputBase, int durationSeconds)
    {
        var duration = TimeSpan.FromSeconds(durationSeconds);
        var durationArg = duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

        return ProcessRunner.Start(new ProcessSpec
        {
            FileName = _countersExe,
            Arguments = new[]
            {
                "collect",
                "-p", pid.ToString(CultureInfo.InvariantCulture),
                "--counters", "System.Runtime",
                "--format", "csv",
                "--refresh-interval", "1", // 1с → Rate-значение == приращение за интервал
                "--duration", durationArg,
                "-o", outputBase,
            },
        });
    }

    public static string CsvPath(string outputBase) => outputBase + ".csv";
}
