namespace QuanLyNghiPhep.Models;

public enum UserRole
{
    TrungDoi,
    PhongQLHV,
    BoMon,
    Admin
}

public static class RoleLabels
{
    public static string Display(UserRole role) => role switch
    {
        UserRole.TrungDoi => "Chủ nhiệm lớp",
        UserRole.PhongQLHV => "Phòng Quản lý học viên",
        UserRole.BoMon => "Giáo viên bộ môn",
        UserRole.Admin => "Quản trị viên",
        _ => "Không xác định"
    };
}

public static class LeaveCategories
{
    public const string CuoiTuan = "CuoiTuan";
    public const string LeTet = "LeTet";
    public const string TranhThu = "TranhThu";

    public static string DefaultWeekendAddress { get; set; } = "Phường Hà Đông, Thành phố Hà Nội";
    public static string PlatoonPrefix { get; set; } = "Trung đội";
    public static int TranhThuMonthlyLimit { get; set; } = 3;
    public const string TagCuoiTuan = "Nghỉ phép cuối tuần";
    public const string TagTranhThu = "Nghỉ phép tranh thủ";
}

public static class LeaveStatuses
{
    public const string WaitingTrungDoi = "Chờ duyệt trung đội";
    public const string WaitingPhong = "Chờ duyệt Phòng QLHV";
    public const string Approved = "Đã duyệt";
    public const string Rejected = "Từ chối";
    public const string Cancelled = "Đã hủy";
}

public sealed class ClassGroup
{
    public string TenLop { get; set; } = "";
    public string ManagerUserId { get; set; } = "";
    public string NguoiTao { get; set; } = "";
    public DateTime NgayTao { get; set; } = DateTime.Now;
}

public sealed class Student
{
    public bool IsActive { get; set; } = true;
    public int STT
    {
        get; set;
    }
    public string MaHocVien { get; set; } = "";
    public string HoTen { get; set; } = "";
    public string Lop { get; set; } = "";
    public string NgaySinh { get; set; } = "";
    public string SoDienThoai { get; set; } = "";
    public string TrungDoi { get; set; } = "";

    public Student Clone() => (Student)MemberwiseClone();
}

public sealed class LeaveRequest
{
    public int Id
    {
        get; set;
    }
    public string MaDon { get; set; } = "";
    public string MaHocVien { get; set; } = "";
    public string HoTen { get; set; } = "";
    public string Lop { get; set; } = "";
    public string TrungDoi { get; set; } = "";
    public DateTime TuThoiDiem
    {
        get; set;
    }
    public DateTime DenThoiDiem
    {
        get; set;
    }
    public string LyDo { get; set; } = "";
    public string DiaChiNoiNghi { get; set; } = "";
    public string TrangThai { get; set; } = LeaveStatuses.Approved;
    public int? SubjectId
    {
        get; set;
    }

    // Phân loại nghỉ & hệ thống Tag ẩn theo yêu cầu
    public string PhanLoaiNghi { get; set; } = LeaveCategories.TranhThu; // CuoiTuan | LeTet | TranhThu
    public string TagLoaiNghi { get; set; } = LeaveCategories.TagTranhThu; // Tag 1: Nghỉ phép cuối tuần | Nghỉ Tết / Nghỉ lễ 30-4 1-5... | Nghỉ phép tranh thủ
    public string TagMonHoc { get; set; } = ""; // Tag 2: Tên môn học (chỉ có khi Nghỉ phép tranh thủ, có thể nhiều môn cách nhau dấu phẩy)

    public string NguoiDuyetTrungDoi { get; set; } = "";
    public DateTime? NgayDuyetTrungDoi
    {
        get; set;
    }
    public string NguoiDuyetPhong { get; set; } = "";
    public DateTime? NgayDuyetPhong
    {
        get; set;
    }
    public DateTime NgayTao
    {
        get; set;
    }
    public string NguoiTao { get; set; } = "";
    public string NguoiDuyet { get; set; } = "";
    public DateTime? NgayDuyet
    {
        get; set;
    }
    public string GhiChuDuyet { get; set; } = "";

