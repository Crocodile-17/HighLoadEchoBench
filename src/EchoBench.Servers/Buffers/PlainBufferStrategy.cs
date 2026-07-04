using EchoBench.Abstractions;

namespace EchoBench.Servers.Buffers;

/// <summary>Без пулинга: каждый Rent — свежий массив, Return — no-op.</summary>
public sealed class PlainBufferStrategy : IBufferStrategy
{
    public byte[] Rent(int size) => new byte[size];

    public void Return(byte[] buffer)
    {
        // Намеренно пусто: массив отдаётся на откуп GC.
    }
}
