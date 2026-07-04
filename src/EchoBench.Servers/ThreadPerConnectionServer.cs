using System.Net.Sockets;
using EchoBench.Abstractions;

namespace EchoBench.Servers;

/// <summary>
/// Классическая модель «поток на соединение»: каждое принятое соединение получает
/// выделенный фоновый поток ОС и обслуживается блокирующими Receive/Send. Просто и
/// предсказуемо, но масштабируется числом потоков — эталон, с которым сравниваются
/// остальные (epoll-) модели.
/// </summary>
public sealed class ThreadPerConnectionServer : EchoServerBase
{
    public ThreadPerConnectionServer(ServerConfig config, IBufferStrategy buffers)
        : base(config, buffers)
    {
    }

    protected override void Dispatch(Socket connection, CancellationToken ct)
    {
        var thread = new Thread(() => HandleConnection(connection, ct))
        {
            IsBackground = true,
            Name = "echo-conn",
        };
        thread.Start();
    }

    private void HandleConnection(Socket socket, CancellationToken ct)
    {
        var buffer = Buffers.Rent(Config.BufferSize);
        // Блокирующий Receive не видит отмену — на остановке закрываем сокет,
        // чтобы разбудить поток. Регистрация снимается при штатном закрытии соединения.
        using var cancelReg = ct.Register(static s => ((Socket)s!).Dispose(), socket);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = socket.Receive(buffer, 0, Config.BufferSize, SocketFlags.None);
                if (read == 0)
                    break; // соединение закрыто клиентом

                int sent = 0;
                while (sent < read)
                    sent += socket.Send(buffer, sent, read - sent, SocketFlags.None);
            }
        }
        catch (SocketException)
        {
            // соединение оборвалось — для эхо-стенда это не ошибка
        }
        catch (ObjectDisposedException)
        {
            // сокет закрыт по отмене
        }
        finally
        {
            Buffers.Return(buffer);
            socket.Dispose();
        }
    }
}
