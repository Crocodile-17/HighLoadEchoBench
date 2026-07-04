using System.Net;
using System.Net.Sockets;
using EchoBench.Abstractions;
using EchoBench.Servers;

namespace EchoBench.Tests;

/// <summary>
/// Утилиты для тестов сервера: выбор свободного порта и запуск сервера с ожиданием,
/// пока он реально начнёт слушать (сигнал <see cref="EchoServerBase.Started"/> = «READY»).
/// </summary>
internal static class ServerHarness
{
    /// <summary>Занимает эфемерный порт у ОС и тут же освобождает, отдавая его номер.</summary>
    public static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>
    /// Запускает <paramref name="server"/> в фоне и ждёт сигнала готовности.
    /// Возвращает задачу <see cref="IEchoServer.RunAsync"/> — её следует дождаться при teardown.
    /// </summary>
    public static async Task<Task> StartAsync(IEchoServer server, CancellationToken ct)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((EchoServerBase)server).Started += () => ready.TrySetResult();

        Task run = server.RunAsync(ct);

        // Если bind/listen упал, RunAsync вернёт зафейленную задачу — пробрасываем ошибку.
        Task winner = await Task.WhenAny(ready.Task, run).ConfigureAwait(false);
        if (winner == run)
            await run.ConfigureAwait(false);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        return run;
    }
}
