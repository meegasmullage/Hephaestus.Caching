using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hephaestus.Caching.Memcached;

namespace Hephaestus.Caching.Memcached.Operations
{
    internal class Get : Operation<ulong>
    {
        private readonly string _key;
        private readonly IBufferWriter<byte> _writer;
        private readonly TimeSpan? _ttl;

        public Get(string key, IBufferWriter<byte> writer, TimeSpan? ttl = null)
        {
            _key = key;
            _writer = writer;
            _ttl = ttl;
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

            if (input[chunks[0]].Equals("EN", StringComparison.OrdinalIgnoreCase))
            {
                return Constants.StatusCodes.NotFound;
            }

            return Constants.StatusCodes.InternalServerError;
        }

        public override async ValueTask SerializeAsync(StringBuilder builder, PipeWriter writer, CancellationToken cancellationToken = default)
        {
            try
            {
                builder.Append("mg");

                builder.Append(' ');
                builder.Append(_key);

                if (_ttl != null)
                {
                    builder.Append(' ');
                    builder.Append('T');
                    builder.Append(((TimeSpan)_ttl).TotalSeconds);
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
                    case Constants.StatusCodes.NotFound:
                        TaskCompletionSource.SetResult(default);
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
