# HighLoadEchoBench

Стенд для честного сравнения моделей TCP-сервера под высокой нагрузкой на **.NET 10 / Linux**.
Одна и та же тривиальная задача — эхо (принял байты → отдал те же байты) — решается двумя
моделями конкуренции, и каждая замеряется по пропускной способности (RPS), латентности
(p50/p95/p99/p99.9) и поведению памяти/GC.

Центральный вопрос: **как обслуживать тысячи одновременных соединений и какая модель
конкуренции лучше держит нагрузку** — блокирующая (поток на соединение) или асинхронная
(epoll-цикл). Эхо выбрано намеренно: бизнес-логики нет, поэтому единственной значимой
переменной остаётся модель работы с соединениями. Это контролируемый эксперимент, а не
приложение.

## Сравниваемые модели

| Модель | Суть |
|---|---|
| `ThreadPerConnection` | блокирующий `Accept` + выделенный поток на соединение, синхронные `Read`/`Write` |
| `Async` | `async/await` поверх сокетов; на Linux это epoll-цикл рантайма (`SocketAsyncEngine`), масштабируется ThreadPool'ом |

## Матрица эксперимента

Сравнение идёт не по одной оси, а по полной декартовой матрице:

| Ось | Значения |
|---|---|
| Модель | `ThreadPerConnection` · `Async` |
| GC: куча | Workstation · Server (`DOTNET_gcServer`) |
| GC: конкурентность | Blocking · Background (`DOTNET_gcConcurrent`) |
| Пулинг буферов | `new byte[]` · `ArrayPool<byte>.Shared` |
| Соединения (свип) | 64 · 128 · 256 · 512 · 1024 (по умолчанию) |

2 модели × 2 × 2 режима GC × 2 пулинга × 5 значений свипа = **80 ячеек**, каждая — отдельный
прогон с собственной строкой в `results.csv`.

## Почему стенд устроен именно так

- **Процесс на ячейку.** Режим GC фиксируется на старте процесса и в рантайме не
  переключается, поэтому оркестратор запускает `Host` отдельным дочерним процессом на каждую
  ячейку, выставляя `DOTNET_gcServer` / `DOTNET_gcConcurrent` через окружение.
- **Метрики — вне процесса.** Измерять GC изнутри — значит самому аллоцировать и искажать
  картину (эффект наблюдателя). Счётчики `System.Runtime` снимает внешний `dotnet-counters`,
  прицепленный к PID сервера.
- **Изоляция прогонов.** Прогрев (JIT) отбрасывается, между ячейками выдерживается пауза;
  сервер и нагрузчик по умолчанию пиннятся `taskset` на непересекающиеся половины ядер,
  чтобы клиент не воровал CPU у сервера.
- **Coordinated omission.** Замкнутый цикл нагрузчика занижает хвост латентности; в режиме
  целевого RPS (`--rps`) латентности пишутся в HdrHistogram с ожидаемым интервалом — хвост
  честный.

## Быстрый старт

Требования: Linux, .NET SDK 10 (версия зафиксирована в [global.json](global.json)),
`dotnet-counters` (обязателен для оркестратора), `gnuplot` (опционально, для PNG),
`taskset` из util-linux (желательно, для CPU-изоляции).

```bash
# инструменты: dotnet-counters, dotnet-gcdump, gnuplot (умеет ставить без root в ~/.local)
scripts/setup.sh
export PATH="$HOME/.dotnet/tools:$HOME/.local/bin:$PATH"

# smoke: одна ячейка, убедиться что петля работает end-to-end
scripts/run.sh --max-cells 1

# полная матрица: build -c Release + 80 ячеек (~10 c на ячейку, порядка 15 минут)
scripts/run.sh
```

После прогона: `bench/results/results.csv` + графики `bench/results/img/*.png`.

## Что получается на выходе

Все выходы генерируются прогоном и в git не хранятся (см. `.gitignore`):

```
bench/results/
├─ results.csv        # строка на ячейку: все переменные + все метрики
├─ counters/*.csv     # сырой вывод dotnet-counters по каждой ячейке
├─ plot_*.dat         # tidy-данные, развёрнутые из results.csv для gnuplot
└─ img/*.png          # готовые графики
```

Колонки `results.csv`:

| Колонка | Смысл |
|---|---|
| `model`, `gc_server`, `gc_concurrent`, `pooled`, `conns` | независимые переменные ячейки (0/1 для булевых) |
| `dur_s` | фактическая длительность замера, с |
| `rps` | запросов/с по данным клиента |
| `p50_us` … `p999_us` | перцентили round-trip, мкс (HdrHistogram клиента) |
| `gen0`, `gen1`, `gen2` | число сборок GC за прогон |
| `gc_pause_ms` | суммарная длительность пауз GC, мс |
| `ws_mb` | пик working set, МиБ |
| `alloc_mbps` | средняя скорость аллокаций, МиБ/с |
| `threads` | пик числа потоков ThreadPool |

Графики (`bench/results/img/`):

- **memory.png** — флагман: working set от числа соединений, серия на каждую пару
  модель × режим GC;
