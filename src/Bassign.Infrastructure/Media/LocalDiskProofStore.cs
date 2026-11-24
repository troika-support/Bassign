using Bassign.Application.Media;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Media;

/// <summary>Writes photos atomically (temp file + move) named by the device's operation id.</summary>
public sealed class LocalDiskProofStore(IOptions<MediaOptions> options) : IProofStore
{
    private readonly string _root = options.Value.ResolveProofRoot();

    public async Task<string> SaveAsync(Guid id, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var reference = $"{id:N}.{(contentType == "image/png" ? "png" : "jpg")}";
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, reference);
        var temp = path + ".part";
        await using (var file = File.Create(temp))
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        File.Move(temp, path, overwrite: true);
        return reference;
    }

    public Task<ProofFile?> OpenAsync(string reference, CancellationToken cancellationToken = default)
    {
        // The reference format is validated, so it can never escape the root folder.
        if (!ProofReferences.IsValid(reference))
            return Task.FromResult<ProofFile?>(null);

        var path = Path.Combine(_root, reference);
        if (!File.Exists(path))
            return Task.FromResult<ProofFile?>(null);

        var contentType = reference.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg";
        return Task.FromResult<ProofFile?>(new ProofFile(File.OpenRead(path), contentType));
    }
}
