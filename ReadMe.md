# Hephaestus.Caching

A .NET client implemented for Memcached. This project depends on [`Hephaestus.Extensions.Buffers`](https://github.com/meegasmullage/Hephaestus.Extensions).

## memcached
Free & open source, high-performance, distributed memory object caching system.

[https://memcached.org](https://memcached.org)

### Docker
[Docker Official Image](https://hub.docker.com/_/memcached)

```
docker run --name memcached -d -p 11211:11211 memcached memcached --memory-limit=64 -vvv
```