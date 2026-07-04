namespace EchoBench.Abstractions;

/// <summary>Параметры одного прогона сервера (одна ячейка матрицы).</summary>
public sealed record ServerConfig
{
    /// <summary>Какая модель конкуренции поднимается.</summary>
    public ServerModel Model { get; init; }

    /// <summary>TCP-порт прослушивания.</summary>
    public int Port { get; init; } = 9000;

    /// <summary>Длина очереди ожидающих соединений (listen backlog).</summary>
    public int Backlog { get; init; } = 1024;

    /// <summary>Размер буфера чтения/записи в байтах.</summary>
    public int BufferSize { get; init; } = 4096;

    /// <summary>Пулинг буферов: ArrayPool (true) против new byte[] (false).</summary>
    public bool UseBufferPool { get; init; } = true;

    /// <summary>Отключение алгоритма Нейгла (TCP_NODELAY).</summary>
    public bool NoDelay { get; init; } = true;
}
