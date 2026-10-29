using System.Globalization;
using ClosedXML.Excel;
using PhotoCapture.Host.Data;
using PhotoCapture.Host.Services;

namespace PhotoCapture.Host;

public static class ExcelExport
{
    const int MaxRowsWithImages = 1500;
    static readonly XLColor Accent = XLColor.FromHtml("#00008B");
    static readonly XLColor Ink = XLColor.FromHtml("#FFFFFF");

    public static string MapsUrl(Photo p) =>
        string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps?q={p.Latitude:0.######},{p.Longitude:0.######}");

    static string MapsUrl(double lat, double lon) =>
        string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps?q={lat:0.######},{lon:0.######}");

    static readonly Dictionary<string, string> TaskStatusLabels = new()
    {
        ["timed_in"] = "Timed in",
        ["at_client"] = "At client",
        ["job_saved"] = "Job saved",
        ["complete"] = "Complete",
    };

    static string ReviewLabel(FieldTask t) => t.ReviewStatus switch
    {
        "approved" => "Approved",
        "rejected" => "Rejected",
        _ => t.Status == "complete" ? "Awaiting review" : "Not submitted",
    };

    /// <summary>Blank stays blank: a missing leg must not read as 0,0 (a real coordinate in the Gulf of Guinea).</summary>
    static void Text(IXLCell cell, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        cell.Value = value.Length > 32000 ? value[..32000] : value;
    }

    static void Coord(IXLCell cell, double? value)
    {
        if (value is null) return;
        cell.Value = value.Value;
        cell.Style.NumberFormat.Format = "0.000000";
    }

    static void Map(IXLCell cell, double? lat, double? lon)
    {
        if (lat is null || lon is null) return;
        cell.Value = "Open map";
        cell.SetHyperlink(new XLHyperlink(MapsUrl(lat.Value, lon.Value)));
        cell.Style.Font.FontColor = XLColor.Blue;
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
    }

    static void Leg(IXLWorksheet ws, int r, int c, DateOnly? date, TimeOnly? time, string? address, double? lat, double? lon)
    {
        if (date is not null)
        {
            ws.Cell(r, c).Value = date.Value.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, c).Style.DateFormat.Format = "yyyy-mm-dd";
        }
        if (time is not null)
        {
            ws.Cell(r, c + 1).Value = time.Value.ToTimeSpan();
            ws.Cell(r, c + 1).Style.DateFormat.Format = "hh:mm:ss";
        }
        Text(ws.Cell(r, c + 2), address);
        Coord(ws.Cell(r, c + 3), lat);
        Coord(ws.Cell(r, c + 4), lon);
        Map(ws.Cell(r, c + 5), lat, lon);
    }

    /// <summary>Data only: the owner asked for no photos in the task export. Photo counts come from the caller so this class stays free of data access.</summary>
    public static Task<byte[]> BuildTasksAsync(SettingsStore store, List<FieldTask> rows, Dictionary<Guid, int> photoCounts, CancellationToken ct)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Tasks");

        string[] headers =
        [
            "Status", "Review status", "Review reason", "Reviewed at", "Reviewed by", "Technician", "Client", "Job type",
            "Time in date", "Time in time", "Time in address", "Time in latitude", "Time in longitude", "Time in map",
            "Client date", "Client time", "Client address", "Client latitude", "Client longitude", "Client map",
            "Time out date", "Time out time", "Time out address", "Time out latitude", "Time out longitude", "Time out map",
            "Old IMEI", "New IMEI", "Old SIM", "New SIM", "Plate number", "Note", "Photos", "Task id",
        ];
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

        var header = ws.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = Ink;
        header.Style.Fill.BackgroundColor = Accent;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 24;

        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = rows[i];
            var r = i + 2;
            Text(ws.Cell(r, 1), TaskStatusLabels.TryGetValue(t.Status ?? "", out var label) ? label : t.Status);
            Text(ws.Cell(r, 2), ReviewLabel(t));
            Text(ws.Cell(r, 3), t.ReviewReason);
            if (t.ReviewedAt is not null)
            {
                ws.Cell(r, 4).Value = t.ReviewedAt.Value.LocalDateTime;
                ws.Cell(r, 4).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            }
            Text(ws.Cell(r, 5), t.ReviewedBy);
            Text(ws.Cell(r, 6), t.TechnicianName);
            Text(ws.Cell(r, 7), t.Client);
            Text(ws.Cell(r, 8), t.JobType);
            Leg(ws, r, 9, t.TimeInDate, t.TimeInTime, t.TimeInAddress, t.TimeInLatitude, t.TimeInLongitude);
            Leg(ws, r, 15, t.ClientDate, t.ClientTime, t.ClientAddress, t.ClientLatitude, t.ClientLongitude);
            Leg(ws, r, 21, t.TimeOutDate, t.TimeOutTime, t.TimeOutAddress, t.TimeOutLatitude, t.TimeOutLongitude);
            Text(ws.Cell(r, 27), t.OldImei);
            Text(ws.Cell(r, 28), t.NewImei);
            Text(ws.Cell(r, 29), t.OldSim);
            Text(ws.Cell(r, 30), t.NewSim);
            Text(ws.Cell(r, 31), t.PlateNumber);
            Text(ws.Cell(r, 32), t.Note);
            ws.Cell(r, 33).Value = photoCounts.TryGetValue(t.Id, out var n) ? n : 0;
            ws.Cell(r, 33).Style.NumberFormat.Format = "0";
            Text(ws.Cell(r, 34), t.Id.ToString());
        }

