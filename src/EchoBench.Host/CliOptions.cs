using EchoBench.Abstractions;

namespace EchoBench.Host;

/// <summary>Разбор аргументов командной строки в <see cref="ServerConfig"/>.</summary>
internal static class CliOptions
{
    /// <summary>
    /// Поддерживает: --model, --port, --pooled, --buffer, --backlog, --nodelay.
    /// Неуказанные параметры берут значения по умолчанию из <see cref="ServerConfig"/>.
    /// </summary>
    public static ServerConfig Parse(string[] args)
    {
        var map = ToMap(args);

        var config = new ServerConfig();

        if (map.TryGetValue("model", out var model))
            config = config with { Model = Enum.Parse<ServerModel>(model, ignoreCase: true) };
        if (map.TryGetValue("port", out var port))
            config = config with { Port = int.Parse(port) };
        if (map.TryGetValue("pooled", out var pooled))
            config = config with { UseBufferPool = bool.Parse(pooled) };
        if (map.TryGetValue("buffer", out var buffer))
            config = config with { BufferSize = int.Parse(buffer) };
        if (map.TryGetValue("backlog", out var backlog))
            config = config with { Backlog = int.Parse(backlog) };
        if (map.TryGetValue("nodelay", out var nodelay))
            config = config with { NoDelay = bool.Parse(nodelay) };

        return config;
    }

    private static Dictionary<string, string> ToMap(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            var key = args[i][2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                map[key] = args[i + 1];
                i++;
            }
            else
            {
                map[key] = "true"; // флаг без значения
            }
        }
        return map;
    }
}
