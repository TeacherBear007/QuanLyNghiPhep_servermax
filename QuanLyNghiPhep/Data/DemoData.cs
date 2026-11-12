using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Data;

internal static class DemoData
{
    public static RepositoryData Create()
    {
        var classes = new List<ClassGroup>
        {
            new() { TenLop = "B10D57", NguoiTao = "trungdoi", NgayTao = new DateTime(2026, 9, 1, 8, 0, 0) }
        };

        var students = new List<Student>
        {
            new() { STT = 1, MaHocVien = "HV24001", HoTen = "Nguyễn Văn An", Lop = "B10D57", NgaySinh = "12/03/2005", SoDienThoai = "0912345101", TrungDoi = "Trung đội B10D57" },
            new() { STT = 2, MaHocVien = "HV24002", HoTen = "Trần Minh Đức", Lop = "B10D57", NgaySinh = "25/07/2005", SoDienThoai = "0912345102", TrungDoi = "Trung đội B10D57" },
            new() { STT = 3, MaHocVien = "HV24003", HoTen = "Lê Thu Hà", Lop = "B10D57", NgaySinh = "08/11/2005", SoDienThoai = "0912345103", TrungDoi = "Trung đội B10D57" },
            new() { STT = 4, MaHocVien = "HV24004", HoTen = "Phạm Quốc Bảo", Lop = "B10D57", NgaySinh = "19/01/2005", SoDienThoai = "0912345104", TrungDoi = "Trung đội B10D57" },
            new() { STT = 5, MaHocVien = "HV24005", HoTen = "Hoàng Ngọc Mai", Lop = "B10D57", NgaySinh = "30/04/2005", SoDienThoai = "0912345105", TrungDoi = "Trung đội B10D57" },
            new() { STT = 6, MaHocVien = "HV24006", HoTen = "Đỗ Thị Quỳnh", Lop = "B10D57", NgaySinh = "14/09/2005", SoDienThoai = "0912345106", TrungDoi = "Trung đội B10D57" },
            new() { STT = 7, MaHocVien = "HV24007", HoTen = "Bùi Anh Tuấn", Lop = "B10D57", NgaySinh = "03/06/2005", SoDienThoai = "0912345107", TrungDoi = "Trung đội B10D57" },
            new() { STT = 8, MaHocVien = "HV24008", HoTen = "Ngô Minh Khang", Lop = "B10D57", NgaySinh = "22/12/2005", SoDienThoai = "0912345108", TrungDoi = "Trung đội B10D57" },
            new() { STT = 9, MaHocVien = "HV24009", HoTen = "Vũ Đức Duy", Lop = "B10D57", NgaySinh = "17/05/2005", SoDienThoai = "0912345109", TrungDoi = "Trung đội B10D57" },
            new() { STT = 10, MaHocVien = "HV24010", HoTen = "Nguyễn Thị Lan", Lop = "B10D57", NgaySinh = "09/08/2005", SoDienThoai = "0912345110", TrungDoi = "Trung đội B10D57" },
            new() { STT = 11, MaHocVien = "HV24011", HoTen = "Đặng Quang Huy", Lop = "B10D57", NgaySinh = "28/02/2005", SoDienThoai = "0912345111", TrungDoi = "Trung đội B10D57" },
            new() { STT = 12, MaHocVien = "HV24012", HoTen = "Trương Bảo Ngọc", Lop = "B10D57", NgaySinh = "05/10/2005", SoDienThoai = "0912345112", TrungDoi = "Trung đội B10D57" }
        };

        var requests = new List<LeaveRequest>();
        int nextId = 1;

        // 1. Dữ liệu mẫu Nghỉ phép cuối tuần (Từ 17h ngày 06/11/2026 đến 17h ngày 07/11/2026)
        var weekendStart = new DateTime(2026, 11, 6, 17, 0, 0);
        var weekendEnd = new DateTime(2026, 11, 7, 17, 0, 0);
        var weekendCreated = new DateTime(2026, 11, 6, 10, 0, 0);
        for (int i = 0; i < 8; i++)
        {
            var s = students[i];
            string addr = i == 2 ? "Xã Đông Anh, Thành phố Hà Nội (Về nghỉ tại nhà)"
                        : i == 5 ? "Thành phố Bắc Ninh, Tỉnh Bắc Ninh (Về nghỉ tại nhà)"
                        : LeaveCategories.DefaultWeekendAddress;
            requests.Add(new LeaveRequest
            {
                Id = nextId,
                MaDon = $"NP2026-{nextId:0000}",
                MaHocVien = s.MaHocVien,
                HoTen = s.HoTen,
                Lop = s.Lop,
                TrungDoi = s.TrungDoi,
                TuThoiDiem = weekendStart,
                DenThoiDiem = weekendEnd,
                LyDo = LeaveCategories.TagCuoiTuan,
                DiaChiNoiNghi = addr,
                TrangThai = LeaveStatuses.Approved,
                PhanLoaiNghi = LeaveCategories.CuoiTuan,
                TagLoaiNghi = LeaveCategories.TagCuoiTuan,
                TagMonHoc = "",
                NgayTao = weekendCreated,
                NguoiTao = "trungdoi",
                NguoiDuyetTrungDoi = "trungdoi",
                NgayDuyetTrungDoi = weekendCreated,
                NguoiDuyetPhong = "phongqlhv",
                NgayDuyetPhong = weekendCreated,
                NguoiDuyet = "trungdoi",
                NgayDuyet = weekendCreated
            });
            nextId++;
        }

        // 2. Dữ liệu mẫu Nghỉ Lễ / Tết (Nghỉ lễ Quốc khánh 2/9 và Nghỉ lễ 30-4 1-5)
        var holidayStart = new DateTime(2026, 9, 1, 17, 0, 0);
        var holidayEnd = new DateTime(2026, 9, 3, 17, 0, 0);
        var holidayCreated = new DateTime(2026, 8, 31, 9, 0, 0);
        for (int i = 0; i < 5; i++)
        {
            var s = students[i];
            requests.Add(new LeaveRequest
            {
                Id = nextId,
                MaDon = $"NP2026-{nextId:0000}",
                MaHocVien = s.MaHocVien,
                HoTen = s.HoTen,
                Lop = s.Lop,
                TrungDoi = s.TrungDoi,
                TuThoiDiem = holidayStart,
                DenThoiDiem = holidayEnd,
                LyDo = "Nghỉ lễ Quốc khánh 2-9",
                DiaChiNoiNghi = i % 2 == 0 ? LeaveCategories.DefaultWeekendAddress : "Về nghỉ tại gia đình",
                TrangThai = LeaveStatuses.Approved,
                PhanLoaiNghi = LeaveCategories.LeTet,
                TagLoaiNghi = "Nghỉ lễ Quốc khánh 2-9",
                TagMonHoc = "",
                NgayTao = holidayCreated,
                NguoiTao = "trungdoi",
                NguoiDuyetTrungDoi = "trungdoi",
                NgayDuyetTrungDoi = holidayCreated,
                NguoiDuyetPhong = "phongqlhv",
                NgayDuyetPhong = holidayCreated,
                NguoiDuyet = "trungdoi",
                NgayDuyet = holidayCreated
            });
            nextId++;
        }

        // 3. Dữ liệu mẫu Nghỉ phép tranh thủ (có Tag 2 là tên môn học để khoa giảng dạy tra cứu)
        var tranhThuSamples = new (Student Student, DateTime From, DateTime To, string Reason, string SubjectTag, string Address)[]
        {
            (students[0], new DateTime(2026, 11, 2, 7, 30, 0), new DateTime(2026, 11, 2, 11, 30, 0), "Khám bệnh tại Bệnh viện 19-8 Bộ Công an", "Luật hình sự", "Bệnh viện 19-8, Mai Dịch, Cầu Giấy, Hà Nội"),
            (students[1], new DateTime(2026, 11, 3, 13, 30, 0), new DateTime(2026, 11, 3, 17, 0, 0), "Giải quyết việc gia đình đột xuất", "Luật hình sự", "Phường Hà Đông, Thành phố Hà Nội"),
            (students[2], new DateTime(2026, 11, 4, 7, 30, 0), new DateTime(2026, 11, 4, 16, 30, 0), "Về quê thăm bố mẹ bị ốm", "Luật hình sự, Luật tố tụng hình sự", "Thành phố Hải Phòng"),
            (students[3], new DateTime(2026, 11, 5, 7, 30, 0), new DateTime(2026, 11, 5, 11, 30, 0), "Đi khám răng theo lịch hẹn bác sĩ", "An ninh mạng", "Phường Hà Đông, Thành phố Hà Nội"),
            (students[4], new DateTime(2026, 10, 26, 7, 30, 0), new DateTime(2026, 10, 26, 11, 30, 0), "Làm lại căn cước và giấy tờ cá nhân", "Nghiệp vụ cơ bản", "Phường Hà Đông, Thành phố Hà Nội"),
            (students[0], new DateTime(2026, 10, 20, 13, 30, 0), new DateTime(2026, 10, 20, 17, 0, 0), "Tái khám sức khỏe định kỳ", "Luật hình sự", "Bệnh viện 103, Hà Đông, Hà Nội"),
            (students[5], new DateTime(2026, 10, 15, 7, 30, 0), new DateTime(2026, 10, 15, 17, 0, 0), "Gia đình có việc hiếu tại quê", "Tâm lý học nghiệp vụ", "Thành phố Thanh Hóa")
        };

        foreach (var sample in tranhThuSamples)
        {
            var created = sample.From.AddHours(-12);
            requests.Add(new LeaveRequest
            {
                Id = nextId,
                MaDon = $"NP2026-{nextId:0000}",
                MaHocVien = sample.Student.MaHocVien,
                HoTen = sample.Student.HoTen,
                Lop = sample.Student.Lop,
                TrungDoi = sample.Student.TrungDoi,
                TuThoiDiem = sample.From,
                DenThoiDiem = sample.To,
                LyDo = sample.Reason,
                DiaChiNoiNghi = sample.Address,
                TrangThai = LeaveStatuses.Approved,
                PhanLoaiNghi = LeaveCategories.TranhThu,
                TagLoaiNghi = LeaveCategories.TagTranhThu,
                TagMonHoc = sample.SubjectTag,
                NgayTao = created,
                NguoiTao = "trungdoi",
                NguoiDuyetTrungDoi = "trungdoi",
                NgayDuyetTrungDoi = created,
                NguoiDuyetPhong = "phongqlhv",
                NgayDuyetPhong = created,
                NguoiDuyet = "trungdoi",
                NgayDuyet = created
            });
            nextId++;
        }

        return new RepositoryData
        {
            Classes = classes,
            Students = students,
            Requests = requests,
            NextId = nextId,
            DataVersion = 2,
            Audit = new List<AuditEntry>
            {
                new() { At = new DateTime(2026, 11, 6, 10, 0, 0), User = "trungdoi", Action = "Khởi tạo dữ liệu lớp B10D57", Detail = "Đã nạp danh sách lớp B10D57 và dữ liệu nghỉ cuối tuần, nghỉ lễ/tết, nghỉ phép tranh thủ." }
            }
        };
    }
}
