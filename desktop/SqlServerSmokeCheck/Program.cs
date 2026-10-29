using PhotoCapture.Host;
using PhotoCapture.Host.Services;

var source = new SqlServerPhotoSource("Server=(localdb)\\MSSQLLocalDB;Database=SSD_mobileCamera;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5");
var all = await source.GetPhotosAsync(new PhotoFilter(null, null, null, null, null), CancellationToken.None);
if (all.Count != 17) throw new Exception($"Expected 17 rows, received {all.Count}.");
var needle = all[0].Name[..Math.Min(2, all[0].Name.Length)];
var filtered = await source.GetPhotosAsync(new PhotoFilter(null, null, needle, null, null), CancellationToken.None);
if (filtered.Count == 0 || filtered.Any(p => !p.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
    throw new Exception("Parameterized name filter failed.");
var exactDate = all[0].CaptureDate;
var byDate = await source.GetPhotosAsync(new PhotoFilter(exactDate, exactDate, null, null, null), CancellationToken.None);
if (byDate.Count == 0 || byDate.Any(p => p.CaptureDate != exactDate)) throw new Exception("Date range filter failed.");
Console.WriteLine($"PASS: SQL Server returned {all.Count} rows; name filter {filtered.Count}; date filter {byDate.Count}.");
