using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Bassign.Mobile.Services;

public interface IProofCamera
{
    /// <summary>Takes a proof photo and returns a compressed local JPEG path, or null if cancelled/unavailable.</summary>
    Task<string?> CaptureAsync();
}

/// <summary>Camera → downscale to 1600px longest edge → JPEG at 70% quality, before it's queued.
/// A typical phone photo goes from several MB to ~200–400 KB (data cost matters on mobile plans).</summary>
public sealed class ProofCamera : IProofCamera
{
    private const float MaxEdge = 1600f;
    private const float Quality = 0.7f;

    public async Task<string?> CaptureAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
                return null;

            var permission = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Camera>());
            if (permission != PermissionStatus.Granted)
                return null;

            var photo = await MainThread.InvokeOnMainThreadAsync(() => MediaPicker.Default.CapturePhotoAsync());
            if (photo is null)
                return null;

            var folder = Path.Combine(FileSystem.AppDataDirectory, "proof");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, $"{Guid.NewGuid():N}.jpg");

            await using var source = await photo.OpenReadAsync();
            IImage image = PlatformImage.FromStream(source, ImageFormat.Jpeg);
            try
            {
                if (image.Width > MaxEdge || image.Height > MaxEdge)
                    image = image.Downsize(MaxEdge, disposeOriginal: true);

                await using var output = File.Create(target);
                await image.SaveAsync(output, ImageFormat.Jpeg, Quality);
            }
            finally
            {
                image.Dispose();
            }
            return target;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException or IOException)
        {
            return null;
        }
    }
}
