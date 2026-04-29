using ClosedXML.Excel;
using ChamDiemGrader.Models;

namespace ChamDiemGrader.Services;

public static class ExcelReportWriter
{
    /// <summary>Chỉ xuất sheet TomTat theo yêu cầu.</summary>
    public static void Save(string path, IReadOnlyList<GradeResult> rows)
    {
        TraceLogger.Write("ExcelReportWriter.Save START path=" + path + " rows=" + rows.Count);
        using var wb = new XLWorkbook();

        var summary = wb.Worksheets.Add("TomTat");
        summary.Cell(1, 1).Value = "Tên file";
        summary.Cell(1, 2).Value = "Hợp lệ";
        summary.Cell(1, 3).Value = "Điểm ND (6)";
        summary.Cell(1, 4).Value = "Điểm HT (4)";
        summary.Cell(1, 5).Value = "Tổng (10)";
        summary.Cell(1, 6).Value = "Tác giả / Tác phẩm";
        summary.Cell(1, 7).Value = "Nhận xét nổi bật";
        summary.Cell(1, 8).Value = "Chi tiết chấm (I.1–II.4)";
        summary.Cell(1, 9).Value = "Nhận xét chi tiết (>8.5)";
        summary.Range(1, 1, 1, 9).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            summary.Cell(r, 1).Value = row.FileName;
            summary.Cell(r, 2).Value = row.HopLe ? "Có" : "Không";
            if (row.HopLe && row.DiemNoiDung.HasValue)
                summary.Cell(r, 3).Value = Math.Round(row.DiemNoiDung.Value, 1);
            else
                summary.Cell(r, 3).Value = row.HopLe ? "" : "—";

            if (row.HopLe && row.DiemHinhThuc.HasValue)
                summary.Cell(r, 4).Value = Math.Round(row.DiemHinhThuc.Value, 1);
            else
                summary.Cell(r, 4).Value = row.HopLe ? "" : "—";

            if (row.HopLe && row.TongDiem.HasValue)
                summary.Cell(r, 5).Value = Math.Round(row.TongDiem.Value, 1);
            else if (!row.HopLe)
                summary.Cell(r, 5).Value = "Không hợp lệ";
            else
                summary.Cell(r, 5).Value = "";

            summary.Cell(r, 6).Value = row.TenTacGiaTacPham ?? "";
            summary.Cell(r, 7).Value = row.NhanXetNoiBat ?? row.GhiChu ?? "";
            summary.Cell(r, 8).Value = row.ChiTietDiemVaLyDo ?? "";
            summary.Cell(r, 8).Style.Alignment.WrapText = true;
            summary.Cell(r, 9).Value = row.NhanXetDatTu85 ?? "";
            summary.Cell(r, 9).Style.Alignment.WrapText = true;
            r++;
        }

        summary.Columns(1, 5).AdjustToContents();
        summary.Column(6).Width = 28;
        summary.Column(7).Width = 40;
        summary.Column(8).Width = 70;
        summary.Column(9).Width = 50;

        wb.SaveAs(path);
        TraceLogger.Write("ExcelReportWriter.Save DONE");
    }
}


