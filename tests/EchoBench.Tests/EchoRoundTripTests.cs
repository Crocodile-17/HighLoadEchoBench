using System.Net;
using System.Net.Sockets;
using System.Text;
using EchoBench.Abstractions;
using EchoBench.Servers;
using Xunit;

namespace EchoBench.Tests;

/// <summary>
/// Корректность эха для ОБЕИХ моделей за единым контрактом: подняли сервер через фабрику,
/// подключились по TCP, отправили байты — должны получить ровно те же. Гоняем по обеим
/// моделям, чтобы общий каркас (accept-цикл) и обе реализации обработки соединения
/// доказанно работают «яблоки к яблокам».
/// </summary>
public sealed class EchoRoundTripTests
{
    public static TheoryData<ServerModel> Models => new()
    {
        ServerModel.Async,
        ServerModel.ThreadPerConnection,
    };

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Echoes_small_payload_unchanged(ServerModel model)
    {
        var config = new ServerConfig { Model = model, Port = ServerHarness.GetFreePort(), BufferSize = 1024 };
        await RunWithServer(config, async (port, ct) =>
        {
            var payload = Encoding.UTF8.GetBytes("hello echo world");
            byte[] echo = await RoundTrip(port, payload, ct);
            Assert.Equal(payload, echo);
        });
    }

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Echoes_payload_larger_than_buffer(ServerModel model)
    {
        // Payload крупнее BufferSize → эхо приходит несколькими чтениями; проверяем цикл send/receive.
        const int bufferSize = 512;
        var config = new ServerConfig { Model = model, Port = ServerHarness.GetFreePort(), BufferSize = bufferSize };
        await RunWithServer(config, async (port, ct) =>
        {
            byte[] payload = MakePayload(bufferSize * 5 + 123);
            byte[] echo = await RoundTrip(port, payload, ct);
            Assert.Equal(payload, echo);
        });
    }

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Echoes_multiple_sequential_messages_on_one_connection(ServerModel model)
    {
        var config = new ServerConfig { Model = model, Port = ServerHarness.GetFreePort(), BufferSize = 1024 };
        await RunWithServer(config, async (port, ct) =>
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, ct);
            NetworkStream stream = client.GetStream();

            for (int i = 0; i < 10; i++)
            {
                byte[] payload = Encoding.UTF8.GetBytes($"message-{i}");
                await stream.WriteAsync(payload, ct);

                var echo = new byte[payload.Length];
                await stream.ReadExactlyAsync(echo, ct);
                Assert.Equal(payload, echo);
            }
        });
    }

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Echoes_across_many_concurrent_connections(ServerModel model)
    {
        const int connections = 25;
        var config = new ServerConfig { Model = model, Port = ServerHarness.GetFreePort(), BufferSize = 1024 };
        await RunWithServer(config, async (port, ct) =>
        {
            IEnumerable<Task> roundTrips = Enumerable.Range(0, connections).Select(async i =>
            {
                byte[] payload = Encoding.UTF8.GetBytes($"conn-{i}-payload-{i * 7}");
                byte[] echo = await RoundTrip(port, payload, ct);
                Assert.Equal(payload, echo);
            });

            await Task.WhenAll(roundTrips);
        });
    }

    /// <summary>Поднимает сервер через фабрику, ждёт READY, прогоняет тело теста, аккуратно гасит.</summary>
    private static async Task RunWithServer(ServerConfig config, Func<int, CancellationToken, Task> body)
    {
        using var serverCts = new CancellationTokenSource();
        await using IEchoServer server = EchoServerFactory.Create(config);
        Task run = await ServerHarness.StartAsync(server, serverCts.Token);

        // Отдельный токен с тайм-аутом для клиентских операций — тест не должен висеть.
        using var opCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await body(config.Port, opCts.Token);
        }
        finally
        {
            serverCts.Cancel();
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                // штатная остановка accept-цикла
            }
        }
    }

    /// <summary>Одно соединение: отправить payload, дочитать ровно столько же байт эха.</summary>
    private static async Task<byte[]> RoundTrip(int port, byte[] payload, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(payload, ct);

        var echo = new byte[payload.Length];
        await stream.ReadExactlyAsync(echo, ct);
        return echo;
    }

    private static byte[] MakePayload(int size)
    {
        var data = new byte[size];
        for (int i = 0; i < size; i++)
            data[i] = (byte)(i % 251); // не кратно степени двойки — ловит сдвиги/обрезку
        return data;
    }
}
