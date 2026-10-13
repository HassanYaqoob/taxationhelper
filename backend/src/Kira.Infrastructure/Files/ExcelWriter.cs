using ClosedXML.Excel;
using Kira.Application.Common;

namespace Kira.Infrastructure.Files;

/// <summary>Builds formatted .xlsx workbooks: bold titles and headers, number formats, frozen headers, sensible widths.</summary>
public sealed class ExcelWriter : IExcelWriter
{
    static readonly string[] BoldStarts = ["Total", "Net", "Chargeable", "Estimated", "Adjusted", "Closing"];

    public byte[] Build(IEnumerable<Sheet> sheets)
    {
        using var wb = new XLWorkbook();
        foreach (var s in sheets)
        {
            var ws = wb.Worksheets.Add(s.Name.Length > 31 ? s.Name[..31] : s.Name);
            for (var r = 0; r < s.Rows.Count; r++)
            {
                var row = s.Rows[r];
                for (var c = 0; c < row.Length; c++)
                {
                    var cell = ws.Cell(r + 1, c + 1);
                    switch (row[c])
                    {
                        case null: break;
                        case decimal d: cell.Value = (double)d; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                        case int i: cell.Value = i; break;
                        case DateOnly dt: cell.Value = dt.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "dd/MM/yyyy"; break;
                        default: cell.Value = row[c]!.ToString(); break;
                    }
                }
                var first = row.Length > 0 ? row[0] as string : null;
                if (r == 0) { ws.Row(1).Style.Font.Bold = true; ws.Row(1).Style.Font.FontSize = 14; }
                else if (s.HeaderRows.Contains(r))
                {
                    ws.Row(r + 1).Style.Font.Bold = true;
                    ws.Row(r + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E4F2EF");
                }
                else if (first != null && BoldStarts.Any(first.StartsWith)) ws.Row(r + 1).Style.Font.Bold = true;
            }
            for (var c = 0; c < s.Widths.Length; c++) ws.Column(c + 1).Width = s.Widths[c];
            if (s.HeaderRows.Length > 0) ws.SheetView.FreezeRows(s.HeaderRows[0] + 1);
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
