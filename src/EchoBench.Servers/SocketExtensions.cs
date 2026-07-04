using System.Net.Sockets;
using EchoBench.Abstractions;

namespace EchoBench.Servers;

/// <summary>Единая настройка сокет-опций — чтобы сравнивались модели, а не настройки.</summary>
internal static class SocketExtensions
{
    /// <summary>Готовит слушающий сокет: ReuseAddress + опции из конфига.</summary>
    public static void ConfigureListener(this Socket socket, ServerConfig config)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        if (config.NoDelay)
            socket.NoDelay = true;
    }

    /// <summary>Готовит принятое клиентское соединение.</summary>
    public static void ConfigureAccepted(this Socket socket, ServerConfig config)
    {
        if (config.NoDelay)
            socket.NoDelay = true;
    }
}
