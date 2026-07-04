using System.Buffers;
using EchoBench.Abstractions;

namespace EchoBench.Servers.Buffers;

/// <summary>Пулинг через общий <see cref="ArrayPool{T}"/>.</summary>
public sealed class PooledBufferStrategy : IBufferStrategy
{
    public byte[] Rent(int size) => ArrayPool<byte>.Shared.Rent(size);

    public void Return(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);
}
