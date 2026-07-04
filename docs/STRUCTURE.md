# Структура проекта

Полная раскладка решения вплоть до отдельных файлов с короткой пометкой, за что каждый
отвечает. Архитектурные обоснования — в [ARCHITECTURE.md](./ARCHITECTURE.md).

## Дерево

```
HighLoadEchoBench/
├─ HighLoadEchoBench.sln
├─ Directory.Build.props          # общие MSBuild-свойства (net10, Nullable, LangVersion) для всех проектов
├─ Directory.Packages.props       # централизованные версии NuGet (CPM)
├─ global.json                    # пин версии .NET SDK — воспроизводимость бенчмарка
├─ .editorconfig
├─ .gitignore
├─ README.md
├─ CLAUDE.md                      # контекст для Claude Code
├─ docs/
│  ├─ ARCHITECTURE.md             # архитектура
│  └─ STRUCTURE.md                # этот файл
│
├─ src/
│  ├─ EchoBench.Abstractions/     # чистые контракты, без поведения
│  │  ├─ EchoBench.Abstractions.csproj
│  │  ├─ IEchoServer.cs           # контракт сервера: RunAsync(ct)
│  │  ├─ ServerModel.cs           # enum: ThreadPerConnection | Async
│  │  ├─ ServerConfig.cs          # record с параметрами прогона (порт, backlog, буфер, пулинг…)
│  │  └─ IBufferStrategy.cs       # контракт пулинга: Rent/Return
│  │
│  ├─ EchoBench.Servers/          # 2 реализации модели + инфраструктура
│  │  ├─ EchoBench.Servers.csproj
│  │  ├─ EchoServerFactory.cs     # Create(config) → нужная IEchoServer + выбор IBufferStrategy по config
│  │  ├─ EchoServerBase.cs        # общий код: bind/listen, socket-опции, accept-цикл, сигнал готовности
│  │  ├─ SocketExtensions.cs      # единая настройка NoDelay/ReuseAddress/backlog
│  │  ├─ Buffers/
│  │  │  ├─ PooledBufferStrategy.cs   # ArrayPool<byte>.Shared.Rent/Return
│  │  │  └─ PlainBufferStrategy.cs    # new byte[size]; Return — пустой
│  │  ├─ ThreadPerConnectionServer.cs # блокирующий accept + поток на соединение
│  │  └─ AsyncServer.cs               # async/await поверх сокетов (epoll-модель рантайма)
│  │
│  ├─ EchoBench.Host/             # тонкий процесс: 1 запуск = 1 ячейка матрицы
│  │  ├─ EchoBench.Host.csproj    # GC в csproj НЕ задаём — им управляет env от оркестратора
│  │  ├─ Program.cs               # аргументы → конфиг → фабрика → запуск → сигнал READY → shutdown
│  │  ├─ CliOptions.cs            # аргументы CLI → ServerConfig
│  │  └─ EchoMetrics.cs           # Meter + прикладные счётчики (соединения, прокачанные байты)
│  │
│  ├─ EchoBench.LoadClient/       # генератор нагрузки + измерение латентности
│  │  ├─ EchoBench.LoadClient.csproj  # ссылка на HdrHistogram
│  │  ├─ Program.cs               # аргументы → запуск нагрузки → выдать результат
│  │  ├─ LoadOptions.cs           # аргументы CLI (host, port, conns, duration, payload, warmup)
│  │  ├─ LoadRunner.cs            # N воркеров, прогрев, тайминг, слияние гистограмм, RPS+перцентили
│  │  ├─ ConnectionWorker.cs      # цикл одного соединения: send → дочитать эхо → записать RTT
│  │  ├─ LatencyRecorder.cs       # обёртка HdrHistogram + коррекция coordinated omission
│  │  └─ LoadResult.cs            # DTO результата (rps, p50/p95/p99) + сериализация в строку CSV
│  │
│  └─ EchoBench.Orchestrator/     # прогон всей матрицы, сбор данных, графики
│     ├─ EchoBench.Orchestrator.csproj
│     ├─ Program.cs               # построить матрицу → цикл прогонов → CSV → графики
│     ├─ RunSpec.cs               # одна ячейка: модель, gcServer, gcConcurrent, pooled, conns, dur, port
│     ├─ ExperimentMatrix.cs      # генерация набора RunSpec (все сочетания осей)
│     ├─ ServerProcessLauncher.cs # запуск Host с env (DOTNET_gcServer/gcConcurrent) + ожидание READY + teardown
│     ├─ CountersCollector.cs     # dotnet-counters collect --process-id → counters.csv; gcdump на пике
│     ├─ CountersParser.cs        # разбор counters.csv → метрики GC/памяти
│     ├─ LoadClientRunner.cs      # запуск LoadClient (или tcpkali) + разбор результата
│     ├─ ResultWriter.cs          # склейка (нагрузка + счётчики) → строка в results.csv
│     ├─ PlotRunner.cs            # вызов gnuplot со скриптами после матрицы
│     └─ ProcessRunner.cs         # хелпер запуска внешних процессов (capture stdout/stderr, timeout)
│
├─ bench/
│  ├─ plots/
│  │  ├─ common.gp                # общие настройки gnuplot (терминал, стиль, пути), подключается остальными
│  │  ├─ rps.gp                   # RPS по моделям, сгруппировано по режиму GC
│  │  ├─ latency.gp               # перцентили латентности по моделям
│  │  ├─ memory.gp                # флагман: working set от числа соединений
│  │  └─ alloc.gp                 # allocation rate: пул против без пула
│  └─ results/
│     └─ .gitkeep                 # CSV пишутся сюда в рантайме (сам results.csv в .gitignore)
│
├─ tests/                         # опционально, но желательно
│  └─ EchoBench.Tests/
│     ├─ EchoBench.Tests.csproj
│     ├─ EchoRoundTripTests.cs    # поднять сервер, подключиться, проверить корректность эха (обе модели)
│     └─ BufferStrategyTests.cs   # Rent отдаёт массив ≥ запрошенного; Return не падает
│
└─ scripts/                       # опционально
   ├─ setup.sh                    # поставить dotnet-counters, dotnet-gcdump, tcpkali, gnuplot
   └─ run.sh                      # build -c Release + запуск оркестратора
```

