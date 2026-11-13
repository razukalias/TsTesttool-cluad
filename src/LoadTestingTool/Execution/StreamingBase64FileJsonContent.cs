using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

internal sealed class StreamingBase64FileJsonContent : HttpContent
{
    private readonly string _prefix;
    private readonly string _suffix;
    private readonly BinaryFileReference _file;

    public StreamingBase64FileJsonContent(string prefix, string suffix, BinaryFileReference file, string contentType)
    {
        _prefix = prefix;
        _suffix = suffix;
        _file = file;
        Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType);
    }

    protected override bool TryComputeLength(out long length)
    {
        var base64Length = checked(4L * ((_file.SizeBytes + 2L) / 3L));
        length = Encoding.UTF8.GetByteCount(_prefix) + base64Length + Encoding.UTF8.GetByteCount(_suffix);
        return true;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var prefix = Encoding.UTF8.GetBytes(_prefix);
        var suffix = Encoding.UTF8.GetBytes(_suffix);
        await stream.WriteAsync(prefix);
        await using var input = new FileStream(_file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        var carry = new byte[3];
        var carryCount = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            var offset = 0;
            if (carryCount > 0)
            {
                while (carryCount < 3 && offset < read) carry[carryCount++] = buffer[offset++];
                if (carryCount == 3)
                {
                    await WriteBase64Async(stream, carry.AsMemory(0, 3));
                    carryCount = 0;
                }
            }
            var usable = read - offset;
            var complete = usable - usable % 3;
            if (complete > 0) await WriteBase64Async(stream, buffer.AsMemory(offset, complete));
            offset += complete;
            while (offset < read) carry[carryCount++] = buffer[offset++];
        }
        if (carryCount > 0) await WriteBase64Async(stream, carry.AsMemory(0, carryCount));
        await stream.WriteAsync(suffix);
    }

    private static async Task WriteBase64Async(Stream destination, ReadOnlyMemory<byte> bytes)
    {
        var encoded = Convert.ToBase64String(bytes.Span);
        var output = Encoding.ASCII.GetBytes(encoded);
        await destination.WriteAsync(output);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
