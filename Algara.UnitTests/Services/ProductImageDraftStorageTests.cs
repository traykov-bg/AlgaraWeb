using Algara.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using SkiaSharp;

namespace Algara.UnitTests.Services;

public sealed class ProductImageDraftStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Algara.Draft.Tests", Guid.NewGuid().ToString("N"));
    private readonly IWebHostEnvironment _environment;
    public ProductImageDraftStorageTests()
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(_root);
        environment.SetupGet(e => e.WebRootPath).Returns(Path.Combine(_root, "wwwroot"));
        _environment = environment.Object;
    }

    [Fact]
    public async Task Draft_IsPrivateAndBoundToOwner()
    {
        var storage = new ProductImageDraftStorage(_environment, "first");
        var id = await storage.CreateAsync(default);
        await using (var draft = await storage.OpenAsync(id, default))
        {
            var upload = Assert.Single(await draft.AddAsync(await Images(), default));
            Assert.NotEmpty(await draft.ReadAsync(upload, default));
        }
        Assert.False(Directory.Exists(_environment.WebRootPath));
        var other = new ProductImageDraftStorage(_environment, "second");
        await Assert.ThrowsAsync<ProductImageValidationException>(() => other.OpenAsync(id, default));
    }

    [Fact]
    public async Task ClosedDraft_RejectsReplayAfterProductSave()
    {
        var storage = new ProductImageDraftStorage(_environment, "owner");
        var id = await storage.CreateAsync(default);
        await using (var draft = await storage.OpenAsync(id, default))
        {
            await draft.AddAsync(await Images(), default);
            await draft.CompleteAsync();
        }
        await Assert.ThrowsAsync<ProductImageValidationException>(() => storage.OpenAsync(id, default));
        Assert.Empty(Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DeleteLockedPrivateFile_RemovesMembershipSuccessfully()
    {
        var storage = new ProductImageDraftStorage(_environment, "owner");
        var id = await storage.CreateAsync(default);
        await using var draft = await storage.OpenAsync(id, default);
        var upload = Assert.Single(await draft.AddAsync(await Images(), default));
        var path = Assert.Single(Directory.GetFiles(_root, upload, SearchOption.AllDirectories));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            await draft.DeleteAsync(upload, default);
        Assert.Empty(draft.Images);
        await Assert.ThrowsAsync<ProductImageValidationException>(() => draft.ReadAsync(upload, default));
    }

    [Fact]
    public async Task Cleanup_DoesNotRemoveFreshDirectoryBeforeManifestCreation()
    {
        var pending = Path.Combine(_root, "App_Data", "product-image-drafts", "owner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        await new ProductImageDraftStorage(_environment, "other").CreateAsync(default);
        Assert.True(Directory.Exists(pending));
    }

    [Fact]
    public async Task Cleanup_RemovesExpiredDraftOnlyAndLeavesPublishedFiles()
    {
        var storage = new ProductImageDraftStorage(_environment, "owner");
        var id = await storage.CreateAsync(default);
        string upload;
        await using (var draft = await storage.OpenAsync(id, default))
            upload = Assert.Single(await draft.AddAsync(await Images(), default));
        var manifest = Assert.Single(Directory.GetFiles(_root, "draft.json", SearchOption.AllDirectories));
        File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow.AddDays(-2));
        Directory.CreateDirectory(_environment.WebRootPath);
        var published = Path.Combine(_environment.WebRootPath, upload);
        await File.WriteAllTextAsync(published, "committed copy");
        await storage.CreateAsync(default);
        Assert.False(Directory.Exists(Path.GetDirectoryName(manifest)));
        Assert.True(File.Exists(published));
    }

    [Fact]
    public async Task BatchAboveGalleryLimit_IsRejectedBeforeExtraFilesAreWritten()
    {
        var storage = new ProductImageDraftStorage(_environment, "owner");
        var id = await storage.CreateAsync(default);
        await using var draft = await storage.OpenAsync(id, default);
        var image = Assert.Single(await Images());
        await draft.AddAsync(Enumerable.Repeat(image, 20).ToArray(), default);
        await Assert.ThrowsAsync<ProductImageValidationException>(() => draft.AddAsync([image], default));
        Assert.Equal(20, draft.Images.Count);
        Assert.Equal(20, Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories).Length);
    }

    private async Task<IReadOnlyList<PreparedProductImage>> Images()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        var bytes = data.ToArray();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", "photo.png");
        return await new ProductImageStorage(_environment).PrepareAsync([file]);
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(_root);
        var intended = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Algara.Draft.Tests")) + Path.DirectorySeparatorChar;
        if (root.StartsWith(intended, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root)) Directory.Delete(root, true);
    }
}