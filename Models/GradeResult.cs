namespace ChamDiemGrader.Models;

/// <summary>Kết quả chấm một bài.</summary>
public sealed class GradeResult
{
    public required string FileName { get; init; }
    public required bool HopLe { get; init; }
    public string? LyDoKhongHopLe { get; init; }
    public double? TongDiem { get; init; }
    /// <summary>Tổng phần Nội dung (tối đa 6).</summary>
    public double? DiemNoiDung { get; init; }
    /// <summary>Tổng phần Hình thức (tối đa 4).</summary>
    public double? DiemHinhThuc { get; init; }
    public string? PhanLoai { get; init; }
    public string? GhiChu { get; init; }
    /// <summary>Hiển thị cột tác giả / tác phẩm trong bảng tổng hợp.</summary>
    public string? TenTacGiaTacPham { get; init; }
    /// <summary>Nhận xét nổi bật (cột cuối bảng tổng hợp).</summary>
    public string? NhanXetNoiBat { get; init; }
    /// <summary>Văn bản nhiều dòng: từng hạng mục — điểm chấm — lý do.</summary>
    public string? ChiTietDiemVaLyDo { get; init; }
    /// <summary>Nhận xét mở rộng cho bài có điểm lớn hơn 8.5.</summary>
    public string? NhanXetDatTu85 { get; init; }
    public string? RawModelJson { get; init; }
}
