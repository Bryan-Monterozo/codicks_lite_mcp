using System.Text;

namespace LocalAgent.Infrastructure.Files;

internal static class TextFileCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static PreparedTextResult PrepareCreate(
        string content,
        long maxBytes)
    {
        if (content is null)
        {
            return PreparedTextResult.Fail("Text content is required.");
        }

        var lineEnding = DetectLineEnding(content);
        if (lineEnding == TextLineEnding.Mixed)
        {
            return PreparedTextResult.Fail(
                "Mixed line endings are not supported. Use one consistent line-ending style.");
        }

        try
        {
            var bytes = StrictUtf8.GetBytes(content);
            if (bytes.LongLength > maxBytes)
            {
                return PreparedTextResult.Fail(
                    $"Content exceeds the configured MaxEditableFileBytes limit of {maxBytes} bytes.");
            }

            return PreparedTextResult.Ok(
                bytes,
                "utf-8",
                ToDisplayName(lineEnding));
        }
        catch (EncoderFallbackException)
        {
            return PreparedTextResult.Fail("Content cannot be encoded as strict UTF-8.");
        }
    }

    public static PreparedTextResult PrepareUpdate(
        byte[] existingBytes,
        string newContent,
        long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(existingBytes);

        if (newContent is null)
        {
            return PreparedTextResult.Fail("Text content is required.");
        }

        var decoded = DecodeExisting(existingBytes);
        if (!decoded.Success)
        {
            return PreparedTextResult.Fail(decoded.Message);
        }

        var lineEnding = DetectLineEnding(decoded.Content);
        if (lineEnding == TextLineEnding.Mixed)
        {
            return PreparedTextResult.Fail(
                "The existing file uses mixed line endings, which Chunk 05 will not rewrite automatically.");
        }

        var normalizedContent = NormalizeToLf(newContent);
        var preservedContent = ApplyLineEnding(normalizedContent, lineEnding);

        try
        {
            var contentBytes = StrictUtf8.GetBytes(preservedContent);
            byte[] outputBytes;

            if (decoded.HasUtf8Bom)
            {
                var preamble = Encoding.UTF8.GetPreamble();
                outputBytes = new byte[preamble.Length + contentBytes.Length];
                Buffer.BlockCopy(preamble, 0, outputBytes, 0, preamble.Length);
                Buffer.BlockCopy(contentBytes, 0, outputBytes, preamble.Length, contentBytes.Length);
            }
            else
            {
                outputBytes = contentBytes;
            }

            if (outputBytes.LongLength > maxBytes)
            {
                return PreparedTextResult.Fail(
                    $"Content exceeds the configured MaxEditableFileBytes limit of {maxBytes} bytes.");
            }

            return PreparedTextResult.Ok(
                outputBytes,
                decoded.HasUtf8Bom ? "utf-8-bom" : "utf-8",
                ToDisplayName(lineEnding));
        }
        catch (EncoderFallbackException)
        {
            return PreparedTextResult.Fail("Content cannot be encoded as strict UTF-8.");
        }
    }

    internal static DecodedTextResult DecodeExisting(byte[] bytes)
    {
        if (bytes.Length >= 2 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xFE)
        {
            return DecodedTextResult.Fail("UTF-16 LE is not supported; use UTF-8 text.");
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0xFE &&
            bytes[1] == 0xFF)
        {
            return DecodedTextResult.Fail("UTF-16 BE is not supported; use UTF-8 text.");
        }

        var hasBom =
            bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF;

        var offset = hasBom ? 3 : 0;

        try
        {
            var content = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            if (content.Contains('\0'))
            {
                return DecodedTextResult.Fail("Binary/NUL-containing files are not supported for text updates.");
            }

            return DecodedTextResult.Ok(content, hasBom);
        }
        catch (DecoderFallbackException)
        {
            return DecodedTextResult.Fail("The existing file is not valid UTF-8 text.");
        }
    }

    private static TextLineEnding DetectLineEnding(string content)
    {
        var hasCrLf = content.Contains("\r\n", StringComparison.Ordinal);
        var withoutCrLf = content.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        var hasLf = withoutCrLf.Contains('\n');
        var hasCr = withoutCrLf.Contains('\r');

        var styles = 0;
        if (hasCrLf)
        {
            styles++;
        }

        if (hasLf)
        {
            styles++;
        }

        if (hasCr)
        {
            styles++;
        }

        if (styles > 1)
        {
            return TextLineEnding.Mixed;
        }

        if (hasCrLf)
        {
            return TextLineEnding.CrLf;
        }

        if (hasCr)
        {
            return TextLineEnding.Cr;
        }

        if (hasLf)
        {
            return TextLineEnding.Lf;
        }

        return TextLineEnding.None;
    }

    private static string NormalizeToLf(string content) =>
        content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string ApplyLineEnding(
        string normalizedContent,
        TextLineEnding lineEnding) =>
        lineEnding switch
        {
            TextLineEnding.CrLf => normalizedContent.Replace("\n", "\r\n", StringComparison.Ordinal),
            TextLineEnding.Cr => normalizedContent.Replace('\n', '\r'),
            _ => normalizedContent
        };

    private static string ToDisplayName(TextLineEnding lineEnding) =>
        lineEnding switch
        {
            TextLineEnding.CrLf => "crlf",
            TextLineEnding.Cr => "cr",
            TextLineEnding.Lf => "lf",
            TextLineEnding.None => "none",
            _ => "mixed"
        };

    private enum TextLineEnding
    {
        None,
        Lf,
        CrLf,
        Cr,
        Mixed
    }

    internal sealed record PreparedTextResult(
        bool Success,
        byte[] Bytes,
        string Encoding,
        string LineEnding,
        string Message)
    {
        public static PreparedTextResult Ok(
            byte[] bytes,
            string encoding,
            string lineEnding) =>
            new(true, bytes, encoding, lineEnding, string.Empty);

        public static PreparedTextResult Fail(string message) =>
            new(false, [], string.Empty, string.Empty, message);
    }

    internal sealed record DecodedTextResult(
        bool Success,
        string Content,
        bool HasUtf8Bom,
        string Message)
    {
        public static DecodedTextResult Ok(string content, bool hasUtf8Bom) =>
            new(true, content, hasUtf8Bom, string.Empty);

        public static DecodedTextResult Fail(string message) =>
            new(false, string.Empty, false, message);
    }
}
