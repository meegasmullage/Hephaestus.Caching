using System.Threading;
using System.Threading.Tasks;

namespace Hephaestus.Caching.Memcached.Operations
{
    internal interface IOperation<T> : IOperation
    {
        public Task<T> GetResultAsync(CancellationToken cancellationToken = default);
    }
}
