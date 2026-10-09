using System.Buffers.Binary;
using System.Collections;
using Algara.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using SkiaSharp;

namespace Algara.UnitTests.Services;

public sealed class ProductImageStorageTests : IDisposable
{
    private readonly string _testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Algara.ProductImageStorage.Tests"));
    private readonly string _root;
    private readonly ProductImageStorage _storage;

    public ProductImageStorageTests()
    {
        _root = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.WebRootPath).Returns(_root);
        _storage = new ProductImageStorage(environment.Object);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, ".jpg")]
    [InlineData(SKEncodedImageFormat.Png, ".png")]
    [InlineData(SKEncodedImageFormat.Webp, ".webp")]
    public async Task Prepare_DecodesRealFormatsAndUsesContentInsteadOfUntrustedFilename(
        SKEncodedImageFormat format, string expectedExtension)
    {
        var prepared = await _storage.PrepareAsync([File(Image(format), "../../unsafe.aspx")]);

        var image = Assert.Single(prepared);
        Assert.Equal(expectedExtension, image.Extension);
        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.False(Directory.Exists(_root));
        var urls = await _storage.SaveAsync(prepared);
        var url = Assert.Single(urls);
        Assert.Matches("^/uploads/products/[0-9a-f]{32}\\" + expectedExtension + "$", url);
        using var saved = SKCodec.Create(PathFor(url));
        Assert.Equal(format, saved.EncodedFormat);
        Assert.Equal(SKCodecResult.Success, saved.GetPixels(out _));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("MZ executable content")]
    public async Task Prepare_RejectsNonImagesDespiteJpegNameAndMimeType(string contents)
    {
        await Assert.ThrowsAsync<ProductImageValidationException>(() =>
            _storage.PrepareAsync([File(System.Text.Encoding.UTF8.GetBytes(contents), "photo.jpg")]));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Prepare_RejectsUnsupportedRealImageFormat()
    {
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        await Assert.ThrowsAsync<ProductImageValidationException>(() => _storage.PrepareAsync([File(gif)]));
    }

    [Fact]
    public async Task Prepare_RejectsTruncatedImageWithRealSignature()
    {
        var jpeg = Image(SKEncodedImageFormat.Jpeg);
        await Assert.ThrowsAsync<ProductImageValidationException>(() =>
            _storage.PrepareAsync([File(jpeg[..(jpeg.Length / 2)])]));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Prepare_InvalidLaterFileDoesNotWriteEarlierFiles()
    {
        await Assert.ThrowsAsync<ProductImageValidationException>(() =>
            _storage.PrepareAsync([File(Image(SKEncodedImageFormat.Png)), File([1, 2, 3])]));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Prepare_RejectsActualBytesAboveLimitWhenReportedLengthIsSmall()
    {
        var stream = new CountingStream(new byte[ProductImageStorage.MaxImageBytes + 10_000]);
        var file = new Mock<IFormFile>();
        file.SetupGet(value => value.Length).Returns(1);
        file.Setup(value => value.OpenReadStream()).Returns(stream);

        await Assert.ThrowsAsync<ProductImageValidationException>(() => _storage.PrepareAsync([file.Object]));

        Assert.Equal(ProductImageStorage.MaxImageBytes + 1, stream.BytesRead);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Prepare_RejectsClaimedOversizeBeforeOpeningStream()
    {
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        file.SetupGet(value => value.Length).Returns(ProductImageStorage.MaxImageBytes + 1);
        await Assert.ThrowsAsync<ProductImageValidationException>(() => _storage.PrepareAsync([file.Object]));
        file.Verify(value => value.OpenReadStream(), Times.Never);
    }

    [Fact]
    public async Task Prepare_RejectsEmptyFile()
    {
        await Assert.ThrowsAsync<ProductImageValidationException>(() => _storage.PrepareAsync([File([])]));
    }

    [Fact]
    public async Task Prepare_RejectsTooManyFilesBeforeReadingThem()
    {
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        var files = Enumerable.Repeat(file.Object, ProductImageStorage.MaxImagesPerBatch + 1).ToArray();
        await Assert.ThrowsAsync<ProductImageValidationException>(() => _storage.PrepareAsync(files));
        file.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(10001, 1)]
    [InlineData(5000, 5000)]
    public async Task Prepare_RejectsExcessiveDimensionsBeforeDecodingPixels(int width, int height)
    {
        var png = Image(SKEncodedImageFormat.Png);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), Crc32(png.AsSpan(12, 17)));
        var exception = await Assert.ThrowsAsync<ProductImageValidationException>(() => _storage.PrepareAsync([File(png)]));
        Assert.Contains("24", exception.Message);
    }

    [Fact]
    public async Task Prepare_ReencodesImagesAndRemovesAppendedData()
    {
        var marker = System.Text.Encoding.UTF8.GetBytes("<script>unsafe-upload-marker</script>");
        var uploaded = Image(SKEncodedImageFormat.Png).Concat(marker).ToArray();
        var urls = await _storage.SaveAsync(await _storage.PrepareAsync([File(uploaded)]));
        var saved = await System.IO.File.ReadAllBytesAsync(PathFor(Assert.Single(urls)));
        Assert.DoesNotContain("unsafe-upload-marker", System.Text.Encoding.UTF8.GetString(saved));
        using var codec = SKCodec.Create(new MemoryStream(saved));
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(out _));
    }

    [Fact]
    public async Task Prepare_AppliesExifOrientationBeforeRemovingMetadata()
    {
        var jpeg = Image(SKEncodedImageFormat.Jpeg);
        byte[] orientation = [
            0xff, 0xe1, 0x00, 0x22, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
            0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var withExif = jpeg.Take(2).Concat(orientation).Concat(jpeg.Skip(2)).ToArray();
        var prepared = await _storage.PrepareAsync([File(withExif)]);
        var image = Assert.Single(prepared);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        var urls = await _storage.SaveAsync(prepared);
        using var saved = SKCodec.Create(PathFor(Assert.Single(urls)));
        Assert.Equal(SKEncodedOrigin.TopLeft, saved.EncodedOrigin);
    }

    [Fact]
    public async Task PrepareAndSave_EmptyBatchDoNotCreateDirectory()
    {
        Assert.Empty(await _storage.SaveAsync(await _storage.PrepareAsync([])));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task PrepareAndSave_HonorCancellationBeforeReadingOrWriting()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _storage.PrepareAsync([], cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _storage.SaveAsync([], cancellation.Token));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Save_CancellationAfterFirstFileRollsBackItsFiles()
    {
        var prepared = await _storage.PrepareAsync([File(Image(SKEncodedImageFormat.Png)), File(Image(SKEncodedImageFormat.Png))]);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _storage.SaveAsync(new CancellingImages(prepared, cancellation), cancellation.Token));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "uploads", "products")));
    }

    [Fact]
    public async Task Rollback_DeletesOnlyFilesCreatedBySameInstance()
    {
        var prepared = await _storage.PrepareAsync([File(Image(SKEncodedImageFormat.Png))]);
        var url = Assert.Single(await _storage.SaveAsync(prepared));
        var existingName = Guid.NewGuid().ToString("N") + ".png";
        var existingUrl = ProductImageStorage.UrlPrefix + existingName;
        var existingPath = PathFor(existingUrl);
        await System.IO.File.WriteAllTextAsync(existingPath, "existing protected image");
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.WebRootPath).Returns(_root);
        var otherRequest = new ProductImageStorage(environment.Object);

        await otherRequest.DeleteNewFilesAsync([url]);
        Assert.True(System.IO.File.Exists(PathFor(url)));
        await _storage.DeleteNewFilesAsync(["../../outside.jpg", existingUrl, existingPath, url, url]);

        Assert.False(System.IO.File.Exists(PathFor(url)));
        Assert.True(System.IO.File.Exists(existingPath));
    }

    private string PathFor(string url) => Path.Combine(_root, "uploads", "products", Path.GetFileName(url));

    private static IFormFile File(byte[] bytes, string name = "photo.jpg") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "NewImages", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

    private static byte[] Image(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(2, 3);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~crc;
    }

    public void Dispose()
    {
        var resolvedRoot = Path.GetFullPath(_root);
        if (resolvedRoot.StartsWith(_testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(resolvedRoot))
            Directory.Delete(resolvedRoot, true);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }

    private sealed class CancellingImages(IReadOnlyList<PreparedProductImage> images, CancellationTokenSource cancellation)
        : IReadOnlyList<PreparedProductImage>
    {
        public PreparedProductImage this[int index] => images[index];
        public int Count => images.Count;
        public IEnumerator<PreparedProductImage> GetEnumerator()
        {
            yield return images[0];
            cancellation.Cancel();
            yield return images[1];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
