using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Caching.Memcached
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMemcached(this IServiceCollection services)
        {
            return services
                .AddSingleton<IMemcachedClient, MemcachedClient>();
        }
    }
}
