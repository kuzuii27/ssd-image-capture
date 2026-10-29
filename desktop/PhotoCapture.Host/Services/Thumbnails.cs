using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PhotoCapture.Host.Services;

/// <summary>Small JPEG previews cached under the data folder so the admin table loads quickly.</summary>
public static class Thumbnails
{
    const int Width = 240;
    static readonly SemaphoreSlim Gate = new(2);

    public static async Task<string?> GetAsync(SettingsStore store, Guid id, string imageFullPath, CancellationToken ct)
    {
        var thumb = Path.Combine(store.ThumbsDir, $"{id:N}.jpg");
        if (File.Exists(thumb)) return thumb;
        await Gate.WaitAsync(ct);
        try
        {
            if (File.Exists(thumb)) return thumb;
            return await Task.Run(() => Create(imageFullPath, thumb), ct);
        }
        finally { Gate.Release(); }
    }

    static string? Create(string source, string target)
    {
        try
        {
            using var img = Image.FromFile(source);
            ApplyExifOrientation(img);
            var scale = Math.Min(1.0, (double)Width / img.Width);
            var w = Math.Max(1, (int)Math.Round(img.Width * scale));
            var h = Math.Max(1, (int)Math.Round(img.Height * scale));
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(img, 0, 0, w, h);
            }
            var tmp = target + ".tmp";
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using (var p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(Encoder.Quality, 80L);
                bmp.Save(tmp, codec, p);
            }
            File.Move(tmp, target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or OutOfMemoryException or ExternalException or ArgumentException)
        {
            return null; // the caller falls back to the full image
        }
    }

    static void ApplyExifOrientation(Image img)
    {
        const int orientationTag = 0x0112;
        if (!img.PropertyIdList.Contains(orientationTag)) return;
        var value = img.GetPropertyItem(orientationTag)?.Value;
        if (value is not { Length: >= 2 }) return;
        var flip = value[0] switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };
        if (flip != RotateFlipType.RotateNoneFlipNone) img.RotateFlip(flip);
    }
}
