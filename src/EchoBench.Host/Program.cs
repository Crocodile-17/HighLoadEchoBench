using EchoBench.Host;
using EchoBench.Servers;

// Один процесс = одна ячейка матрицы. Аргументы → конфиг → фабрика → запуск.
var config = CliOptions.Parse(args);

await using var server = EchoServerFactory.Create(config);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Сигнал готовности для оркестратора: печатается сразу после listen.
if (server is EchoServerBase baseServer)
    baseServer.Started += () => Console.WriteLine("READY");

Console.WriteLine($"Starting {config.Model} server on port {config.Port} " +
                  $"(pooled={config.UseBufferPool}, buffer={config.BufferSize}).");

try
{
    await server.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // штатное завершение по Ctrl+C / отмене
}

Console.WriteLine("Stopped.");
