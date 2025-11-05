using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hephaestus.Caching.Memcached.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hephaestus.Caching.Memcached
{
    internal class Connection : IAsyncDisposable
    {
        private readonly ILogger<Connection> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly long _id;
        private readonly EndPoint _endPoint;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Socket _socket;
        private readonly DuplexPipe _duplexPipe;
        private readonly Channel<IOperation> _writerChannel;
        private readonly Channel<IOperation> _readerChannel;
        private readonly Task _task;
        private bool _disposed;

        public Connection(ILogger<Connection> logger, IServiceProvider serviceProvider, long id, EndPoint endPoint, CancellationTokenSource cancellationTokenSource)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _id = id;
            _endPoint = endPoint;
            _cancellationTokenSource = cancellationTokenSource;

            //
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 45);
            _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 45);
            _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 9);

            _socket.Connect(_endPoint);

            //
            _duplexPipe = (DuplexPipe)ActivatorUtilities.CreateInstance(_serviceProvider, typeof(DuplexPipe), _id, _socket, _cancellationTokenSource);

            //
            _writerChannel = Channel.CreateBounded<IOperation>(new BoundedChannelOptions(1024)
            {
                SingleWriter = false,
                SingleReader = true
            });

            _readerChannel = Channel.CreateUnbounded<IOperation>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true
            });

            //
            var writerTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    var stringBuilder = new StringBuilder(2048);

                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        var operation = await _writerChannel.Reader.ReadAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

                        await operation.SerializeAsync(stringBuilder, _duplexPipe.Writer, _cancellationTokenSource.Token).ConfigureAwait(false);

                        await _readerChannel.Writer.WriteAsync(operation).ConfigureAwait(false);

                        stringBuilder.Clear();
                    }
                }
                catch (Exception ex)
                {
                    switch (ex)
                    {
                        case OperationCanceledException:
                            break;
                        default:
                            _logger.LogError(ex, "An error occurred while processing. [Id={Id}]", _id);
                            break;
                    }

                    if (!_cancellationTokenSource.IsCancellationRequested) { _cancellationTokenSource.Cancel(); }
                }
            }, TaskCreationOptions.LongRunning).Unwrap();

            var readerTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        var operation = await _readerChannel.Reader.ReadAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

                        await operation.DeserializeAsync(_duplexPipe.Reader, _cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    switch (ex)
                    {
                        case OperationCanceledException:
                            break;
                        default:
                            _logger.LogError(ex, "An error occurred while processing. [Id={Id}]", _id);
                            break;
                    }

                    if (!_cancellationTokenSource.IsCancellationRequested) { _cancellationTokenSource.Cancel(); }
                }
            }, TaskCreationOptions.LongRunning).Unwrap();

            _task = Task.WhenAll(writerTask, readerTask).ContinueWith(x =>
            {
                try
                {
                    _writerChannel.Writer.Complete();
                    _readerChannel.Writer.Complete();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing. [Id={Id}]", _id);
                }
            });
        }

        public long Id => _id;

        public ValueTask Enqueue(IOperation operation, CancellationToken cancellationToken = default)
            => _writerChannel.Writer.WriteAsync(operation, cancellationToken);

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await _task.ConfigureAwait(false);

                while (_writerChannel.Reader.TryRead(out var operation))
                {
                    operation.TrySetCanceled();
                }

                while (_readerChannel.Reader.TryRead(out var operation))
                {
                    operation.TrySetCanceled();
                }

                try { _socket.Shutdown(SocketShutdown.Both); } catch { } //DELIBERATE EMPTY CATCH

                await _duplexPipe.DisposeAsync().ConfigureAwait(false);

                _socket.Close();
                _socket.Dispose();
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
