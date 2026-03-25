using System;
using System.IO;
using System.Threading.Tasks;

namespace BIM765T.Revit.Bridge.Tests;

public sealed class PipeClientProtocolTests
{
    [Fact]
    public async Task ReadResponseLineAsync_Returns_Line_When_Data_Arrives()
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("ok\r\n"));
        using var reader = new StreamReader(stream);

        var line = await PipeClientProtocol.ReadResponseLineAsync(reader, TimeSpan.FromSeconds(1));

        Assert.Equal("ok", line);
    }

    [Fact]
    public async Task ReadResponseLineAsync_Times_Out_When_Server_Stalls()
    {
        using var reader = new StreamReader(new NeverEndingStream());

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => PipeClientProtocol.ReadResponseLineAsync(reader, TimeSpan.FromMilliseconds(150)));

        Assert.Contains("Timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
        {
            return new TaskCompletionSource<int>().Task;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(new TaskCompletionSource<int>().Task);
        }
    }
}
