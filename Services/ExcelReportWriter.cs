using ClosedXML.Excel;
using ChamDiemGrader.Models;

namespace ChamDiemGrader.Services;

public static class ExcelReportWriter
{
    /// <summary>Chỉ xuất sheet TomTat theo yêu cầu.</summary>
    public static void Save(string path, IReadOnlyList<GradeResult> rows)
    {
        using var wb = new XLWorkbook();

        var summary = wb.Worksheets.Add("TomTat");
        summary.Cell(1, 1).Value = "Tên file";
        summary.Cell(1, 2).Value = "Điểm";
        summary.Cell(1, 3).Value = "Chi tiết chấm (từng hạng mục)";
        summary.Cell(1, 4).Value = "Nhận xét chi tiết (>8.5)";
        summary.Range(1, 1, 1, 4).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            summary.Cell(r, 1).Value = row.FileName;
            if (row.HopLe && row.TongDiem.HasValue)
                summary.Cell(r, 2).Value = Math.Round(row.TongDiem.Value, 1);
            else if (!row.HopLe)
                summary.Cell(r, 2).Value = "Không hợp lệ";
            else
                summary.Cell(r, 2).Value = "";

            summary.Cell(r, 3).Value = row.ChiTietDiemVaLyDo ?? "";
            summary.Cell(r, 3).Style.Alignment.WrapText = true;
            summary.Cell(r, 4).Value = row.NhanXetDatTu85 ?? "";
            summary.Cell(r, 4).Style.Alignment.WrapText = true;
            r++;
        }

        summary.Columns(1, 2).AdjustToContents();
        summary.Column(3).Width = 70;
        summary.Column(4).Width = 90;

        wb.SaveAs(path);
    }
}


