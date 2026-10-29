using PhotoCapture.Host.Services;

namespace PhotoCapture.Host.Services;

public sealed class SettingsStorePhotoStorage(SettingsStore store)
{
    public string PhotosDir => store.PhotosDir;
}
