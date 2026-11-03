using System.Buffers.Binary;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Usb;

internal static class LyProtocol
{
    public const int VendorId = 0x0416;
    public const int ProductLy = 0x5408;
    public const int ProductLy1 = 0x5409;
    public const int TrofeoVision113Profile = 0x05;
    public const int TrofeoVision113Width = 1920;
    public const int TrofeoVision113Height = 400;
    public const int MaximumJpegSize = 650_000;

    private const int ChunkSize = 512;
    private const int ChunkHeaderSize = 16;
    private const int ChunkDataSize = ChunkSize - ChunkHeaderSize;

    private static readonly byte[] HandshakeHeader =
    [
        0x02, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    public static byte[] CreateHandshake()
    {
        var payload = new byte[2048];
        HandshakeHeader.CopyTo(payload, 0);
        return payload;
    }

    public static DeviceInfo ParseHandshake(ReadOnlySpan<byte> response, int productId)
    {
        if (response.Length < 37 || response[0] != 0x03 || response[1] != 0xFF || response[8] != 0x01)
        {
            throw new InvalidDataException("LCD handshake validation failed");
        }

        var lyProfile = productId == ProductLy ? response[20] : -1;
        int pm;
        int sub;
        if (productId == ProductLy)
        {
            var rawPm = lyProfile;
            if (rawPm <= 3)
            {
                rawPm = 1;
            }
            pm = 64 + rawPm;
            sub = response.Length > 22 ? response[22] + 1 : 0;
        }
        else
        {
            pm = 50 + response[36];
            sub = response.Length > 22 ? response[22] : 0;
        }

        var fbl = pm switch
        {
            65 or 66 or 68 or 69 => 192,
            63 or 64 => 114,
            _ => pm,
        };

        var (width, height, jpeg, rotate) = fbl switch
        {
            192 => (1920, 480, true, true),
            114 => (1600, 720, true, true),
            128 => (1280, 480, true, true),
            _ => (1920, 480, true, true),
        };
        if (fbl == 192 && pm == 68)
        {
            (width, height) = (1280, 480);
        }
        else if (fbl == 192 && pm == 69)
        {
            (width, height) = (1920, 440);
        }

        // Trofeo Vision 11.3 shares 0416:5408 with the 9.16-inch models. Its
        // handshake profile is 0x05 even though legacy metadata describes a
        // 480/440-line panel.
        if (productId == ProductLy && lyProfile == TrofeoVision113Profile)
        {
            (width, height) = (TrofeoVision113Width, TrofeoVision113Height);
        }

        return new DeviceInfo(pm, sub, fbl, width, height, jpeg, rotate, productId);
    }

    public static bool IsTrofeoVision113(DeviceInfo info) =>
        info.ProductId == ProductLy &&
        info.Pm == 64 + TrofeoVision113Profile &&
        info.Width == TrofeoVision113Width &&
        info.Height == TrofeoVision113Height;

    public static byte[] CreateFrameTransfer(ReadOnlySpan<byte> jpeg, int productId)
    {
        if (jpeg.Length == 0 || jpeg.Length > MaximumJpegSize)
        {
            throw new ArgumentOutOfRangeException(nameof(jpeg),
                $"JPEG frame must be between 1 and {MaximumJpegSize} bytes");
        }

        var numberOfChunks = jpeg.Length / ChunkDataSize + 1;
        var padMultiple = productId == ProductLy ? 4 : 1;
        var paddedChunks = ((numberOfChunks + padMultiple - 1) / padMultiple) * padMultiple;
        var transfer = new byte[paddedChunks * ChunkSize];
        var command = productId == ProductLy ? (byte)0x01 : (byte)0x02;

        for (var index = 0; index < numberOfChunks; index++)
        {
            var destination = transfer.AsSpan(index * ChunkSize, ChunkSize);
            var sourceOffset = index * ChunkDataSize;
            var dataLength = index == numberOfChunks - 1
                ? jpeg.Length % ChunkDataSize
                : ChunkDataSize;
            destination[0] = 0x01;
            destination[1] = 0xFF;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[2..6], (uint)jpeg.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[6..8], (ushort)dataLength);
            destination[8] = command;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[9..11], (ushort)numberOfChunks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[11..13], (ushort)index);
            if (dataLength > 0)
            {
                jpeg.Slice(sourceOffset, dataLength).CopyTo(destination[ChunkHeaderSize..]);
            }
        }
        return transfer;
    }
}
