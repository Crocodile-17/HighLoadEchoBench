namespace EchoBench.Abstractions;

/// <summary>
/// Единый контракт эхо-сервера. Хост выбирает конкретную модель по конфигу,
/// нагрузчик и метрики работают с любой реализацией одинаково.
/// </summary>
public interface IEchoServer : IAsyncDisposable
{
    /// <summary>
    /// Поднимает слушатель и принимает соединения до отмены <paramref name="ct"/>.
    /// Возвращается, когда сервер остановлен.
    /// </summary>
    Task RunAsync(CancellationToken ct);
}
