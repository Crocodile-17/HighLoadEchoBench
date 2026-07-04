using EchoBench.Abstractions;
using EchoBench.Servers.Buffers;

namespace EchoBench.Servers;

/// <summary>Собирает нужную модель по конфигу и инжектит стратегию буферов.</summary>
public static class EchoServerFactory
{
    public static IEchoServer Create(ServerConfig config)
    {
        IBufferStrategy buffers = config.UseBufferPool
            ? new PooledBufferStrategy()
            : new PlainBufferStrategy();

        return config.Model switch
        {
            ServerModel.ThreadPerConnection => new ThreadPerConnectionServer(config, buffers),
            ServerModel.Async => new AsyncServer(config, buffers),

            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Model, "Неизвестная модель."),
        };
    }
}