- **rps.png** — RPS по режимам GC, кластеры моделей;
- **latency.png** — перцентили латентности по моделям (GC = Server + background);
- **alloc.png** — allocation rate: `ArrayPool` против `new[]`.

Перерисовать PNG без нового прогона (из уже готовых `plot_*.dat`) — из корня репозитория:
`gnuplot bench/plots/memory.gp` и т.д.

## Запуск компонентов вручную

### Host — эхо-сервер (1 процесс = 1 ячейка матрицы)

```bash
DOTNET_gcServer=1 DOTNET_gcConcurrent=1 \
  dotnet run -c Release --project src/EchoBench.Host -- --model Async --pooled true --port 9000
```

| Флаг | По умолчанию | Что задаёт |
|---|---|---|
| `--model` | `ThreadPerConnection` | `ThreadPerConnection` или `Async` |
| `--port` | `9000` | TCP-порт |
| `--pooled` | `true` | `ArrayPool<byte>` (true) или `new byte[]` (false) |
| `--buffer` | `4096` | размер буфера чтения/записи, байт |
| `--backlog` | `1024` | listen backlog |
| `--nodelay` | `true` | TCP_NODELAY |

Режим GC задаётся **только** переменными окружения `DOTNET_gcServer` / `DOTNET_gcConcurrent`
(намеренно не через csproj — иначе осядет в `runtimeconfig.json` и испортит эксперимент).
Сразу после `listen` хост печатает `READY` — этого ждёт оркестратор.

### LoadClient — TCP-нагрузчик

```bash
dotnet run -c Release --project src/EchoBench.LoadClient -- --port 9000 --conns 256 --duration 10
```

| Флаг | По умолчанию | Что задаёт |
|---|---|---|
| `--host` | `127.0.0.1` | адрес сервера |
| `--port` | `9000` | порт сервера |
| `--conns` | `16` | число одновременных соединений |
| `--duration` | `5` | длительность замера, с |
| `--warmup` | `1` | прогрев, с (в статистику не входит) |
| `--payload` | `64` | размер запроса, байт |
| `--rps` (`--target-rps`) | `0` | целевой суммарный RPS: `0` — closed-loop «на максимум»; `>0` — пейсинг с коррекцией coordinated omission |

Каждое соединение гоняет цикл «отправить payload → дочитать ровно столько же байт эха →
записать RTT»; итог — слитая гистограмма, RPS и перцентили (человекочитаемо + CSV-строка).

### Orchestrator — прогон матрицы

```bash
dotnet build -c Release   # оркестратор запускает уже собранные Host/LoadClient
dotnet run -c Release --no-build --project src/EchoBench.Orchestrator -- [опции]
```

| Флаг | По умолчанию | Что задаёт |
|---|---|---|
| `--conns-sweep` | `64,128,256,512,1024` | свип по соединениям (внутренняя ось матрицы) |
| `--conns` | — | одно значение вместо свипа |
| `--dur` / `--warmup` | `4` / `1` | замер и прогрев на ячейку, с |
| `--payload` | `128` | размер запроса, байт |
| `--max-cells` | — | ограничить число ячеек (smoke-прогон) |
| `--base-port` | `18000` | стартовый порт; каждой ячейке — свой |
| `--pause-ms` | `1500` | пауза между ячейками (дренаж портов, стабилизация GC) |
| `--out` | `bench/results/results.csv` | путь к итоговому CSV |
| `--server-cores` / `--client-cores` | половины ядер | явные списки ядер для `taskset` |
| `--no-pin` | — | выключить CPU-пиннинг |
| `--no-plots` | — | не строить графики после матрицы |

Цикл на ячейку: поднять `Host` с GC-окружением → дождаться `READY` → прицепить
`dotnet-counters` → прогнать `LoadClient` → распарсить счётчики → строка в `results.csv` →
погасить процесс и выдержать паузу. После матрицы — gnuplot.

## Тесты

```bash
dotnet test
```

xUnit v3: корректность эха для обеих моделей (включая payload больше буфера и параллельные
клиенты), стратегии буферов, разбор вывода `dotnet-counters`, `LatencyRecorder`
(CO-коррекция) и `ProcessRunner`.

## Структура репозитория

```
src/
├─ EchoBench.Abstractions/   контракты: IEchoServer, ServerConfig, IBufferStrategy
├─ EchoBench.Servers/        ThreadPerConnectionServer, AsyncServer, стратегии буферов, фабрика
├─ EchoBench.Host/           тонкий консольный хост: 1 процесс = 1 ячейка матрицы
├─ EchoBench.LoadClient/     нагрузчик + HdrHistogram
└─ EchoBench.Orchestrator/   матрица, запуск процессов, dotnet-counters, CSV, gnuplot
bench/
├─ plots/                    .gp-скрипты GNUplot
└─ results/                  выходы прогона (в git не попадают)
tests/EchoBench.Tests/       xUnit v3
scripts/                     setup.sh · run.sh
```

Зависимости проектов текут строго в сторону `Abstractions`; оркестратор не ссылается на
`Host`/`LoadClient` как на сборки — он запускает их как внешние процессы.