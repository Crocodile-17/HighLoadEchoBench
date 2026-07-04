using EchoBench.Orchestrator;
using Xunit;

namespace EchoBench.Tests;

/// <summary>
/// CountersParser агрегирует CSV от dotnet-counters в метрики §9. Сэмпл повторяет реальный
/// формат dotnet-counters 9.x / .NET 10 (проверен вживую): заголовок
/// «Timestamp,Provider,Counter Name,Counter Type,Mean/Increment», Rate-счётчики «/ 1 sec».
/// </summary>
public sealed class CountersParserTests
{
    private const string Sample =
        "Timestamp,Provider,Counter Name,Counter Type,Mean/Increment\n" +
        // working_set (Metric, байты) — берётся ПИК
        "06/24/2026 16:13:52,System.Runtime,dotnet.process.memory.working_set (By),Metric,46000000\n" +
        "06/24/2026 16:13:53,System.Runtime,dotnet.process.memory.working_set (By),Metric,49000000\n" +
        "06/24/2026 16:13:54,System.Runtime,dotnet.process.memory.working_set (By),Metric,48000000\n" +
        // gc.collections gen0/1/2 (Rate) — СУММА по интервалам
        "06/24/2026 16:13:52,System.Runtime,dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen0],Rate,3\n" +
        "06/24/2026 16:13:53,System.Runtime,dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen0],Rate,2\n" +
        "06/24/2026 16:13:52,System.Runtime,dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen1],Rate,1\n" +
        "06/24/2026 16:13:53,System.Runtime,dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen1],Rate,0\n" +
        "06/24/2026 16:13:52,System.Runtime,dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen2],Rate,0\n" +
        "06/24/2026 16:13:53,System.Runtime,dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen2],Rate,1\n" +
        // gc.pause.time (Rate, секунды) — СУММА × 1000 = мс
        "06/24/2026 16:13:52,System.Runtime,dotnet.gc.pause.time (s / 1 sec),Rate,0.002\n" +
        "06/24/2026 16:13:53,System.Runtime,dotnet.gc.pause.time (s / 1 sec),Rate,0.003\n" +
        // total_allocated (Rate, байты/с) — СРЕДНЕЕ: (1 MiB/s + 3 MiB/s)/2 = 2 MiB/s
        "06/24/2026 16:13:52,System.Runtime,dotnet.gc.heap.total_allocated (By / 1 sec),Rate,1048576\n" +
        "06/24/2026 16:13:53,System.Runtime,dotnet.gc.heap.total_allocated (By / 1 sec),Rate,3145728\n" +
        // thread_pool.thread.count (Rate = ДЕЛЬТА) — пик бегущей суммы: 6→8→5, пик=8
        "06/24/2026 16:13:52,System.Runtime,dotnet.thread_pool.thread.count ({thread} / 1 sec),Rate,6\n" +
        "06/24/2026 16:13:53,System.Runtime,dotnet.thread_pool.thread.count ({thread} / 1 sec),Rate,2\n" +
        "06/24/2026 16:13:54,System.Runtime,dotnet.thread_pool.thread.count ({thread} / 1 sec),Rate,-3\n" +
        // посторонний счётчик — должен игнорироваться
        "06/24/2026 16:13:52,System.Runtime,dotnet.assembly.count ({assembly}),Metric,19\n";

    [Fact]
    public void Aggregates_runtime_counters_per_section_9()
    {
        var s = CountersParser.Parse(Sample.Split('\n'));

        Assert.Equal(5, s.Gen0);   // 3 + 2
        Assert.Equal(1, s.Gen1);   // 1 + 0
        Assert.Equal(1, s.Gen2);   // 0 + 1
        Assert.Equal(5.0, s.GcPauseMs, 3);                 // (0.002 + 0.003) * 1000
        Assert.Equal(49000000 / (1024.0 * 1024.0), s.WsMb, 3); // пик working set
        Assert.Equal(2.0, s.AllocMbps, 3);                 // среднее 2 MiB/s
        Assert.Equal(8, s.Threads);                        // пик бегущей суммы дельт
    }

    [Fact]
    public void Rejects_unexpected_header()
    {
        var bad = "ts,prov,name,type,val\n06/24/2026 16:13:52,System.Runtime,x (By),Metric,1\n";
        Assert.Throws<FormatException>(() => CountersParser.Parse(bad.Split('\n')));
    }

    [Fact]
    public void Throws_when_no_header_present()
    {
        Assert.Throws<FormatException>(() => CountersParser.Parse(new[] { "", "   " }));
    }
}
