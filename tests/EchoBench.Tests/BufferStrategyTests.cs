using EchoBench.Abstractions;
using EchoBench.Servers.Buffers;
using Xunit;

namespace EchoBench.Tests;

/// <summary>
/// Контракт <see cref="IBufferStrategy"/> для обеих реализаций: Rent отдаёт массив не меньше
/// запрошенного, Return не падает. Пулинг — тумблер прогона, поэтому обе стратегии обязаны
/// вести себя одинаково по контракту, различаясь только аллокацией под капотом.
/// </summary>
public sealed class BufferStrategyTests
{
    // Параметризуем сериализуемым флагом (true = ArrayPool), стратегию строим внутри теста.
    public static TheoryData<bool> Pooled => new() { true, false };

    private static IBufferStrategy Create(bool pooled) =>
        pooled ? new PooledBufferStrategy() : new PlainBufferStrategy();

    [Theory]
    [MemberData(nameof(Pooled))]
    public void Rent_returns_buffer_at_least_requested_size(bool pooled)
    {
        IBufferStrategy strategy = Create(pooled);
        foreach (int size in new[] { 1, 16, 100, 4096, 65537 })
        {
            byte[] buffer = strategy.Rent(size);
            Assert.NotNull(buffer);
            Assert.True(buffer.Length >= size, $"Rent({size}) вернул массив длиной {buffer.Length}.");
            strategy.Return(buffer);
        }
    }

    [Theory]
    [MemberData(nameof(Pooled))]
    public void Return_does_not_throw_for_rented_buffer(bool pooled)
    {
        IBufferStrategy strategy = Create(pooled);
        byte[] buffer = strategy.Rent(4096);
        Exception? ex = Record.Exception(() => strategy.Return(buffer));
        Assert.Null(ex);
    }

    [Theory]
    [MemberData(nameof(Pooled))]
    public void Return_does_not_throw_for_arbitrary_array(bool pooled)
    {
        // Return должен переживать и «чужой» массив — ни одна стратегия не обязана падать.
        IBufferStrategy strategy = Create(pooled);
        Exception? ex = Record.Exception(() => strategy.Return(new byte[1024]));
        Assert.Null(ex);
    }

    [Fact]
    public void Plain_strategy_allocates_exact_size()
    {
        var strategy = new PlainBufferStrategy();
        byte[] buffer = strategy.Rent(1234);
        Assert.Equal(1234, buffer.Length);
    }
}
