using System.Buffers;
using SkiaSharp;

namespace Algara.Web.Services;

public sealed class ProductImageValidationException(string message) : Exception(message);

public sealed class PreparedProductImage
{
    internal PreparedProductImage(byte[] bytes, string extension, int width, int height)
    {
        Bytes = bytes;
        Extension = extension;
        Width = width;
        Height = height;
    }

    internal byte[] Bytes { get; }
    public string Extension { get; }
    public int Width { get; }
    public int Height { get; }
}

/// <summary>Validates the whole batch before creating files and tracks only this request's new files for rollback.</summary>
public sealed class ProductImageStorage(IWebHostEnvironment environment)
{
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public const int MaxImagesPerBatch = 20;
    public const int MaxDimension = 10_000;
    public const long MaxPixels = 24_000_000;
    public const string UrlPrefix = "/uploads/products/";
    private readonly Dictionary<string, string> _newFiles = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<PreparedProductImage>> PrepareAsync(
        IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        cancellationToken.ThrowIfCancellationRequested();
        if (files.Count > MaxImagesPerBatch)
            throw new ProductImageValidationException("Може да качите най-много 20 снимки наведнъж.");

        var result = new List<PreparedProductImage>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Length > MaxImageBytes)
                throw new ProductImageValidationException("Всяка снимка трябва да е до 5 MB.");
            await using var input = file.OpenReadStream();
            var bytes = await ReadBoundedAsync(input, cancellationToken);
            result.Add(DecodeAndNormalize(bytes, cancellationToken));
        }
        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<string>> SaveAsync(
        IReadOnlyList<PreparedProductImage> images, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(images);
        cancellationToken.ThrowIfCancellationRequested();
        if (images.Count == 0)
            return Array.Empty<string>();
        if (images.Count > MaxImagesPerBatch)
            throw new ProductImageValidationException("Може да качите най-много 20 снимки наведнъж.");
        if (string.IsNullOrWhiteSpace(environment.WebRootPath))
            throw new InvalidOperationException("A web root is required for product image storage.");

        var directory = Path.GetFullPath(Path.Combine(environment.WebRootPath, "uploads", "products"));
        Directory.CreateDirectory(directory);
        var urls = new List<string>(images.Count);
        try
        {
            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Guid.NewGuid().ToString("N") + image.Extension;
                var path = Path.Combine(directory, fileName);
                var url = UrlPrefix + fileName;
                // CreateNew prevents replacement, even in the extraordinarily unlikely case of a name collision.
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous);
                _newFiles.Add(url, path);
                urls.Add(url);
                await output.WriteAsync(image.Bytes, cancellationToken);
            }
            return urls.AsReadOnly();
        }
        catch
        {
            await DeleteNewFilesAsync(urls);
            throw;
        }
    }

    /// <summary>Only URLs created by this storage instance can be deleted; posted or legacy paths are ignored.</summary>
    public Task DeleteNewFilesAsync(IEnumerable<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        foreach (var url in urls)
        {
            if (!_newFiles.TryGetValue(url, out var path))
                continue;
            File.Delete(path);
            _newFiles.Remove(url);
        }
        return Task.CompletedTask;
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                // Read one byte beyond the limit to reject a dishonest Length without buffering unlimited data.
                var requested = (int)Math.Min(chunk.Length, MaxImageBytes + 1L - buffer.Length);
                var count = await input.ReadAsync(chunk.AsMemory(0, requested), cancellationToken);
                if (count == 0)
                    break;
                if (buffer.Length + count > MaxImageBytes)
                    throw new ProductImageValidationException("Всяка снимка трябва да е до 5 MB.");
                buffer.Write(chunk, 0, count);
            }
            if (buffer.Length == 0)
                throw new ProductImageValidationException("Избраният файл е празен.");
            return buffer.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private static PreparedProductImage DecodeAndNormalize(byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not
            (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw new ProductImageValidationException("Изберете валидна снимка във формат JPEG, PNG или WebP.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension ||
            (long)info.Width * info.Height > MaxPixels)
            throw new ProductImageValidationException("Снимката трябва да е до 24 мегапиксела и до 10 000 пиксела на страна.");
        if (codec.FrameCount > 1)
            throw new ProductImageValidationException("Качете неподвижна снимка; анимираните изображения не се поддържат.");

        var decodedInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodedInfo);
        if (codec.GetPixels(decodedInfo, decoded.GetPixels()) != SKCodecResult.Success)
            throw new ProductImageValidationException("Снимката е повредена или непълна. Изберете друг файл.");
        cancellationToken.ThrowIfCancellationRequested();

        // Apply EXIF orientation before removing metadata, so phone photos remain the right way up.
        using var oriented = Orient(decoded, codec.EncodedOrigin);
        using var normalized = SKImage.FromBitmap(oriented);
        using var encoded = normalized.Encode(codec.EncodedFormat, 90);
        if (encoded is null)
            throw new ProductImageValidationException("Снимката не може да бъде обработена. Изберете друг файл.");
        if (encoded.Size > MaxImageBytes)
            throw new ProductImageValidationException("Обработената снимка надвишава 5 MB. Изберете снимка с по-малки размери.");
        cancellationToken.ThrowIfCancellationRequested();
        var extension = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ".jpg",
            SKEncodedImageFormat.Png => ".png",
            _ => ".webp"
        };
        return new PreparedProductImage(encoded.ToArray(), extension, oriented.Width, oriented.Height);
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
            SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swapsAxes ? source.Height : source.Width,
            swapsAxes ? source.Width : source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var width = source.Width;
        var height = source.Height;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
            _ => SKMatrix.Identity
        };
        using var canvas = new SKCanvas(result);
        canvas.SetMatrix(in matrix);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return result;
    }
}
