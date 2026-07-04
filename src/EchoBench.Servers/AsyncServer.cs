using System.Net.Sockets;
using EchoBench.Abstractions;

namespace EchoBench.Servers;

/// <summary>
/// Модель async/await поверх сокетов. На Linux масштабируется epoll-циклом рантайма
/// (SocketAsyncEngine) и пулом потоков — отдельной настройки epoll не требуется.
/// </summary>
public sealed class AsyncServer : EchoServerBase
{
    public AsyncServer(ServerConfig config, IBufferStrategy buffers)
        : base(config, buffers)
    {
    }

    protected override void Dispatch(Socket connection, CancellationToken ct)
    {
        // Каждое соединение — независимая асинхронная задача поверх ThreadPool.
        _ = HandleConnectionAsync(connection, ct);
    }

    private async Task HandleConnectionAsync(Socket socket, CancellationToken ct)
    {
        var buffer = Buffers.Rent(Config.BufferSize);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await socket.ReceiveAsync(buffer.AsMemory(0, Config.BufferSize), ct)
                    .ConfigureAwait(false);
                if (read == 0)
                    break; // соединение закрыто клиентом

                int sent = 0;
                while (sent < read)
                {
                    sent += await socket.SendAsync(buffer.AsMemory(sent, read - sent), ct)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // штатная остановка
        }
        catch (SocketException)
        {
            // соединение оборвалось — для эхо-стенда это не ошибка
        }
        finally
        {
            Buffers.Return(buffer);
            socket.Dispose();
        }
    }
}
