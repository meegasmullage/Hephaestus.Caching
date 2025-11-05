using System.Text;
using Hephaestus.Extensions.Buffers;

namespace Hephaestus.Caching.Memcached
{
    public static class ChunkWriterExtensions
    {
        public static ulong ToUInt64(this ChunkWriter writer)
        {
            var value = Encoding.ASCII.GetString(writer.Buffer);

            return string.IsNullOrEmpty(value) ? 0 : ulong.Parse(value);
        }
    }
}
