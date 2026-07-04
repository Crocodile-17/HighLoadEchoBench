using EchoBench.Abstractions;

namespace EchoBench.Orchestrator;

/// <summary>
/// Одна ячейка матрицы эксперимента: модель × режим GC × пулинг + параметры нагрузки.
/// Каждая ячейка — отдельный процесс Host (см. ARCHITECTURE §4) и отдельная строка
/// в results.csv.
/// </summary>
public sealed record RunSpec
{
    public required ServerModel Model { get; init; }

    /// <summary>DOTNET_gcServer: Server GC (куча+поток на ядро) против Workstation.</summary>
    public required bool GcServer { get; init; }

    /// <summary>DOTNET_gcConcurrent: Background/concurrent GC против blocking.</summary>
    public required bool GcConcurrent { get; init; }

    /// <summary>ArrayPool против new[] — прокидывается в Host как --pooled.</summary>
    public required bool Pooled { get; init; }

    public required int Conns { get; init; }
    public required int DurationSeconds { get; init; }
    public required int WarmupSeconds { get; init; }
    public required int PayloadBytes { get; init; }

    /// <summary>Уникальный порт ячейки — инкрементный, чтобы прогоны не сталкивались на TIME_WAIT.</summary>
    public required int Port { get; init; }

    /// <summary>Короткая метка для логов и имён файлов counters.csv.</summary>
    public string Label =>
        $"{Model}_gcS{(GcServer ? 1 : 0)}_gcC{(GcConcurrent ? 1 : 0)}_pool{(Pooled ? 1 : 0)}_p{Port}";
}
