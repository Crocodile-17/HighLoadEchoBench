using System.Diagnostics.Metrics;

namespace EchoBench.Host;

/// <summary>
/// Прикладные счётчики хоста. Метрики GC/памяти снимаются ВНЕ процесса
/// (dotnet-counters), здесь — только то, что знает само приложение.
/// </summary>
public sealed class EchoMetrics : IDisposable
{
    public const string MeterName = "EchoBench.Host";

    private readonly Meter _meter;
    private readonly Counter<long> _connections;
    private readonly Counter<long> _bytesEchoed;

    public EchoMetrics()
    {
        _meter = new Meter(MeterName);
        _connections = _meter.CreateCounter<long>("echo.connections", unit: "{conn}");
        _bytesEchoed = _meter.CreateCounter<long>("echo.bytes", unit: "By");
    }

    public void ConnectionAccepted() => _connections.Add(1);

    public void BytesEchoed(long count) => _bytesEchoed.Add(count);

    public void Dispose() => _meter.Dispose();
}
