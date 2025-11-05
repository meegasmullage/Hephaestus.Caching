using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Hephaestus.Caching.Memcached;
using Hephaestus.Extensions.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hephaestus.Caching.Sandbox
{
    public class ExampleService : IHostedService
    {
        private readonly ILogger<ExampleService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly System.Timers.Timer _timer;
        private readonly ConcurrentBag<double> _elapsedTimes;

        public ExampleService(ILogger<ExampleService> logger, IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;

            _timer = new System.Timers.Timer
            {
                Interval = 1000,
                Enabled = true
            };

            _timer.Elapsed += TimerElapsed;

            _elapsedTimes = [];
        }

        private async Task CacheAsync(int tId)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var memcachedClient = scope.ServiceProvider.GetRequiredService<IMemcachedClient>();

                using (var cancellationTokenSource = new CancellationTokenSource())
                {
                    for (var i = 0; i < 1000; i++)
                    {
                        var rawCacheKey = new byte[Random.Shared.Next(1, 64)];
                        Random.Shared.NextBytes(rawCacheKey);

                        var cacheKey = Convert.ToHexString(rawCacheKey).ToLower();

                        var cacheValue = ArrayPool<byte>.Shared.Rent(1024 * Random.Shared.Next(1, 64));
                        Random.Shared.NextBytes(cacheValue);

                        //
                        var startingTimestamp = Stopwatch.GetTimestamp();

                        var latestVersion = await memcachedClient.SetAsync(cacheKey, new ReadOnlySequence<byte>(cacheValue), TimeSpan.FromSeconds(5), Convert.ToUInt64(i + 1), cancellationTokenSource.Token).ConfigureAwait(false);

                        latestVersion = await memcachedClient.SetIfMatchAsync(cacheKey, new ReadOnlySequence<byte>(cacheValue), TimeSpan.FromSeconds(10), latestVersion, latestVersion + 1, cancellationTokenSource.Token).ConfigureAwait(false);

                        using (var writer = new ChunkWriter())
                        {
                            latestVersion = await memcachedClient.GetAsync(cacheKey, writer, cancellationToken: cancellationTokenSource.Token).ConfigureAwait(false);
                            if (latestVersion != 0 && writer.Length != cacheValue.Length)
                            {
                                throw new InvalidOperationException();
                            }
                        }

                        latestVersion = await memcachedClient.TouchAsync(cacheKey, TimeSpan.FromSeconds(5), cancellationTokenSource.Token).ConfigureAwait(false);

                        await memcachedClient.DeleteIfMatchAsync(cacheKey, latestVersion, cancellationTokenSource.Token).ConfigureAwait(false);

                        await memcachedClient.DeleteAsync(cacheKey, cancellationTokenSource.Token).ConfigureAwait(false);

                        //
                        using (var writer = new ChunkWriter())
                        {
                            latestVersion = await memcachedClient.IncrementAsync(cacheKey, writer, TimeSpan.FromSeconds(5), cancellationToken: cancellationTokenSource.Token).ConfigureAwait(false);
                            var counter = writer.ToUInt64();

                            writer.Reset();

                            latestVersion = await memcachedClient.DecrementAsync(cacheKey, writer, TimeSpan.FromSeconds(5), cancellationToken: cancellationTokenSource.Token).ConfigureAwait(false);
                            counter = writer.ToUInt64();

                            writer.Reset();

                            latestVersion = await memcachedClient.IncrementIfMatchAsync(cacheKey, writer, TimeSpan.FromSeconds(5), latestVersion, cancellationToken: cancellationTokenSource.Token).ConfigureAwait(false);
                            counter = writer.ToUInt64();

                            writer.Reset();

                            latestVersion = await memcachedClient.DecrementIfMatchAsync(cacheKey, writer, TimeSpan.FromSeconds(5), latestVersion, cancellationToken: cancellationTokenSource.Token).ConfigureAwait(false);
                            counter = writer.ToUInt64();

                            writer.Reset();
                        }

                        await memcachedClient.NoOpAsync(cancellationTokenSource.Token).ConfigureAwait(false);

                        var elapsedTime = Stopwatch.GetElapsedTime(startingTimestamp);

                        _elapsedTimes.Add(elapsedTime.TotalMilliseconds);

                        _logger.LogInformation("ElapsedTime [{TaskId:00}] [{Iteration:000}] [{ElapsedMs}] [{ThreadId}] [{CacheSize}]",
                            tId, i, elapsedTime.TotalMilliseconds, Environment.CurrentManagedThreadId, cacheValue.Length);

                        ArrayPool<byte>.Shared.Return(cacheValue);

                        await Task.Delay(Random.Shared.Next(0, 100));
                    }
                }
            }
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                _timer.Enabled = false;

                Task.Run(async () =>
                {
                    var tasks = new Task[8];

                    for (var i = 0; i < tasks.Length; i++)
                    {
                        tasks[i] = CacheAsync(i);
                    }

                    await Task.WhenAll(tasks);

                    var average = _elapsedTimes.Average();
                    var min = _elapsedTimes.Min();
                    var max = _elapsedTimes.Max();

                    Console.WriteLine();
                    Console.WriteLine($"[Min={min}] [Max={max}] [Average={average}]");

                    _elapsedTimes.Clear();

                    await Task.Delay(TimeSpan.FromMinutes(5));

                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while running the service");
            }
            finally
            {
                _timer.Enabled = true;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _timer.Enabled = true;

            await Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer.Enabled = false;

            return Task.CompletedTask;
        }
    }
}
