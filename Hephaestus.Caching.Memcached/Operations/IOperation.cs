using System;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hephaestus.Caching.Memcached.Operations
{
    internal interface IOperation
    {
        public ValueTask SerializeAsync(StringBuilder builder, PipeWriter writer, CancellationToken cancellationToken = default);

        public ValueTask DeserializeAsync(PipeReader reader, CancellationToken cancellationToken = default);

        public void TrySetException(Exception exception);

        public void TrySetCanceled();
    }
}
