using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Hephaestus.Caching.Memcached
{
    public class DuplexPipe : IAsyncDisposable
    {
        private readonly ILogger<DuplexPipe> _logger;
        private readonly long _id;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly NetworkStream _stream;
        private readonly Pipe _writerPipe;
        private readonly Pipe _readerPipe;
        private readonly Task _task;
        private bool _disposed;

        public PipeReader Reader => _readerPipe.Reader;

        public PipeWriter Writer => _writerPipe.Writer;

        public DuplexPipe(ILogger<DuplexPipe> logger, long id, Socket socket, CancellationTokenSource cancellationTokenSource)
        {
            _logger = logger;
            _id = id;
            _cancellationTokenSource = cancellationTokenSource;

            //
            _stream = new NetworkStream(socket, false);

            //
            _writerPipe = new Pipe();
            _readerPipe = new Pipe();

            var writerTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        var result = await _writerPipe.Reader.ReadAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

                        var resultBuffer = result.Buffer;

                        foreach (var chunk in resultBuffer)
                        {
                            _stream.Write(chunk.Span);
                        }

                        _writerPipe.Reader.AdvanceTo(resultBuffer.End);

                        await _stream.FlushAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
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
                        var buffer = _readerPipe.Writer.GetMemory();

                        var result = await _stream.ReadAsync(buffer, _cancellationTokenSource.Token).ConfigureAwait(false);
                        if (result == 0)
                        {
                            throw new EndOfStreamException();
                        }

                        _readerPipe.Writer.Advance(result);

                        await _readerPipe.Writer.FlushAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
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

            _task = Task.WhenAll(writerTask, readerTask).ContinueWith(async x =>
            {
                try
                {
                    await _writerPipe.Writer.CompleteAsync().ConfigureAwait(false);
                    await _readerPipe.Writer.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing. [Id={Id}]", _id);
                }
            });
        }

        public long Id => _id;

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await _task.ConfigureAwait(false);

                _stream.Close();
                _stream.Dispose();
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
