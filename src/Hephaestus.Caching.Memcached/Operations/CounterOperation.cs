using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hephaestus.Caching.Memcached;

namespace Hephaestus.Caching.Memcached.Operations
{
    internal class Counter : Operation<ulong>
    {
        private readonly char _direction;
        private readonly string _key;
        private readonly IBufferWriter<byte> _writer;
        private readonly TimeSpan _ttl;
        private readonly ulong? _version;

        public Counter(char direction, string key, IBufferWriter<byte> writer, TimeSpan ttl, ulong? version = null)
        {
            _direction = direction;
            _key = key;
            _writer = writer;
            _ttl = ttl;
            _version = version;
        }

        private static int Parse(ReadOnlySpan<char> input, out int length, out ulong version)
        {
            Span<Range> chunks = stackalloc Range[3];

            var count = input.Split(chunks, ' ', StringSplitOptions.RemoveEmptyEntries);

            if (input[chunks[0]].Equals("VA", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(input[chunks[1]]);
                version = ulong.Parse(input[chunks[2]][1..]);

                return Constants.StatusCodes.OK;
            }

            length = default;
            version = default;

            if (input[chunks[0]].Equals("NS", StringComparison.OrdinalIgnoreCase))
            {
                return Constants.StatusCodes.ServiceUnavailable;
            }

            return Constants.StatusCodes.InternalServerError;
        }

        public override async ValueTask SerializeAsync(StringBuilder builder, PipeWriter writer, CancellationToken cancellationToken = default)
        {
            try
            {
                builder.Append("ma");

                builder.Append(' ');
                builder.Append(_key);

                builder.Append(' ');
                builder.Append('M');
                builder.Append(_direction);

                builder.Append(' ');
                builder.Append('N');
                builder.Append(_ttl.TotalSeconds);

                builder.Append(' ');
                builder.Append('J');
                builder.Append('1');

                builder.Append(' ');
                builder.Append('D');
                builder.Append('1');

                builder.Append(' ');
                builder.Append('T');
                builder.Append(_ttl.TotalSeconds);

                if (_version != null)
                {
                    builder.Append(' ');
                    builder.Append('E');
                    builder.Append((ulong)_version);
                }

                builder.Append(' ');
                builder.Append('v');

                builder.Append(' ');
                builder.Append('c');

                writer.Write(builder);

                writer.Write(Crlf);

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TaskCompletionSource.SetException(ex);
                throw;
            }
        }

        public override async ValueTask DeserializeAsync(PipeReader reader, CancellationToken cancellationToken = default)
        {
            try
            {
                var header = await ReadHeaderAsync(reader, cancellationToken).ConfigureAwait(false);

                var statusCode = Parse(header, out var length, out var latestVersion);
                switch (statusCode)
                {
                    case Constants.StatusCodes.OK:
                        break;
                    case Constants.StatusCodes.ServiceUnavailable:
                        TaskCompletionSource.SetException(new MemcachedClientException(statusCode));
                        return;
                    default:
                        TaskCompletionSource.SetException(new MemcachedClientException(statusCode, header));
                        return;
                }

                await ReadContentAsync(reader, _writer, length, cancellationToken).ConfigureAwait(false);

                TaskCompletionSource.SetResult(latestVersion);
            }
            catch (Exception ex)
            {
                TaskCompletionSource.SetException(ex);
                throw;
            }
        }
    }
}