        var body = ws.Range(1, 1, Math.Max(2, rows.Count + 1), headers.Length);
        body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        body.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        ws.Column(3).Style.Alignment.WrapText = true;
        ws.Column(32).Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, Math.Max(1, rows.Count + 1), headers.Length).SetAutoFilter();

        double[] widths = [12, 15, 34, 19, 16, 22, 22, 20, 12, 10, 40, 14, 14, 11, 12, 10, 40, 14, 14, 11, 12, 10, 40, 14, 14, 11, 18, 18, 18, 18, 14, 40, 8, 38];
        for (var c = 0; c < headers.Length; c++) ws.Column(c + 1).Width = widths[c];

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    public static async Task<byte[]> BuildAsync(SettingsStore store, List<Photo> rows, bool withImages, CancellationToken ct)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Photos");
        withImages &= rows.Count <= MaxRowsWithImages;

        var headers = new List<string> { "Date", "Time", "Name", "Client", "Activity", "Note", "Address", "Latitude", "Longitude", "Accuracy (m)", "Map", "Photo file" };
        if (withImages) headers.Insert(0, "Photo");
        for (var c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = headers[c];

        var header = ws.Range(1, 1, 1, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = Ink;
        header.Style.Fill.BackgroundColor = Accent;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 24;

        var offset = withImages ? 1 : 0;
        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = rows[i];
            var r = i + 2;
            ws.Cell(r, 1 + offset).Value = p.CaptureDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 1 + offset).Style.DateFormat.Format = "yyyy-mm-dd";
            ws.Cell(r, 2 + offset).Value = p.CaptureTime.ToTimeSpan();
            ws.Cell(r, 2 + offset).Style.DateFormat.Format = "hh:mm:ss";
            ws.Cell(r, 3 + offset).Value = p.Name;
            ws.Cell(r, 4 + offset).Value = p.Client ?? "";
            ws.Cell(r, 5 + offset).Value = p.Activity ?? "";
            ws.Cell(r, 6 + offset).Value = p.Note ?? "";
            ws.Cell(r, 7 + offset).Value = p.Address ?? "";
            ws.Cell(r, 8 + offset).Value = p.Latitude;
            ws.Cell(r, 9 + offset).Value = p.Longitude;
            ws.Cell(r, 8 + offset).Style.NumberFormat.Format = "0.000000";
            ws.Cell(r, 9 + offset).Style.NumberFormat.Format = "0.000000";
            ws.Cell(r, 10 + offset).Value = Math.Round(p.Accuracy, 1);
            var map = ws.Cell(r, 11 + offset);
            map.Value = "Open map";
            map.SetHyperlink(new XLHyperlink(MapsUrl(p)));
            map.Style.Font.FontColor = XLColor.Blue;
            map.Style.Font.Underline = XLFontUnderlineValues.Single;

            var full = Path.GetFullPath(Path.Combine(store.PhotosDir, p.ImagePath));
            var fileCell = ws.Cell(r, 12 + offset);
            fileCell.Value = full;
            if (File.Exists(full)) fileCell.SetHyperlink(new XLHyperlink(new Uri(full).AbsoluteUri));

            if (withImages)
            {
                ws.Row(r).Height = 62;
                var thumb = File.Exists(full) ? await Thumbnails.GetAsync(store, p.Id, full, ct) : null;
                if (thumb is not null)
                {
                    try
                    {
                        using var img = System.Drawing.Image.FromFile(thumb);
                        var scale = Math.Min(80.0 / img.Width, 78.0 / img.Height);
                        ws.AddPicture(thumb).MoveTo(ws.Cell(r, 1)).Scale(scale);
                    }
                    catch (Exception ex) when (ex is IOException or OutOfMemoryException or ArgumentException) { }
                }
            }
        }

        var body = ws.Range(1, 1, Math.Max(2, rows.Count + 1), headers.Count);
        body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        body.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        ws.Column(6 + offset).Style.Alignment.WrapText = true;
        ws.Column(7 + offset).Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, Math.Max(1, rows.Count + 1), headers.Count).SetAutoFilter();

        double[] widths = [12, 10, 22, 22, 20, 40, 40, 12, 12, 12, 12, 50];
        for (var c = 0; c < headers.Count - offset; c++) ws.Column(c + 1 + offset).Width = widths[c];
        if (withImages) ws.Column(1).Width = 14;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
