using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace Hephaestus.Caching.Memcached
{
    public interface IMemcachedClient : IAsyncDisposable
    {
        Task NoOpAsync(CancellationToken cancellationToken = default);

        Task<ulong> SetAsync(string key, ReadOnlySequence<byte> value, TimeSpan ttl, ulong? version = null, CancellationToken cancellationToken = default);

        Task<ulong> SetIfMatchAsync(string key, ReadOnlySequence<byte> value, TimeSpan ttl, ulong ifMatch, ulong? version = null, CancellationToken cancellationToken = default);

        Task<ulong> GetAsync(string key, IBufferWriter<byte> writer, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

        Task<ulong> TouchAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);

        Task DeleteAsync(string key, CancellationToken cancellationToken = default);

        Task DeleteIfMatchAsync(string key, ulong ifMatch, CancellationToken cancellationToken = default);

        Task<ulong> IncrementAsync(string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong? version = null, CancellationToken cancellationToken = default);

        Task<ulong> DecrementAsync(string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong? version = null, CancellationToken cancellationToken = default);

        Task<ulong> IncrementIfMatchAsync(string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong ifMatch, ulong? version = null, CancellationToken cancellationToken = default);

        Task<ulong> DecrementIfMatchAsync(string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong ifMatch, ulong? version = null, CancellationToken cancellationToken = default);
    }
}
