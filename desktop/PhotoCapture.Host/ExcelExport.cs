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
