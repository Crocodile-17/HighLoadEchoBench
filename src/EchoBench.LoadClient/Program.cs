using EchoBench.LoadClient;

var options = LoadOptions.Parse(args);

var mode = options.TargetRps > 0 ? $"target {options.TargetRps} rps (CO-corrected)" : "closed-loop (max)";
Console.WriteLine($"Load: {options.Connections} conns → {options.Host}:{options.Port}, " +
                  $"payload={options.PayloadBytes}B, warmup={options.WarmupSeconds}s, " +
                  $"measure={options.DurationSeconds}s, {mode}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var runner = new LoadRunner(options);
var result = await runner.RunAsync(cts.Token);

// Человекочитаемый итог + машинная пара строк (заголовок + данные) для оркестратора.
Console.WriteLine(result);
Console.WriteLine(LoadResult.CsvHeader);
Console.WriteLine(result.ToCsvLine());
