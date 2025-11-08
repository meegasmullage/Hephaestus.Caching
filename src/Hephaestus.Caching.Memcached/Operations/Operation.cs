using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hephaestus.Caching.Memcached.Operations
{
    internal abstract class Operation<T> : IOperation<T>
    {
        protected static readonly byte[] Crlf = [(byte)'\r', (byte)'\n'];

        private readonly TaskCompletionSource<T> _taskCompletionSource;

        protected Operation()
        {
            _taskCompletionSource = new TaskCompletionSource<T>();
        }

        protected TaskCompletionSource<T> TaskCompletionSource => _taskCompletionSource;

        public abstract ValueTask SerializeAsync(StringBuilder builder, PipeWriter writer, CancellationToken cancellationToken = default);

        public abstract ValueTask DeserializeAsync(PipeReader reader, CancellationToken cancellationToken = default);

        public Task<T> GetResultAsync(CancellationToken cancellationToken = default)
            => _taskCompletionSource.Task.WaitAsync(cancellationToken);

        public void TrySetCanceled()
            => _taskCompletionSource.TrySetCanceled();

        public void TrySetException(Exception exception)
            => _taskCompletionSource.TrySetException(exception);

        protected static async Task<string> ReadHeaderAsync(PipeReader reader, CancellationToken cancellationToken = default)
        {
            static bool TryReadTo(ref ReadOnlySequence<byte> buffer, ReadOnlySpan<byte> delimiter, out ReadOnlySequence<byte> sequence, out SequencePosition consumed)
            {
                var reader = new SequenceReader<byte>(buffer);

                if (reader.TryReadTo(out sequence, delimiter, true))
                {
                    consumed = reader.Position;

                    return true;
                }

                sequence = default;
                consumed = default;

                return false;
            }

            do
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                var resultBuffer = result.Buffer;

                if (TryReadTo(ref resultBuffer, Crlf, out var sequence, out var consumed))
                {
                    var value = Encoding.ASCII.GetString(sequence);

                    reader.AdvanceTo(consumed);

                    return value;
                }

                if (result.IsCompleted)
                {
                    throw new SocketException((int)SocketError.SocketError, "End of the data stream has been reached");
                }

                reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            } while (true);
        }

        protected static async Task ReadContentAsync(PipeReader reader, IBufferWriter<byte> writer, int length, CancellationToken cancellationToken = default)
        {
            var lengthWithCrlf = length + Crlf.Length;

            do
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                var resultBuffer = result.Buffer;

                if (resultBuffer.Length >= lengthWithCrlf)
                {
                    foreach (var chunk in resultBuffer.Slice(0, length))
                    {
                        var memory = writer.GetMemory(chunk.Length);

                        chunk.CopyTo(memory);

                        writer.Advance(chunk.Length);
                    }

                    reader.AdvanceTo(resultBuffer.GetPosition(lengthWithCrlf));

                    break;
                }

                if (result.IsCompleted)
                {
                    throw new SocketException((int)SocketError.SocketError, "End of the data stream has been reached");
                }

                reader.AdvanceTo(resultBuffer.Start, resultBuffer.End);
            } while (true);
        }
    }
}
