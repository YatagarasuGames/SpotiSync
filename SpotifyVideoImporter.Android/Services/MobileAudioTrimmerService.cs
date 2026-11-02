using System;
using System.IO;
using System.Threading.Tasks;
using TagLib;

namespace SpotifyVideoImporter.Android.Services;

public class MobileAudioTrimmerService
{
    private static readonly int[] BitratesV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
    private static readonly int[] SampleRatesV1 = { 44100, 48000, 32000, 0 };

    /// <summary>
    /// Losslessly trims an MP3 file by slicing frames between startSeconds and endSeconds.
    /// </summary>
    public static async Task TrimMp3LosslessAsync(string inputMp3, string outputMp3, double startSeconds, double endSeconds)
    {
        if (!System.IO.File.Exists(inputMp3))
            throw new FileNotFoundException("Input audio file not found", inputMp3);

        await Task.Run(() =>
        {
            using var inStream = System.IO.File.OpenRead(inputMp3);
            using var outStream = System.IO.File.Create(outputMp3);

            // Skip ID3v2 tag if present at start
            var header = new byte[10];
            if (inStream.Read(header, 0, 10) == 10)
            {
                if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                {
                    // ID3v2 tag length is syncsafe integer at bytes 6-9
                    int tagSize = ((header[6] & 0x7F) << 21) |
                                  ((header[7] & 0x7F) << 14) |
                                  ((header[8] & 0x7F) << 7) |
                                  (header[9] & 0x7F);
                    inStream.Position = 10 + tagSize;
                }
                else
                {
                    inStream.Position = 0;
                }
            }

            double currentSec = 0.0;
            var buffer = new byte[4096];

            while (inStream.Position < inStream.Length - 4)
            {
                int b1 = inStream.ReadByte();
                if (b1 == -1) break;

                // Sync word: 0xFF followed by 0xEx or 0xFx
                if (b1 == 0xFF)
                {
                    int b2 = inStream.ReadByte();
                    if (b2 == -1) break;

                    if ((b2 & 0xE0) == 0xE0)
                    {
                        int b3 = inStream.ReadByte();
                        int b4 = inStream.ReadByte();
                        if (b3 == -1 || b4 == -1) break;

                        int version = (b2 >> 3) & 0x03; // 3 = MPEG 1
                        int layer = (b2 >> 1) & 0x03;   // 1 = Layer III
                        int bitrateIndex = (b3 >> 4) & 0x0F;
                        int sampleRateIndex = (b3 >> 2) & 0x03;
                        int padding = (b3 >> 1) & 0x01;

                        if (version == 3 && layer == 1 && bitrateIndex > 0 && bitrateIndex < 15 && sampleRateIndex < 3)
                        {
                            int bitrate = BitratesV1L3[bitrateIndex] * 1000;
                            int sampleRate = SampleRatesV1[sampleRateIndex];
                            int frameSize = (144 * bitrate) / sampleRate + padding;
                            double frameDuration = 1152.0 / sampleRate;

                            int frameRemaining = frameSize - 4;
                            if (frameRemaining > 0 && inStream.Position + frameRemaining <= inStream.Length)
                            {
                                if (currentSec >= startSeconds && currentSec <= endSeconds)
                                {
                                    // Write frame header
                                    outStream.WriteByte((byte)b1);
                                    outStream.WriteByte((byte)b2);
                                    outStream.WriteByte((byte)b3);
                                    outStream.WriteByte((byte)b4);

                                    // Write frame payload
                                    int readBytes = inStream.Read(buffer, 0, frameRemaining);
                                    outStream.Write(buffer, 0, readBytes);
                                }
                                else
                                {
                                    inStream.Position += frameRemaining;
                                }

                                currentSec += frameDuration;
                                if (currentSec > endSeconds)
                                {
                                    break;
                                }
                                continue;
                            }
                        }
                    }
                }
            }
        });
    }

    /// <summary>
    /// Writes ID3v2.3 metadata (Title, Artist, Album, Artwork) to MP3 file.
    /// </summary>
    public static async Task ApplyTagsAsync(string mp3Path, string title, string artist, string album, byte[]? artworkBytes)
    {
        await Task.Run(() =>
        {
            try
            {
                using var tagFile = TagLib.File.Create(mp3Path);
                tagFile.Tag.Title = title.Trim();
                tagFile.Tag.Performers = new[] { artist.Trim() };
                tagFile.Tag.Album = string.IsNullOrWhiteSpace(album) ? "Spotify Import" : album.Trim();

                if (artworkBytes != null && artworkBytes.Length > 0)
                {
                    var pic = new TagLib.Picture(new ByteVector(artworkBytes))
                    {
                        Type = PictureType.FrontCover,
                        Description = "Cover",
                        MimeType = "image/jpeg"
                    };
                    tagFile.Tag.Pictures = new IPicture[] { pic };
                }

                tagFile.Save();
            }
            catch { }
        });
    }
}
