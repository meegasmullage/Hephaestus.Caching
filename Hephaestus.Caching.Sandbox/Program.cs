using Hephaestus.Caching.Memcached;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hephaestus.Caching.Sandbox
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<MemcachedClientOptions>(hostContext.Configuration.GetSection("Memcached"));

                    services.AddMemcached();

                    services.AddHostedService<ExampleService>();
                });

            builder
                .UseConsoleLifetime()
                .Build()
                .Run();

        }
    }
}


