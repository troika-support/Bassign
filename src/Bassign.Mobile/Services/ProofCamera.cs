using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace Bassign.Mobile.Services;

/// <summary>Path = the compressed photo; Problem = why there isn't one (null when the user just cancelled).</summary>
public sealed record CaptureResult(string? Path, string? Problem);

public interface IProofCamera
{
    /// <summary>Never throws — in a Blazor Hybrid app an escaping exception closes the whole app.</summary>
    Task<CaptureResult> CaptureAsync();
}

/// <summary>
/// Camera → upright (EXIF orientation applied) → max 1600px → JPEG 70% → metadata stripped, before it's queued.
/// A typical phone photo goes from several MB to ~200–400 KB. Metadata (incl. GPS location) is dropped on purpose.
/// </summary>
public sealed class ProofCamera : IProofCamera
{
    private static readonly MediaPickerOptions Options = new()
    {
        Title = "Proof of work",
        RotateImage = true,
        MaximumWidth = 1600,
        MaximumHeight = 1600,
        CompressionQuality = 70,
        PreserveMetaData = false
    };

    public async Task<CaptureResult> CaptureAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
                return new CaptureResult(null, "This device has no camera available.");

            var permission = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Camera>());
            if (permission != PermissionStatus.Granted)
                return new CaptureResult(null, "Camera access is off. Allow it in Settings → Apps → Bassign → Permissions.");

            var photo = await MainThread.InvokeOnMainThreadAsync(() => MediaPicker.Default.CapturePhotoAsync(Options));
            if (photo is null)
                return new CaptureResult(null, null);   // cancelled

            // The processed file sits in the cache folder (the OS may clear it); keep our own copy until it's synced.
            var folder = Path.Combine(FileSystem.AppDataDirectory, "proof");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, $"{Guid.NewGuid():N}.jpg");

            await using (var source = await photo.OpenReadAsync())
            await using (var output = File.Create(target))
            {
                await source.CopyToAsync(output);
            }
            return new CaptureResult(target, null);
        }
        catch (OperationCanceledException)
        {
            return new CaptureResult(null, null);
        }
        catch (FeatureNotSupportedException)
        {
            return new CaptureResult(null, "No camera app is available on this device.");
        }
        catch (PermissionException)
        {
            return new CaptureResult(null, "Camera access is off. Allow it in Settings → Apps → Bassign → Permissions.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bassign] Photo capture failed: {ex}");
#if DEBUG
            return new CaptureResult(null, $"Couldn't take the photo — {ex.GetType().Name}: {ex.Message}");
#else
            return new CaptureResult(null, "Couldn't take the photo. Please try again.");
#endif
        }
    }
}
