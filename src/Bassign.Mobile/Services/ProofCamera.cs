using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace Bassign.Mobile.Services;

public interface IProofCamera
{
    /// <summary>Takes a proof photo and returns a compressed local JPEG path, or null if cancelled/unavailable.</summary>
    Task<string?> CaptureAsync();
}

/// <summary>
/// Camera → upright (EXIF orientation applied) → max 1600px → JPEG 70% → metadata stripped, before it's queued.
/// A typical phone photo goes from several MB to ~200–400 KB (data cost matters on mobile plans).
/// Metadata is stripped on purpose: a proof photo must not silently record where the staff member stood (GPS).
/// </summary>
public sealed class ProofCamera : IProofCamera
{
    private static readonly MediaPickerOptions Options = new()
    {
        Title = "Proof of work",
        RotateImage = true,          // apply the EXIF orientation flag so photos are never sideways
        MaximumWidth = 1600,
        MaximumHeight = 1600,
        CompressionQuality = 70,
        PreserveMetaData = false     // drop EXIF (incl. GPS location)
    };

    public async Task<string?> CaptureAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
                return null;

            var permission = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Camera>());
            if (permission != PermissionStatus.Granted)
                return null;

            var photo = await MainThread.InvokeOnMainThreadAsync(() => MediaPicker.Default.CapturePhotoAsync(Options));
            if (photo is null)
                return null;

            // The processed file sits in the cache folder (the OS may clear it); keep our own copy until it's synced.
            var folder = Path.Combine(FileSystem.AppDataDirectory, "proof");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, $"{Guid.NewGuid():N}.jpg");

            await using (var source = await photo.OpenReadAsync())
            await using (var output = File.Create(target))
            {
                await source.CopyToAsync(output);
            }
            return target;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException or IOException)
        {
            return null;
        }
    }
}
