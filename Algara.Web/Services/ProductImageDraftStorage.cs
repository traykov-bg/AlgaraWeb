using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Algara.Web.Services;

/// <summary>
/// New-product images stay outside wwwroot until the product is saved. Draft paths
/// are owner-bound; file locks serialize mutations across processes. Committed
/// files are copies, so expiring a draft can never remove a product image.
/// </summary>
public sealed class ProductImageDraftStorage(IWebHostEnvironment environment, string owner)
{
    private const string ManifestName = "draft.json";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);
    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "product-image-drafts");
    private string OwnerDirectory => Path.Combine(Root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))));

    public async Task<string> CreateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new UnauthorizedAccessException();
        CleanupExpired();
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(OwnerDirectory, id);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, ManifestName),
            JsonSerializer.Serialize(new Manifest()), cancellationToken);
        return id;
    }

    public async Task<DraftLease> OpenAsync(string? draftId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner) || !Guid.TryParseExact(draftId, "N", out _))
            throw InvalidDraft();
        var directory = Path.Combine(OwnerDirectory, draftId!);
        if (!Directory.Exists(directory)) throw InvalidDraft();
        var started = DateTime.UtcNow;
        FileStream? handle = null;
        while (handle == null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { handle = new FileStream(Path.Combine(directory, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Directory.Exists(directory) && DateTime.UtcNow - started < TimeSpan.FromSeconds(15))
            { await Task.Delay(50, cancellationToken); }
        }
        try
        {
            var path = Path.Combine(directory, ManifestName);
            var manifest = File.Exists(path)
                ? JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(path, cancellationToken)) : null;
            if (manifest == null || manifest.UpdatedAt + Lifetime < DateTime.UtcNow || manifest.Closed ||
                manifest.Images.Any(id => !IsUploadId(id))) throw InvalidDraft();
            var lease = new DraftLease(directory, manifest, handle);
            await lease.TouchAsync(cancellationToken);
            return lease;
        }
        catch { await handle.DisposeAsync(); throw; }
    }

    private void CleanupExpired()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var ownerDirectory in Directory.EnumerateDirectories(Root))
        foreach (var directory in Directory.EnumerateDirectories(ownerDirectory))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            var manifest = Path.Combine(directory, ManifestName);
            if (IsRecent(directory, manifest)) continue;
            // Cleanup is opportunistic. An active preview/upload/save owns the lock.
            try
            {
                using (new FileStream(Path.Combine(directory, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    if (IsRecent(directory, manifest)) continue;
                    foreach (var path in Directory.EnumerateFiles(directory))
                        if (Path.GetFileName(path) != "write.lock") File.Delete(path);
                }
                File.Delete(Path.Combine(directory, "write.lock"));
                Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsRecent(string directory, string manifest)
        => (File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest) : Directory.GetCreationTimeUtc(directory)) + Lifetime >= DateTime.UtcNow;

    private static bool IsUploadId(string id)
        => Guid.TryParseExact(Path.GetFileNameWithoutExtension(id), "N", out _) &&
           Path.GetFileName(id) == id && Path.GetExtension(id) is ".jpg" or ".png" or ".webp";
    private static ProductImageValidationException InvalidDraft()
        => new("Качените снимки за новия продукт са изтекли или не са достъпни. Презаредете страницата и ги качете отново.");

    public sealed class Manifest
    {
        public List<string> Images { get; set; } = [];
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool Closed { get; set; }
    }

    public sealed class DraftLease(string directory, Manifest manifest, FileStream handle) : IAsyncDisposable
    {
        public IReadOnlyList<string> Images => manifest.Images;

        internal async Task TouchAsync(CancellationToken cancellationToken)
        {
            manifest.UpdatedAt = DateTime.UtcNow;
            var temp = Path.Combine(directory, "draft.tmp");
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(manifest), cancellationToken);
            File.Move(temp, Path.Combine(directory, ManifestName), true);
        }

        public async Task<IReadOnlyList<string>> AddAsync(IReadOnlyList<PreparedProductImage> images, CancellationToken cancellationToken)
        {
            if (manifest.Images.Count + images.Count > ProductImageStorage.MaxImagesPerBatch)
                throw new ProductImageValidationException("Галерията може да съдържа най-много 20 снимки.");
            var added = new List<string>();
            try
            {
                foreach (var image in images)
                {
                    var id = Guid.NewGuid().ToString("N") + image.Extension;
                    added.Add(id);
                    await File.WriteAllBytesAsync(Path.Combine(directory, id), image.Bytes, cancellationToken);
                }
                manifest.Images.AddRange(added);
                await TouchAsync(cancellationToken);
                return added;
            }
            catch
            {
                foreach (var id in added) { manifest.Images.Remove(id); File.Delete(Path.Combine(directory, id)); }
                throw;
            }
        }

        public async Task DeleteAsync(string? uploadId, CancellationToken cancellationToken)
        {
            if (uploadId == null || !manifest.Images.Remove(uploadId)) throw InvalidDraft();
            await TouchAsync(cancellationToken);
            // Membership is the delete operation. A locked private file can be
            // collected on expiry; never report a failed deletion after removing it.
            try { File.Delete(Path.Combine(directory, uploadId)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public async Task<byte[]> ReadAsync(string? uploadId, CancellationToken cancellationToken)
        {
            if (uploadId == null || !manifest.Images.Contains(uploadId)) throw InvalidDraft();
            return await File.ReadAllBytesAsync(Path.Combine(directory, uploadId), cancellationToken);
        }

        public async Task<IReadOnlyList<PreparedProductImage>> PrepareAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
        {
            var result = new List<PreparedProductImage>();
            foreach (var id in ids)
                result.Add(new PreparedProductImage(await ReadAsync(id, cancellationToken), Path.GetExtension(id), 0, 0));
            return result;
        }

        public async Task CompleteAsync()
        {
            manifest.Closed = true;
            await TouchAsync(CancellationToken.None);
            foreach (var id in manifest.Images) File.Delete(Path.Combine(directory, id));
        }

        public ValueTask DisposeAsync() => handle.DisposeAsync();
    }
}
