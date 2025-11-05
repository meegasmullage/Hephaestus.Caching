using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hephaestus.Caching.Memcached.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hephaestus.Caching.Memcached
{
    public class MemcachedClient : IMemcachedClient
    {
        private readonly MemcachedClientOptions _options;
        private readonly ConnectionPool _connectionPool;
        private readonly IServiceProvider _serviceProvider;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed;

        public MemcachedClient(IServiceProvider serviceProvider, IOptions<MemcachedClientOptions> options)
        {
            _serviceProvider = serviceProvider;
            _options = options.Value;

            _cancellationTokenSource = new CancellationTokenSource();

            var uri = new Uri($"tcp://{_options.Endpoint}");

            EndPoint endPoint = uri.HostNameType switch
            {
                UriHostNameType.Dns => new DnsEndPoint(uri.Host, uri.Port),
                UriHostNameType.IPv4 => IPEndPoint.Parse(uri.Authority),
                _ => throw new ArgumentException($"Hostname type '{uri.HostNameType}' is not unsupported")
            };

            _connectionPool = (ConnectionPool)ActivatorUtilities.CreateInstance(_serviceProvider, typeof(ConnectionPool), endPoint, _cancellationTokenSource);
        }

        // NoOp
        public async Task NoOpAsync(CancellationToken cancellationToken = default)
        {
            var operation = new NoOp();

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            _ = await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        // Set
        public async Task<ulong> SetAsync(string key, ReadOnlySequence<byte> value, TimeSpan ttl, ulong? version = null, CancellationToken cancellationToken = default)
        {
            var operation = new Set(key, value, ttl, version);

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            return await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<ulong> SetIfMatchAsync(string key, ReadOnlySequence<byte> value, TimeSpan ttl, ulong ifMatch, ulong? version = null, CancellationToken cancellationToken = default)
        {
            var operation = new SetIfMatch(key, value, ttl, ifMatch, version);

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            return await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        // Get
        public async Task<ulong> GetAsync(string key, IBufferWriter<byte> writer, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            var operation = new Get(key, writer, ttl);

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            return await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        // Touch
        public async Task<ulong> TouchAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            var operation = new Touch(key, ttl);

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            return await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        // Delete
        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            var operation = new Delete(key);

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            _ = await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteIfMatchAsync(string key, ulong ifMatch, CancellationToken cancellationToken = default)
        {
            var operation = new DeleteIfMatch(key, ifMatch);

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            _ = await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        // Counter
        private async Task<ulong> CounterAsync(char direction, string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong? version = null, CancellationToken cancellationToken = default)
        {
            var operation = new Counter(direction, key, writer, ttl, version);

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            return await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<ulong> IncrementAsync(string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong? version = null, CancellationToken cancellationToken = default)
            => CounterAsync('I', key, writer, ttl, version, cancellationToken);

        public Task<ulong> DecrementAsync(string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong? version = null, CancellationToken cancellationToken = default)
            => CounterAsync('D', key, writer, ttl, version, cancellationToken);

        private async Task<ulong> CounterIfMatchAsync(char direction, string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong ifMatch, ulong? version = null, CancellationToken cancellationToken = default)
        {
            var operation = new CounterIfMatch(direction, key, writer, ttl, ifMatch, version);

            var connection = _connectionPool.GetConnection();

            await connection.Enqueue(operation, cancellationToken).ConfigureAwait(false);

            return await operation.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<ulong> IncrementIfMatchAsync(string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong ifMatch, ulong? version = null, CancellationToken cancellationToken = default)
            => CounterIfMatchAsync('I', key, writer, ttl, ifMatch, version, cancellationToken);

        public Task<ulong> DecrementIfMatchAsync(string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong ifMatch, ulong? version = null, CancellationToken cancellationToken = default)
            => CounterIfMatchAsync('D', key, writer, ttl, ifMatch, version, cancellationToken);

        // Dispose
        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }

                await _connectionPool.DisposeAsync().ConfigureAwait(false);

                _cancellationTokenSource.Dispose();
            }
            catch
            {
                //DELIBERATE EMPTY CATCH
            }

            _disposed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);

            GC.SuppressFinalize(this);
        }
    }
}
