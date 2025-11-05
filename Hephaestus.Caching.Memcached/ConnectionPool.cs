using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hephaestus.Caching.Memcached
{
    internal class ConnectionPool : IAsyncDisposable
    {
        private readonly ILogger<ConnectionPool> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly EndPoint _endPoint;
        private readonly object _lockObject;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentDictionary<long, ConnectionPoolEntry> _connectionPoolEntries;
        private readonly Channel<long> _finalizerChannel;
        private readonly Task _task;
        private Connection _connection;
        private long _id1;
        private long _id2;
        private bool _disposed;

        public ConnectionPool(ILogger<ConnectionPool> logger, IServiceProvider serviceProvider, EndPoint endPoint, CancellationTokenSource cancellationTokenSource)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _endPoint = endPoint;

            _lockObject = new object();
            _connectionPoolEntries = [];

            _id1 = 0;
            _id2 = -1;

            _finalizerChannel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true,
            });

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token);

            var finalizerTask = Task.Factory.StartNew(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var id = await _finalizerChannel.Reader.ReadAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

                        if (_connectionPoolEntries.TryRemove(id, out var connectionPoolEntry))
                        {
                            try
                            {
                                await connectionPoolEntry.DisposeAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "An error occurred while finalizing ConnectionPoolEntry. [Id={Id}]", id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        switch (ex)
                        {
                            case OperationCanceledException:
                                break;
                            default:
                                _logger.LogError(ex, "An error occurred while processing");
                                break;
                        }
                    }
                }
            }).Unwrap();

            _task = finalizerTask.ContinueWith(async x =>
            {
                foreach (var (id, connectionPoolEntry) in _connectionPoolEntries)
                {
                    try
                    {
                        await connectionPoolEntry.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An error occurred while finalizing the ConnectionPoolEntry. [Id={Id}]", id);
                    }
                }
            });
        }

        private void FinalizerCallback(long id)
        {
            Interlocked.Increment(ref _id1);

            if (!_finalizerChannel.Writer.TryWrite(id))
            {
                _logger.LogError("Write to Finalizer channel failed. [Id:{Id}]", id);
            }
        }

        public Connection GetConnection()
        {
            if (_id1 == _id2)
            {
                return _connection;
            }

            lock (_lockObject)
            {
                if (_id1 == _id2)
                {
                    return _connection;
                }

                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                var id = _id1;

                var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);

                var connection = (Connection)ActivatorUtilities.CreateInstance(_serviceProvider, typeof(Connection), id, _endPoint, cancellationTokenSource);

                var connectionPoolEntry = new ConnectionPoolEntry(id, connection, cancellationTokenSource, FinalizerCallback);

                if (!_connectionPoolEntries.TryAdd(id, connectionPoolEntry))
                {
                    throw new InvalidOperationException($"Failed to add connection to pool. [Id:{id}]");
                }

                _connection = connection;
                _id2 = id;
            }

            return _connection;
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await _task.ConfigureAwait(false);

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
