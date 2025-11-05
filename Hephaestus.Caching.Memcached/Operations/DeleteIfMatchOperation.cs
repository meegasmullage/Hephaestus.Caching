using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hephaestus.Caching.Memcached;

namespace Hephaestus.Caching.Memcached.Operations
{
    internal class DeleteIfMatch : Operation<ulong>
    {
        private readonly string _key;
        private readonly ulong _ifMatch;

        public DeleteIfMatch(string key, ulong ifMatch)
        {
            _key = key;
            _ifMatch = ifMatch;
        }

        private static int Parse(ReadOnlySpan<char> input)
        {
            Span<Range> chunks = stackalloc Range[1];

            var count = input.Split(chunks, ' ', StringSplitOptions.RemoveEmptyEntries);

            if (input[chunks[0]].Equals("HD", StringComparison.OrdinalIgnoreCase))
            {
                return Constants.StatusCodes.OK;
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
                builder.Append("md");

                builder.Append(' ');
                builder.Append(_key);

                builder.Append(' ');
                builder.Append('C');
                builder.Append(_ifMatch);

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

                var statusCode = Parse(header);
                switch (statusCode)
                {
                    case Constants.StatusCodes.OK:
                    case Constants.StatusCodes.NotFound:
                        break;
                    case Constants.StatusCodes.PreconditionFailed:
                        TaskCompletionSource.SetException(new MemcachedClientException(statusCode));
                        return;
                    default:
                        TaskCompletionSource.SetException(new MemcachedClientException(statusCode, header));
                        return;
                }

                TaskCompletionSource.SetResult(default);
            }
            catch (Exception ex)
            {
                TaskCompletionSource.SetException(ex);
                throw;
            }
        }
    }
}
