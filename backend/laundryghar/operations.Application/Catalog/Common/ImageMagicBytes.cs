namespace operations.Application.Catalog.Common;

/// <summary>
/// Magic-byte validation for uploaded images.
/// Checks the first 12 bytes of the stream against JPEG, PNG, and WebP signatures.
/// Internal to the Catalog slice — does NOT depend on the Logistics slice.
/// </summary>
internal static class ImageMagicBytes
{
    /// <summary>
    /// Returns true when the stream starts with a recognised image magic-byte sequence.
    /// The stream position is reset to 0 after the check.
    /// </summary>
    internal static bool IsValidImage(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        stream.Position = 0;
        var read = stream.Read(header);
        stream.Position = 0;   // reset so the handler can re-read from the beginning

        if (read < 3) return false;

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return true;

        // WebP: RIFF????WEBP  (bytes 0-3 = 52 49 46 46, bytes 8-11 = 57 45 42 50)
        if (read >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return true;

        return false;
    }
}
