using System.Diagnostics;
using System.Net.Sockets;

namespace EchoBench.LoadClient;

/// <summary>
/// Один воркер = одно соединение. На соединении цикл closed-loop: отправить payload,
/// дочитать ровно столько же байт эха, записать RTT. Если задан целевой темп
/// (<see cref="_intervalTicks"/> &gt; 0), воркер ещё и выдерживает интервал между
/// запросами (idle при опережении), создавая <i>задуманную</i> нагрузку — без неё
/// коррекции coordinated omission не к чему привязаться (см. <see cref="LatencyRecorder"/>).
/// </summary>
public sealed class ConnectionWorker
{
    private readonly LoadOptions _options;
    private readonly byte[] _payload;
    private readonly long _intervalTicks; // Stopwatch-тики между запросами; 0 — без пейсинга.

    public LatencyRecorder Latency { get; }
    public long Requests { get; private set; }

    public ConnectionWorker(LoadOptions options, long expectedIntervalMicros, long intervalTicks)
    {
        _options = options;
        _payload = new byte[options.PayloadBytes];
        Random.Shared.NextBytes(_payload);
        _intervalTicks = intervalTicks > 0 ? intervalTicks : 0;
        Latency = new LatencyRecorder(expectedIntervalMicros);
    }

    /// <param name="measureFromTimestamp">
    /// Значение <see cref="Stopwatch.GetTimestamp"/>, начиная с которого RTT идут в гистограмму
    /// (всё до — прогрев, отбрасывается). Часы у Stopwatch монотонные и общие на процесс,
    /// поэтому сравнивать метки между воркерами корректно.
    /// </param>
    public async Task RunAsync(long measureFromTimestamp, CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        await socket.ConnectAsync(_options.Host, _options.Port, ct).ConfigureAwait(false);

        var echo = new byte[_payload.Length];
        long nextSlot = Stopwatch.GetTimestamp();

        while (!ct.IsCancellationRequested)
        {
            // Пейсинг: если идём с опережением задуманного темпа — подождать слот.
            // Idle-ожидание НЕ входит в RTT (старт замера ниже), поэтому грубость таймера
            // влияет лишь на достигнутый RPS, но не на честность латентности.
            if (_intervalTicks > 0)
            {
                long ahead = nextSlot - Stopwatch.GetTimestamp();
                if (ahead > 0)
                {
                    var wait = TicksToTimeSpan(ahead);
                    if (wait > TimeSpan.FromMilliseconds(1))
                        await Task.Delay(wait, ct).ConfigureAwait(false);
                }
                nextSlot += _intervalTicks;
            }

            long start = Stopwatch.GetTimestamp();
            await SendAllAsync(socket, _payload, ct).ConfigureAwait(false);
            await ReceiveExactlyAsync(socket, echo, ct).ConfigureAwait(false);

            // Считаем только после прогрева — отбрасываем JIT/прогрев соединений.
            if (start >= measureFromTimestamp)
            {
                Latency.Record(TicksToMicros(Stopwatch.GetTimestamp() - start));
                Requests++;
            }
        }
    }

    private static async Task SendAllAsync(Socket socket, byte[] data, CancellationToken ct)
    {
        int sent = 0;
        while (sent < data.Length)
            sent += await socket.SendAsync(data.AsMemory(sent), ct).ConfigureAwait(false);
    }

    private static async Task ReceiveExactlyAsync(Socket socket, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await socket.ReceiveAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
                throw new IOException("Соединение закрыто сервером до получения полного эха.");
            read += n;
        }
    }

    private static long TicksToMicros(long ticks) =>
        (long)(ticks * 1_000_000.0 / Stopwatch.Frequency);

    private static TimeSpan TicksToTimeSpan(long ticks) =>
        TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
}
