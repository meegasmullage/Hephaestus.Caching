namespace Hephaestus.Caching.Memcached
{
    internal static class Constants
    {
        public static class StatusCodes
        {
            public const int OK = 200;

            public const int NotFound = 404;

            public const int PreconditionFailed = 412;

            public const int ServiceUnavailable = 503;

            public const int InternalServerError = 500;
        }
    }
}
