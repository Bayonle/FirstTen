using System.Buffers.Binary;
using System.Text;
using First10.Modules.Intake.Media;

namespace First10.Infrastructure.Modules.Intake.Media;

public sealed class OggOpusAudioSanitizer : IAudioSanitizer
{
    private static ReadOnlySpan<byte> OpusHead => "OpusHead"u8;
    private static ReadOnlySpan<byte> OpusTags => "OpusTags"u8;

    public Task<SanitizedAudio> SanitizeAsync(
        ReadOnlyMemory<byte> rawAudio,
        string sourceContentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceContentType != "audio/ogg"
            || rawAudio.Length == 0
            || rawAudio.Length > MediaPrivacyPolicy.MaximumAudioBytes)
        {
            throw new MediaPrivacyException(MediaFailureCode.UnsupportedContentType);
        }

        var sanitized = rawAudio.ToArray();
        try
        {
            var headOffset = sanitized.AsSpan().IndexOf(OpusHead);
            var tagsOffset = sanitized.AsSpan().IndexOf(OpusTags);
            if (headOffset < 0 || tagsOffset < 0 || tagsOffset + 16 > sanitized.Length)
            {
                throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
            }

            var vendorLength = BinaryPrimitives.ReadUInt32LittleEndian(
                sanitized.AsSpan(tagsOffset + 8, 4));
            if (vendorLength > 64 * 1024
                || tagsOffset + 16L + vendorLength > sanitized.Length)
            {
                throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
            }

            var vendorStart = tagsOffset + 12;
            sanitized.AsSpan(vendorStart, checked((int)vendorLength)).Clear();
            var commentCountOffset = vendorStart + checked((int)vendorLength);
            var commentCount = BinaryPrimitives.ReadUInt32LittleEndian(
                sanitized.AsSpan(commentCountOffset, 4));
            var cursor = commentCountOffset + 4;
            for (var index = 0U; index < commentCount; index++)
            {
                if (cursor + 4 > sanitized.Length)
                {
                    throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
                }

                var commentLength = BinaryPrimitives.ReadUInt32LittleEndian(
                    sanitized.AsSpan(cursor, 4));
                cursor += 4;
                if (commentLength > 1024 * 1024 || cursor + (long)commentLength > sanitized.Length)
                {
                    throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
                }

                sanitized.AsSpan(cursor, checked((int)commentLength)).Clear();
                cursor += checked((int)commentLength);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                sanitized.AsSpan(commentCountOffset, 4),
                0);
            RewritePageChecksums(sanitized);
            return Task.FromResult(new SanitizedAudio(sanitized, "audio/ogg", "ogg-opus-tags-v1"));
        }
        catch
        {
            Array.Clear(sanitized);
            throw;
        }
    }

    private static void RewritePageChecksums(Span<byte> content)
    {
        var offset = 0;
        while (offset < content.Length)
        {
            if (content.Length - offset < 27
                || !content.Slice(offset, 4).SequenceEqual("OggS"u8))
            {
                throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
            }

            var segmentCount = content[offset + 26];
            if (content.Length - offset < 27 + segmentCount)
            {
                throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
            }

            var bodyLength = 0;
            for (var index = 0; index < segmentCount; index++)
            {
                bodyLength += content[offset + 27 + index];
            }

            var pageLength = 27 + segmentCount + bodyLength;
            if (content.Length - offset < pageLength)
            {
                throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
            }

            content.Slice(offset + 22, 4).Clear();
            var checksum = ComputeOggCrc(content.Slice(offset, pageLength));
            BinaryPrimitives.WriteUInt32LittleEndian(content.Slice(offset + 22, 4), checksum);
            offset += pageLength;
        }
    }

    private static uint ComputeOggCrc(ReadOnlySpan<byte> content)
    {
        uint crc = 0;
        foreach (var value in content)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80000000) != 0
                    ? crc << 1 ^ 0x04C11DB7
                    : crc << 1;
            }
        }

        return crc;
    }
}
