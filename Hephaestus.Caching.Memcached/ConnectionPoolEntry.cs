using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hephaestus.Caching.Memcached
{
    internal class ConnectionPoolEntry : IAsyncDisposable
    {
        private readonly long _id;
        private readonly Connection _connection;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly CancellationTokenRegistration _cancellationTokenRegistration;
        private bool _disposed;

        public ConnectionPoolEntry(long id, Connection connection, CancellationTokenSource cancellationTokenSource, Action<long> finalizerCallback)
        {
            _id = id;
            _connection = connection;
            _cancellationTokenSource = cancellationTokenSource;

            _cancellationTokenRegistration = cancellationTokenSource.Token.Register((state) => finalizerCallback((long)state), _id);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await _connection.DisposeAsync().ConfigureAwait(false);

                _cancellationTokenRegistration.Dispose();
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
