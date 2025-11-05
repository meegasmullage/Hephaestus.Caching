using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Hephaestus.Caching.Memcached;

namespace Hephaestus.Caching.Memcached
{
    public static class PipeWriterExtensions
    {
        public static void Write(this PipeWriter writer, StringBuilder builder)
        {
            foreach (var chunk in builder.GetChunks())
            {
                var destination = writer.GetMemory(4096);

                var byteCount = Encoding.ASCII.GetBytes(chunk.Span, destination.Span);

                if (destination.Length < byteCount)
                {
#pragma warning disable CA2208 // Instantiate argument exceptions correctly
                    throw new ArgumentOutOfRangeException(nameof(destination), "Destination array was not long enough");
#pragma warning restore CA2208 // Instantiate argument exceptions correctly
                }

                writer.Advance(byteCount);
            }
        }

        public static void Write(this PipeWriter writer, ReadOnlySequence<byte> value)
        {
            foreach (var chunk in value)
            {
                writer.Write(chunk.Span);
            }
        }
    }
}
