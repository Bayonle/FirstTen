using System.Buffers.Binary;
using System.Security.Cryptography;
using Concentus;
using Concentus.Oggfile;

namespace First10.Infrastructure.Modules.Intake.OpenAI;

public sealed class PreparedOpenAiAudio(byte[] bytes, string contentType) : IDisposable
{
    private byte[]? _bytes = bytes;

    public ReadOnlyMemory<byte> Bytes => _bytes is null
        ? throw new ObjectDisposedException(nameof(PreparedOpenAiAudio))
        : _bytes;

    public string ContentType { get; } = contentType;

    public void Dispose()
    {
        if (_bytes is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_bytes);
        _bytes = null;
    }
}

public sealed class OpenAiAudioPreparer
{
    private const int SampleRate = 48_000;
    private const int Channels = 1;
    private const int MaximumSeconds = 120;
    private const int MaximumSamples = SampleRate * Channels * MaximumSeconds;

    public PreparedOpenAiAudio Prepare(ReadOnlyMemory<byte> safeAudio, string contentType)
    {
        if (safeAudio.IsEmpty)
        {
            throw new OpenAiProviderException("transcription_audio_size_rejected");
        }

        if (contentType != "audio/ogg")
        {
            return new PreparedOpenAiAudio(safeAudio.ToArray(), contentType);
        }

        var source = safeAudio.ToArray();
        try
        {
            using var input = new MemoryStream(source, writable: false);
            using var decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
            var reader = new OpusOggReadStream(decoder, input);
            if (!reader.HasNextPacket)
            {
                throw new OpenAiProviderException("transcription_audio_decode_rejected");
            }

            using var pcm = new MemoryStream();
            pcm.Position = 44;
            var sampleCount = 0;
            Span<byte> encoded = stackalloc byte[2];
            while (reader.HasNextPacket)
            {
                var samples = reader.DecodeNextPacket();
                if (samples is null)
                {
                    if (!reader.HasNextPacket && sampleCount > 0)
                    {
                        break;
                    }

                    throw new OpenAiProviderException("transcription_audio_decode_rejected");
                }

                sampleCount = checked(sampleCount + samples.Length);
                if (sampleCount > MaximumSamples)
                {
                    throw new OpenAiProviderException("transcription_audio_duration_rejected");
                }

                foreach (var sample in samples)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(encoded, sample);
                    pcm.Write(encoded);
                }
            }

            if (sampleCount == 0)
            {
                throw new OpenAiProviderException("transcription_audio_decode_rejected");
            }

            WriteWaveHeader(pcm, checked(sampleCount * sizeof(short)));
            return new PreparedOpenAiAudio(pcm.ToArray(), "audio/wav");
        }
        catch (OpenAiProviderException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OpenAiProviderException("transcription_audio_decode_rejected", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(source);
        }
    }

    private static void WriteWaveHeader(Stream output, int dataLength)
    {
        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], checked(36 + dataLength));
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], SampleRate * Channels * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], Channels * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataLength);
        output.Position = 0;
        output.Write(header);
        output.Position = output.Length;
    }
}
