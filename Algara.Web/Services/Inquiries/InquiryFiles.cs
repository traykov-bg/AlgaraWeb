using System.Text;
using System.Text.RegularExpressions;
using Algara.Data.Models;
namespace Algara.Web.Services.Inquiries;
public sealed class InquiryFiles(ProductImageStorage images)
{
    public const int MaxFiles = 5, MaxBytes = 5 * 1024 * 1024;
    public async Task<List<InquiryAttachment>> PrepareAsync(IReadOnlyList<IFormFile> files, CancellationToken ct)
    {
        if (files.Count > MaxFiles) throw new ProductImageValidationException("Files");
        var result = new List<InquiryAttachment>();
        foreach (var file in files)
        {
            if (file.Length is <= 0 or > MaxBytes) throw new ProductImageValidationException("Files");
            if (Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await using var input = file.OpenReadStream();
                using var output = new MemoryStream();
                var buffer = new byte[81920]; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    if (output.Length + read > MaxBytes) throw new ProductImageValidationException("Files");
                    output.Write(buffer, 0, read);
                }
                var bytes = output.ToArray();
                if (!LooksLikePdf(bytes)) throw new ProductImageValidationException("Files");
                // Opaque, untrusted downloads; never rendered or executed on the server.
                result.Add(new() { Contents = bytes, FileName = $"sketch-{result.Count + 1}.pdf" });
            }
            else
            {
                var prepared = (await images.PrepareAsync(new[] { file }, ct))[0];
                result.Add(new() { Contents = prepared.Bytes, FileName = $"photo-{result.Count + 1}{prepared.Extension}" });
            }
        }
        return result;
    }
    public static bool LooksLikePdf(byte[] bytes)
    {
        if (bytes.Length < 32 || bytes.Length > MaxBytes) return false;
        var header = Encoding.ASCII.GetString(bytes, 0, 8);
        var tail = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(bytes.Length, 1024)).TrimEnd();
        if (!(header.StartsWith("%PDF-1.") && header[7] is >= '0' and <= '7') && header != "%PDF-2.0") return false;
        var end = Regex.Match(tail, @"startxref\s+([0-9]+)\s+%%EOF$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!end.Success || !int.TryParse(end.Groups[1].Value, out var offset) || offset < 8 || offset >= bytes.Length - 10) return false;
        var reference = Encoding.ASCII.GetString(bytes, offset, Math.Min(100, bytes.Length - offset));
        // Both traditional cross-reference tables and PDF cross-reference stream objects are supported.
        return reference.StartsWith("xref", StringComparison.Ordinal) ||
            Regex.IsMatch(reference, @"^[0-9]+\s+[0-9]+\s+obj\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
