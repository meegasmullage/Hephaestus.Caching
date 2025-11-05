using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hephaestus.Caching.Memcached;

namespace Hephaestus.Caching.Memcached.Operations
{
    internal class SetIfMatch : Operation<ulong>
    {
        private readonly string _key;
        private readonly ReadOnlySequence<byte> _value;
        private readonly TimeSpan _ttl;
        private readonly ulong _ifMatch;
        private readonly ulong? _version;

        public SetIfMatch(string key, ReadOnlySequence<byte> value, TimeSpan ttl, ulong ifMatch, ulong? version = null)
        {
            _key = key;
            _value = value;
            _ttl = ttl;
            _ifMatch = ifMatch;
            _version = version;
        }

        private static int Parse(ReadOnlySpan<char> input, out ulong version)
        {
            Span<Range> chunks = stackalloc Range[2];

            var count = input.Split(chunks, ' ', StringSplitOptions.RemoveEmptyEntries);

            if (input[chunks[0]].Equals("HD", StringComparison.OrdinalIgnoreCase))
            {
                version = ulong.Parse(input[chunks[1]][1..]);

                return Constants.StatusCodes.OK;
            }

            version = default;

            if (input[chunks[0]].Equals("NS", StringComparison.OrdinalIgnoreCase))
            {
                return Constants.StatusCodes.ServiceUnavailable;
            }

            if (input[chunks[0]].Equals("EX", StringComparison.OrdinalIgnoreCase))
            {
                return Constants.StatusCodes.PreconditionFailed;
            }

            if (input[chunks[0]].Equals("NF", StringComparison.OrdinalIgnoreCase))
            {
                return Constants.StatusCodes.NotFound;
            }

            return Constants.StatusCodes.InternalServerError;
        }

        public override async ValueTask SerializeAsync(StringBuilder builder, PipeWriter writer, CancellationToken cancellationToken = default)
        {
            try
            {
                builder.Append("ms");

                builder.Append(' ');
                builder.Append(_key);

                builder.Append(' ');
                builder.Append(_value.Length);

                builder.Append(' ');
                builder.Append('T');
                builder.Append(_ttl.TotalSeconds);

                builder.Append(' ');
                builder.Append('C');
                builder.Append(_ifMatch);

                if (_version != null)
                {
                    builder.Append(' ');
                    builder.Append('E');
                    builder.Append((ulong)_version);
                }

                builder.Append(' ');
                builder.Append('c');

                writer.Write(builder);

                writer.Write(Crlf);

                writer.Write(_value);

                writer.Write(Crlf);

                await writer.FlushAsync(cancellationToken);
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

                var statusCode = Parse(header, out var latestVersion);
                switch (statusCode)
                {
                    case Constants.StatusCodes.OK:
                        break;
                    case Constants.StatusCodes.ServiceUnavailable:
                    case Constants.StatusCodes.NotFound:
                    case Constants.StatusCodes.PreconditionFailed:
                        TaskCompletionSource.SetException(new MemcachedClientException(statusCode));
                        return;
                    default:
                        TaskCompletionSource.SetException(new MemcachedClientException(statusCode, header));
                        return;
                }

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