## Зависимости между проектами

```
Abstractions  ←  Servers  ←  Host
      ↑              ↑
      └──────────────┴──  Orchestrator (запускает Host и LoadClient как процессы, не ссылается на них)
LoadClient  →  Abstractions (только для общих DTO/констант, если нужно)
Tests  →  Abstractions, Servers
```

Зависимости текут строго в одну сторону — к `Abstractions`, и никогда обратно. Оркестратор
не ссылается на `Host`/`LoadClient` как на сборки: он запускает их как отдельные процессы.

## Почему именно такая структура

- **`Abstractions` — только контракты** (интерфейсы, enum, record), без поведения. Поэтому
  реализации стратегий буфера лежат в `Servers/Buffers/`, а не рядом с интерфейсом. Это и
  держит однонаправленность зависимостей.

- **`EchoServerBase` + `SocketExtensions`** существуют потому, что обе модели делят
  одинаковый каркас bind/listen/accept и настройку сокет-опций; различается только обработка
  соединения. Это гарантирует, что сравниваются именно модели, а не случайные расхождения в
  настройке сокета.

- **`Host` намеренно тонкий и без GC-настроек в csproj** — режимом GC рулит только
  оркестратор через переменные окружения при запуске процесса. Зафиксируешь GC в csproj — оно
  осядет в `runtimeconfig.json` и испортит чистоту эксперимента.

- **`Orchestrator` разбит по ролям** (запуск процессов / сбор счётчиков / запуск нагрузчика /
  запись CSV / графики) отдельными файлами, потому что это самый «движущийся» код — внешние
  процессы и парсинг их вывода, — и его проще чинить и тестировать по кускам.

- **`LatencyRecorder` вынесен отдельно**, потому что именно там живёт коррекция coordinated
  omission — самое легко-ошибаемое место во всём измерении; держим его изолированным и под
  тестом.