    public string LoaiDon => TuThoiDiem.Date == DenThoiDiem.Date ? "Trong ngày" : "Dài ngày";
    public double SoGio => Math.Max(0, (DenThoiDiem - TuThoiDiem).TotalHours);
    public double SoNgay => Math.Round(SoGio / 24.0, 2);

    // Hiển thị cột Môn học theo đúng quy tắc:
    // Nghỉ cuối tuần -> "Nghỉ cuối tuần"
    // Nghỉ lễ/tết -> "Nghỉ lễ/Tết"
    // Nghỉ tranh thủ -> Tag môn học đã nhập
    public string HienThiMonHoc => PhanLoaiNghi switch
    {
        LeaveCategories.CuoiTuan => "Nghỉ cuối tuần",
        LeaveCategories.LeTet => "Nghỉ lễ/Tết",
        _ => string.IsNullOrWhiteSpace(TagMonHoc) ? "Chưa gắn môn" : TagMonHoc
    };

    public List<string> DanhSachTagMon => PhanLoaiNghi == LeaveCategories.TranhThu && !string.IsNullOrWhiteSpace(TagMonHoc)
        ? TagMonHoc.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
        : new List<string>();

    public LeaveRequest Clone() => (LeaveRequest)MemberwiseClone();
}

public sealed class BatchLeaveDraftItem
{
    public int STT
    {
        get; set;
    }
    public string MaHocVien { get; set; } = "";
    public string HoTen { get; set; } = "";
    public string Lop { get; set; } = "";
    public string DiaChiNoiNghi { get; set; } = LeaveCategories.DefaultWeekendAddress;
    public DateTime TuThoiDiem
    {
        get; set;
    }
    public DateTime DenThoiDiem
    {
        get; set;
    }
    public bool IsEditingAddress
    {
        get; set;
    }
}

public sealed class TranhThuLeaveInput
{
    public string TenHocVien { get; set; } = "";
    public string MaHocVien { get; set; } = "";
    public string Lop { get; set; } = "";
    public string LyDo { get; set; } = "";
    public string DiaChiNoiNghi { get; set; } = LeaveCategories.DefaultWeekendAddress;
    public DateTime TuThoiDiem { get; set; } = DateTime.Today.AddHours(7);
    public DateTime DenThoiDiem { get; set; } = DateTime.Today.AddHours(17);
    public string MonHoc { get; set; } = "";
    public int? SubjectId
    {
        get; set;
    }
}

public sealed class LeaveInput
{
    public string MaHocVien { get; set; } = "";
    public string TrungDoi { get; set; } = "";
    public DateTime TuThoiDiem { get; set; } = DateTime.Today.AddHours(7);
    public DateTime DenThoiDiem { get; set; } = DateTime.Today.AddHours(17);
    public string LyDo { get; set; } = "";
    public string DiaChiNoiNghi { get; set; } = LeaveCategories.DefaultWeekendAddress;
    public string PhanLoaiNghi { get; set; } = LeaveCategories.TranhThu;
    public string TagLoaiNghi { get; set; } = LeaveCategories.TagTranhThu;
    public string TagMonHoc { get; set; } = "";

    public static LeaveInput From(LeaveRequest item) => new()
    {
        MaHocVien = item.MaHocVien,
        TrungDoi = item.TrungDoi,
        TuThoiDiem = item.TuThoiDiem,
        DenThoiDiem = item.DenThoiDiem,
        LyDo = item.LyDo,
        DiaChiNoiNghi = item.DiaChiNoiNghi,
        PhanLoaiNghi = item.PhanLoaiNghi,
        TagLoaiNghi = item.TagLoaiNghi,
        TagMonHoc = item.TagMonHoc
    };
}

public sealed class AuditEntry
{
    public int Id
    {
        get; set;
    }
    public DateTime At { get; set; } = DateTime.Now;
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class RepositoryData
{
    public List<ClassGroup> Classes { get; set; } = new();
    public List<Student> Students { get; set; } = new();
    public List<LeaveRequest> Requests { get; set; } = new();
    public List<AuditEntry> Audit { get; set; } = new();
    public int NextId { get; set; } = 1;
    public int DataVersion { get; set; } = 2;
}
