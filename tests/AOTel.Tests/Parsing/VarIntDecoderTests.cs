using AOTel.Core.Parsing;

namespace AOTel.Tests.Parsing;

public class VarIntDecoderTests
{
    [Fact]
    public void Decode_Zero_ReturnsZeroAndOneByteConsumed()
    {
        // 0 in VarInt is just 0x00
        byte[] buffer = [0x00];
        
        var (value, consumed) = VarIntDecoder.Decode(buffer);
        
        Assert.Equal(0ul, value);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void Decode_OneHundredFifty_ReturnsCorrectValueAndBytesConsumed()
    {
        // 150 in VarInt is 0x96, 0x01
        byte[] buffer = [0x96, 0x01];
        
        var (value, consumed) = VarIntDecoder.Decode(buffer);
        
        Assert.Equal(150ul, value);
        Assert.Equal(2, consumed);
    }

    [Fact]
    public void Decode_MaxUInt64_ReturnsCorrectValueAndBytesConsumed()
    {
        // ulong.MaxValue is encoded as 9 bytes of 0xFF followed by 0x01
        byte[] buffer = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01];
        
        var (value, consumed) = VarIntDecoder.Decode(buffer);
        
        Assert.Equal(ulong.MaxValue, value);
        Assert.Equal(10, consumed);
    }

    [Fact]
    public void Decode_IncompleteBuffer_ReturnsZeroAndZeroBytesConsumed()
    {
        // 0x96 indicates another byte should follow, but the buffer ends early
        byte[] buffer = [0x96];
        
        var (value, consumed) = VarIntDecoder.Decode(buffer);
        
        // Our decoder contract specifies returning (0, 0) on incomplete streams
        Assert.Equal(0ul, value);
        Assert.Equal(0, consumed);
    }
}