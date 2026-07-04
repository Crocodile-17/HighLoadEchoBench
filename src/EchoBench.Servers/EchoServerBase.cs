using System.Net;
using System.Net.Sockets;
using EchoBench.Abstractions;

namespace EchoBench.Servers;

/// <summary>
/// Общий каркас всех моделей: bind/listen, настройка сокета, accept-цикл.
/// Различается только обработка одного соединения — её реализуют наследники.
/// </summary>
public abstract class EchoServerBase : IEchoServer
{
    protected ServerConfig Config { get; }
    protected IBufferStrategy Buffers { get; }

    private Socket? _listener;

    protected EchoServerBase(ServerConfig config, IBufferStrategy buffers)
    {
        Config = config;
        Buffers = buffers;
    }

    /// <summary>Срабатывает сразу после успешного listen — хост сигналит READY.</summary>
    public event Action? Started;

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.ConfigureListener(Config);
        listener.Bind(new IPEndPoint(IPAddress.Any, Config.Port));
        listener.Listen(Config.Backlog);
        _listener = listener;

        Started?.Invoke();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Socket connection;
                try
                {
                    connection = await listener.AcceptAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                connection.ConfigureAccepted(Config);
                Dispatch(connection, ct);
            }
        }
        finally
        {
            listener.Close();
        }
    }

    /// <summary>Запускает обработку одного принятого соединения согласно модели.</summary>
    protected abstract void Dispatch(Socket connection, CancellationToken ct);

    public virtual ValueTask DisposeAsync()
    {
        _listener?.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
