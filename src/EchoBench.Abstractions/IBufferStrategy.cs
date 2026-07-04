namespace EchoBench.Abstractions;

/// <summary>
/// Стратегия выделения буферов. Тумблер пулинга вынесен в инъекцию,
/// чтобы переключать ArrayPool ↔ new[] как параметр прогона, а не через #if.
/// </summary>
public interface IBufferStrategy
{
    /// <summary>Выдаёт буфер размером не меньше <paramref name="size"/>.</summary>
    byte[] Rent(int size);

    /// <summary>Возвращает буфер стратегии (для непулящих реализаций — no-op).</summary>
    void Return(byte[] buffer);
}
