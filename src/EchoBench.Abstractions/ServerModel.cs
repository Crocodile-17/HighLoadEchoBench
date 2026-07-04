namespace EchoBench.Abstractions;

/// <summary>Сравниваемые модели конкуренции TCP-сервера.</summary>
public enum ServerModel
{
    /// <summary>Блокирующий accept + поток на соединение.</summary>
    ThreadPerConnection,

    /// <summary>async/await поверх сокетов (epoll-цикл рантайма).</summary>
    Async,
}
