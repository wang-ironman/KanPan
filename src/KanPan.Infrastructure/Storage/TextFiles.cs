using System.Text;
using KanPan.Infrastructure.Quotes;

namespace KanPan.Infrastructure.Storage;

public static class TextFiles
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Decodes as UTF-8 when valid, otherwise as GBK (what Excel writes for CSV on Chinese Windows).</summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return HttpQuoteProvider.Gbk.GetString(bytes);
        }
    }

    public static string ReadAutoDetect(string path) => Decode(File.ReadAllBytes(path));
}
