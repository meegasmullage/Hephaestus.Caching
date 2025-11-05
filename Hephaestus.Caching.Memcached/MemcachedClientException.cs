using System;

namespace Hephaestus.Caching.Memcached
{
    public class MemcachedClientException : Exception
    {
        private readonly int _statusCode;

        public MemcachedClientException(int stausCode) : base(GetDescription(stausCode))
        {
            _statusCode = stausCode;
        }

        public MemcachedClientException(int stausCode, string message) : base(message)
        {
            _statusCode = stausCode;
        }

        public int StatusCode => _statusCode;

        private static string GetDescription(int stausCode) => stausCode switch
        {
            Constants.StatusCodes.ServiceUnavailable => "Service unavailable",
            Constants.StatusCodes.NotFound => "Not found",
            Constants.StatusCodes.PreconditionFailed => "Precondition failed",
            _ => throw new ArgumentException(null, nameof(stausCode))
        };
    }
}
