using System.Buffers;
using AOTel.Core.Parsing;

namespace AOTel.Tests.Parsing;

public class VarIntDecoderTests
{
    [Fact]
    public void Decode_Zero_ReturnsZeroAndOneByteConsumed()
    {
        // 0 in VarInt is just 0x00
        byte[] buffer = [0x00];
        var sequence = new ReadOnlySequence<byte>(buffer);
        var reader = new SequenceReader<byte>(sequence);

        bool success = VarIntDecoder.TryDecode(ref reader, out ulong value);

        Assert.True(success);
        Assert.Equal(0ul, value);
        Assert.Equal(1, reader.Consumed);
    }

    [Fact]
    public void Decode_OneHundredFifty_ReturnsCorrectValueAndBytesConsumed()
    {
        // 150 in VarInt is 0x96, 0x01
        byte[] buffer = [0x96, 0x01];
        var sequence = new ReadOnlySequence<byte>(buffer);
        var reader = new SequenceReader<byte>(sequence);

        bool success = VarIntDecoder.TryDecode(ref reader, out ulong value);

        Assert.True(success);
        Assert.Equal(150ul, value);
        Assert.Equal(2, reader.Consumed);
    }

    [Fact]
    public void Decode_MaxUInt64_ReturnsCorrectValueAndBytesConsumed()
    {
        // ulong.MaxValue is encoded as 9 bytes of 0xFF followed by 0x01
        byte[] buffer = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01];
        var sequence = new ReadOnlySequence<byte>(buffer);
        var reader = new SequenceReader<byte>(sequence);

        bool success = VarIntDecoder.TryDecode(ref reader, out ulong value);

        Assert.True(success);
        Assert.Equal(ulong.MaxValue, value);
        Assert.Equal(10, reader.Consumed);
    }

    [Fact]
    public void Decode_IncompleteBuffer_ReturnsZeroAndZeroBytesConsumed()
    {
        // 0x96 indicates another byte should follow, but the buffer ends early
        byte[] buffer = [0x96];
        var sequence = new ReadOnlySequence<byte>(buffer);
        var reader = new SequenceReader<byte>(sequence);

        bool success = VarIntDecoder.TryDecode(ref reader, out ulong value);

        Assert.False(success);
        Assert.Equal(0ul, value);
    }

    [Fact]
    public void TryDecode_CrossSegmentBoundary_ReturnsCorrectValue()
    {
        // 150 in VarInt is 0x96, 0x01
        byte[] part1 = [0x96];
        byte[] part2 = [0x01];

        var sequence = CreateSegmentedSequence(part1, part2);
        var reader = new SequenceReader<byte>(sequence);

        bool success = VarIntDecoder.TryDecode(ref reader, out ulong value);

        Assert.True(success);
        Assert.Equal(150ul, value);
        Assert.Equal(2, reader.Consumed);
    }

    private static ReadOnlySequence<byte> CreateSegmentedSequence(params byte[][] arrays)
    {
        if (arrays.Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        if (arrays.Length == 1)
        {
            return new ReadOnlySequence<byte>(arrays[0]);
        }

        var first = new BufferSegment(arrays[0]);
        var last = first;

        for (int i = 1; i < arrays.Length; i++)
        {
            last = last.Append(arrays[i]);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public BufferSegment Append(ReadOnlyMemory<byte> memory) =>
            (BufferSegment)(Next = new BufferSegment(memory) { RunningIndex = RunningIndex + Memory.Length });
    }
}
