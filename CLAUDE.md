# High-load TCP echo benchmark (.NET)

## Цель
Сравнить модели TCP-сервера под нагрузкой: thread-per-connection, async/await. Метрики: RPS, латентность p50/p95/p99, GC, память.

## Матрица
модель × режим GC (Workstation/Server, concurrent/blocking) × пулинг буферов (ArrayPool vs new[])

## Ключевое ограничение
Режим GC фиксируется на старте процесса → каждая ячейка матрицы = отдельный процесс,
запускаемый оркестратором с env DOTNET_gcServer/DOTNET_gcConcurrent.
Метрики снимаются ВНЕ процесса (dotnet-counters), чтобы не искажать GC.

## Раскладка
src/EchoBench.Abstractions — IEchoServer, ServerConfig, IBufferStrategy
src/EchoBench.Servers      — 2 модели + фабрика
src/EchoBench.Host         — консольный хост, выбирает модель по конфигу, отдаёт EventCounters
src/EchoBench.LoadClient   — TCP-нагрузчик + HdrHistogram (учесть coordinated omission)
src/EchoBench.Orchestrator — прогон матрицы, запуск процессов, сбор CSV
bench/plots, bench/results — GNUplot и выходные CSV

## Среда
.NET 10, Linux. Сборка: dotnet build. Запуск хоста: dotnet run --project src/EchoBench.Host

## Подробности
@docs/ARCHITECTURE.md
@docs/STRUCTURE.md
