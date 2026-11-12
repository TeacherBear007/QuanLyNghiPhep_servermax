using System.Text.Json;
using QuanLyNghiPhep.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services;

// Kho dữ liệu dùng chung, lưu SQLite; JSON cũ chỉ được đọc khi chuyển dữ liệu lần đầu.
// Mỗi thao tác ghi đều kiểm tra quyền ở tầng xử lý, không chỉ ẩn nút trên giao diện.
public sealed class LeaveRepository
{
    private readonly object _gate;
    private readonly string _file;
    private readonly BusinessStore _store;
    private readonly IServiceScopeFactory _scopes;
    private RepositoryData _data;
    private string? _committedJson;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LeaveRepository(IWebHostEnvironment env, IServiceScopeFactory scopes, BusinessStore store)
    {
        _scopes = scopes;
        string dir = Path.Combine(env.ContentRootPath, "AppData");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "du-lieu-nghi-phep.json");
        _store = store;
        _gate = store.SyncRoot;
        using var db = _store.Open();
        if (db.State.Any() || db.Classes.Any() || db.Students.Any() || db.Requests.Any())
        {
            _data = new RepositoryData { Classes = db.Classes.AsNoTracking().ToList(), Students = db.Students.AsNoTracking().ToList(), Requests = db.Requests.AsNoTracking().ToList(), Audit = new() };
            _data.NextId = db.State.AsNoTracking().FirstOrDefault()?.NextLeaveId ?? 1;
            NormalizeLoadedData(_data);
            InitializeManagers();
            _committedJson = JsonSerializer.Serialize(_data, JsonOptions);
            return;
        }
        if (File.Exists(_file))
        {
            _data = JsonSerializer.Deserialize<RepositoryData>(File.ReadAllText(_file))
                ?? throw new InvalidDataException("Không đọc được dữ liệu cũ; tệp gốc được giữ nguyên.");
            ValidateImportedData(_data);
            NormalizeLoadedData(_data);
            foreach (var audit in _data.Audit)
                audit.Id = 0;
            foreach (var r in _data.Requests.Where(r => !_data.Students.Any(s => s.MaHocVien == r.MaHocVien)))
                _data.Students.Add(new Student { MaHocVien = r.MaHocVien, HoTen = r.HoTen, Lop = r.Lop, TrungDoi = r.TrungDoi });
            NormalizeLoadedData(_data);
            SaveUnsafe();
        }
        else
        {
            _data = env.IsProduction() ? new RepositoryData() : DemoData.Create();
            SaveUnsafe();
        }
        InitializeManagers();
        _committedJson = JsonSerializer.Serialize(_data, JsonOptions);
        File.WriteAllText(Path.Combine(dir, "business-migrated.txt"), "Dữ liệu nghiệp vụ đã chuyển sang business.db. JSON gốc được giữ nguyên làm bản sao lưu.");
    }

    private void RefreshUnsafe()
    {
        using var db = _store.Open();
        _data = new RepositoryData
        {
            Classes = db.Classes.AsNoTracking().ToList(),
            Students = db.Students.AsNoTracking().ToList(),
            Requests = db.Requests.AsNoTracking().ToList(),
            NextId = db.State.AsNoTracking().FirstOrDefault()?.NextLeaveId ?? 1
        };
        NormalizeLoadedData(_data);
        _committedJson = JsonSerializer.Serialize(_data, JsonOptions);
    }
    private string PlatoonOf(string code, string className)
        => _data.Students.FirstOrDefault(s => s.MaHocVien == code)?.TrungDoi is { Length: > 0 } t ? t : $"{LeaveCategories.PlatoonPrefix} {className}";
    private string CanonicalClass(string name)
        => _data.Classes.FirstOrDefault(x => x.TenLop.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))?.TenLop
        ?? throw new ArgumentException("Không tìm thấy lớp.");
    private string NewStudentCode(string className, int number, IEnumerable<Student>? pending = null)
    {
        var prefix = className.ToUpperInvariant();
        if (prefix.Length > 20)
            prefix = prefix[..20];
        string code;
        do
        {
            code = $"{prefix}-{number++:000}";
        } while (_data.Students.Concat(pending ?? []).Any(x => x.MaHocVien.Equals(code, StringComparison.OrdinalIgnoreCase)));
        return code;
    }

    private static void ValidateImportedData(RepositoryData data)
    {
        if (data.Classes is null || data.Students is null || data.Requests is null || data.Audit is null)
            throw new InvalidDataException("Dữ liệu JSON cũ thiếu danh mục bắt buộc; tệp gốc được giữ nguyên.");
        if (data.Classes.Any(x => x is null || string.IsNullOrWhiteSpace(x.TenLop))
            || data.Classes.Select(x => x.TenLop).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Classes.Count
            || data.Students.Any(x => x is null || string.IsNullOrWhiteSpace(x.MaHocVien) || string.IsNullOrWhiteSpace(x.Lop))
            || data.Students.Select(x => x.MaHocVien).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Students.Count
            || data.Requests.Any(x => x is null || x.Id <= 0 || string.IsNullOrWhiteSpace(x.MaHocVien) || string.IsNullOrWhiteSpace(x.Lop) || x.DenThoiDiem <= x.TuThoiDiem)
            || data.Requests.Select(x => x.Id).Distinct().Count() != data.Requests.Count
            || data.Audit.Any(x => x is null))
            throw new InvalidDataException("Dữ liệu JSON cũ có khóa trùng hoặc hồ sơ không hợp lệ; cần kiểm tra bản sao lưu.");
    }

    private static bool NormalizeLoadedData(RepositoryData data)
    {
        bool changed = false;
        foreach (var request in data.Requests.Where(x => x.TrangThai == LeaveStatuses.WaitingTrungDoi))
        {
            request.TrangThai = LeaveStatuses.WaitingPhong;
            changed = true;
        }
        foreach (var s in data.Students)
        {
            if (!data.Classes.Any(c => c.TenLop.Equals(s.Lop, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(s.Lop))
            {
                data.Classes.Add(new ClassGroup { TenLop = s.Lop, NgayTao = DateTime.Now });
                changed = true;
            }
        }
        foreach (var student in data.Students)
        {
            var canonical = data.Classes.First(x => x.TenLop.Equals(student.Lop, StringComparison.OrdinalIgnoreCase)).TenLop;
            if (student.Lop != canonical)
            {
                student.Lop = canonical;
                changed = true;
            }
        }
        foreach (var request in data.Requests)
            if (!string.IsNullOrWhiteSpace(request.Lop) && !data.Classes.Any(x => x.TenLop.Equals(request.Lop, StringComparison.OrdinalIgnoreCase)))
            {
                data.Classes.Add(new ClassGroup { TenLop = request.Lop });
                changed = true;
            }
        int minimumNextId = data.Requests.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1;
        if (data.NextId < minimumNextId)
        {
            data.NextId = minimumNextId;
            changed = true;
        }
        return changed;
    }

    public List<ClassGroup> GetClasses(AppSession session)
    {
        RequireLoggedIn(session);
        lock (_gate)
        {
            RefreshUnsafe();
            var names = _data.Classes.Select(c => c.TenLop)
                .Concat(_data.Students.Select(s => s.Lop))
                .Concat(_data.Requests.Select(r => r.Lop))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var authScope = _scopes.CreateScope();
            var namesById = authScope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().ToDictionary(x => x.Id, x => x.UserName ?? "");
            var result = new List<ClassGroup>();
            foreach (var name in names)
            {
                var existing = _data.Classes.FirstOrDefault(c => c.TenLop.Equals(name, StringComparison.OrdinalIgnoreCase));
                var manager = existing is not null && namesById.TryGetValue(existing.ManagerUserId, out var currentName) ? currentName : existing?.NguoiTao ?? "";
                result.Add(new ClassGroup
                {
                    TenLop = existing?.TenLop ?? name,
                    NguoiTao = manager,
                    ManagerUserId = existing?.ManagerUserId ?? "",
                    NgayTao = existing?.NgayTao ?? DateTime.Today
                });
            }

            if (session.Role == UserRole.TrungDoi)
            {
                result = result
                    .Where(c => c.ManagerUserId == session.UserId)
                    .ToList();
            }
            else if (session.Role == UserRole.BoMon)
            {
                var allowed = AllowedClasses(session);
                result = result.Where(c => allowed.Contains(c.TenLop)).ToList();
            }

            return result;
        }
    }

    public List<Student> GetStudents(AppSession session, string? className = null, bool includeInactive = false)
    {
        RequireLoggedIn(session);
        if (includeInactive && session.Role is not (UserRole.Admin or UserRole.PhongQLHV))
            throw new UserAccessException("Không có quyền xem hồ sơ ngừng theo dõi.");
        lock (_gate)
        {
            RefreshUnsafe();
            var allowed = AllowedClasses(session);
            if (!string.IsNullOrWhiteSpace(className) && !allowed.Contains(className.Trim()))
                throw new UserAccessException("Lớp không thuộc phạm vi được phân công.");
            return _data.Students.Where(x => allowed.Contains(x.Lop) && (includeInactive || x.IsActive))
                .Where(x => string.IsNullOrWhiteSpace(className) || x.Lop.Equals(className.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Lop).ThenBy(x => x.STT).Select(x => x.Clone()).ToList();
        }
    }

    public List<LeaveRequest> GetRequests(AppSession session, string? className = null)
    {
        RequireLoggedIn(session);
        if (session.Role == UserRole.BoMon)
            return GetFacultyRequests(session, className);
        lock (_gate)
        {
            RefreshUnsafe();
            var allowed = AllowedClasses(session);
            if (!string.IsNullOrWhiteSpace(className) && !allowed.Contains(className.Trim()))
                throw new UserAccessException("Lớp không thuộc phạm vi được phân công.");
            var query = _data.Requests.Where(x => allowed.Contains(x.Lop));
            if (!string.IsNullOrWhiteSpace(className))
                query = query.Where(x => x.Lop.Equals(className.Trim(), StringComparison.OrdinalIgnoreCase));

            // Với đơn quy trình 2 bước (nếu có), kiểm tra trạng thái; với đơn đã duyệt trực tiếp từ Trung đội (TrangThai == Approved) đẩy sang cả Phòng QLHV và Giáo viên bộ môn
            query = query.Where(x => session.Role switch
            {
                UserRole.PhongQLHV => x.TrangThai != LeaveStatuses.WaitingTrungDoi,
                UserRole.BoMon => x.TrangThai == LeaveStatuses.Approved,
                _ => true
            });

            var result = query
                .OrderByDescending(x => x.TuThoiDiem)
                .ThenByDescending(x => x.Id)
                .Select(x => x.Clone())
                .ToList();
            return result;
        }
    }

    public List<string> GetSubjectTags(AppSession session, string? className = null)
    {
        RequireLoggedIn(session);
        lock (_gate)
        {
            RefreshUnsafe();
            var query = GetRequests(session, className).Where(x => x.PhanLoaiNghi == LeaveCategories.TranhThu && x.TrangThai == LeaveStatuses.Approved);
            if (!string.IsNullOrWhiteSpace(className))
                query = query.Where(x => x.Lop.Equals(className.Trim(), StringComparison.OrdinalIgnoreCase));

            return query
                .SelectMany(x => x.DanhSachTagMon)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }
    }

    public List<AuditEntry> GetAudit(AppSession session)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.Admin or UserRole.PhongQLHV))
            throw new UserAccessException("Chỉ Phòng QLHV hoặc Admin được xem nhật ký hệ thống.");
        lock (_gate)
        {
            RefreshUnsafe();
            using var db = _store.Open();
            return db.Audit.AsNoTracking().OrderByDescending(x => x.At).ThenByDescending(x => x.Id).Take(300).ToList();
        }
    }

    public List<AuditView> GetAuditView(AppSession session)
    {
        var rows = GetAudit(session);
        using var scope = _scopes.CreateScope();
        var names = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().ToDictionary(x => x.Id, x => x.UserName ?? "");
        return rows.Select(x => new AuditView(x.At, names.TryGetValue(x.User, out var n) ? n : x.User, x.Action, x.Detail)).ToList();
    }

    // Phòng Quản lý học viên tạo danh sách lớp mới và phân công tài khoản Chủ nhiệm lớp quản lý
    public ClassGroup CreateClassWithStudents(AppSession session, string className, IReadOnlyList<Student> students, string? managerUserName = null)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên mới có quyền tạo danh sách lớp mới.");
        var cleanClass = className?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(cleanClass) || cleanClass.Length > 60)
            throw new ArgumentException("Vui lòng nhập tên lớp hợp lệ (ví dụ: B9D55).");
        if (students is null || students.Count == 0 || students.Count > ValidationRules.MaxImportRows)
            throw new ArgumentException($"Danh sách lớp phải có từ 1 đến {ValidationRules.MaxImportRows} học viên.");

        var assignedManager = FindManager(string.IsNullOrWhiteSpace(managerUserName) ? "trungdoi" : managerUserName.Trim());

        lock (_gate)
        {
            RefreshUnsafe();
            if (_data.Classes.Any(c => c.TenLop.Equals(cleanClass, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Lớp {cleanClass} đã tồn tại trong hệ thống.");

            var validatedList = new List<Student>();
            int stt = 1;
            foreach (var item in students)
            {
                var hoTen = item.HoTen?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(hoTen))
                {
                    if (!string.IsNullOrWhiteSpace(item.MaHocVien) || !string.IsNullOrWhiteSpace(item.NgaySinh) || !string.IsNullOrWhiteSpace(item.SoDienThoai))
                        throw new ArgumentException($"Dòng {stt}: thiếu họ tên học viên.");
                    continue;
                }
                var lop = string.IsNullOrWhiteSpace(item.Lop) ? cleanClass : item.Lop.Trim();
                var ngaySinh = ValidationRules.BirthDate(item.NgaySinh);
                var sdt = ValidationRules.Phone(item.SoDienThoai);
                if (!lop.Equals(cleanClass, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Dòng {stt}: lớp của học viên khác lớp đang tạo.");
                var maHv = string.IsNullOrWhiteSpace(item.MaHocVien) ? NewStudentCode(cleanClass, stt, validatedList) : item.MaHocVien.Trim();
                if (maHv.Length > 30 || hoTen.Length > 100)
                    throw new ArgumentException($"Dòng {stt}: mã/họ tên vượt độ dài cho phép.");
                if (_data.Students.Concat(validatedList).Any(x => x.MaHocVien.Equals(maHv, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Dòng {stt}: mã học viên bị trùng.");

                validatedList.Add(new Student
                {
                    STT = stt++,
                    MaHocVien = maHv,
                    HoTen = hoTen,
                    Lop = cleanClass,
                    NgaySinh = ngaySinh,
                    SoDienThoai = sdt,
                    TrungDoi = $"{LeaveCategories.PlatoonPrefix} {cleanClass}"
                });
            }

            if (validatedList.Count == 0)
                throw new ArgumentException("Vui lòng nhập họ tên cho ít nhất 1 học viên.");

            var group = new ClassGroup
            {
                TenLop = cleanClass,
                NguoiTao = assignedManager.Name,
                ManagerUserId = assignedManager.Id,
                NgayTao = DateTime.Now
            };
            _data.Classes.Add(group);
            _data.Students.AddRange(validatedList);
            LogUnsafe(session, "Tạo danh sách lớp mới", $"Lớp {cleanClass} ({validatedList.Count} học viên) - Giao tài khoản {assignedManager.Name} quản lý");
            SaveUnsafe();
            return new ClassGroup { TenLop = group.TenLop, NguoiTao = group.NguoiTao, ManagerUserId = group.ManagerUserId, NgayTao = group.NgayTao };
        }
    }

    public void UpdateClassManager(AppSession session, string className, string managerUserName)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên mới có quyền phân công tài khoản quản lý lớp.");
        var cleanClass = className?.Trim() ?? "";
        var cleanManager = FindManager(string.IsNullOrWhiteSpace(managerUserName) ? "trungdoi" : managerUserName.Trim());
        lock (_gate)
        {
            RefreshUnsafe();
            var existing = _data.Classes.FirstOrDefault(c => c.TenLop.Equals(cleanClass, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                throw new ArgumentException("Không tìm thấy lớp.");
            existing.ManagerUserId = cleanManager.Id;
            existing.NguoiTao = cleanManager.Name;
            LogUnsafe(session, "Phân công quản lý lớp", $"Lớp {cleanClass} -> Tài khoản {cleanManager.Name}");
            SaveUnsafe();
        }
    }

    // Duyệt danh sách nghỉ cuối tuần hoặc nghỉ Lễ/Tết (hàng loạt) -> Đẩy lên lưu trữ Phòng QLHV
    public List<LeaveRequest> CreateBatchLeaveList(
        AppSession session,
        string className,
        string phanLoaiNghi,
        string tagLoaiNghi,
        DateTime tuThoiDiem,
        DateTime denThoiDiem,
        IReadOnlyList<BatchLeaveDraftItem> items)
    {
        RequireRole(session, UserRole.TrungDoi);
        if (string.IsNullOrWhiteSpace(className))
            throw new ArgumentException("Vui lòng chọn lớp.");
        if (denThoiDiem <= tuThoiDiem)
            throw new ArgumentException("Thời gian kết thúc nghỉ phải sau thời gian bắt đầu.");
        if ((denThoiDiem - tuThoiDiem).TotalDays > 60 || (tagLoaiNghi?.Length ?? 0) > 300)
            throw new ArgumentException("Thời gian nghỉ không quá 60 ngày; tên đợt nghỉ tối đa 300 ký tự.");
        if (items is null || items.Count == 0)
            throw new ArgumentException("Danh sách nghỉ không có học viên nào.");

        string cleanCategory = phanLoaiNghi == LeaveCategories.LeTet ? LeaveCategories.LeTet : LeaveCategories.CuoiTuan;
        string cleanTag = cleanCategory == LeaveCategories.CuoiTuan
            ? LeaveCategories.TagCuoiTuan
            : (string.IsNullOrWhiteSpace(tagLoaiNghi) ? "Nghỉ Lễ/Tết" : tagLoaiNghi.Trim());

        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            EnsureManagedClass(session, className);
            var activeItems = items.Where(x => !string.IsNullOrWhiteSpace(x.HoTen)).ToList();
            if (activeItems.Count == 0 || activeItems.Select(x => x.MaHocVien).Distinct(StringComparer.OrdinalIgnoreCase).Count() != activeItems.Count
                || activeItems.Any(x => x.DiaChiNoiNghi.Length > 300 || !_data.Students.Any(s => s.MaHocVien == x.MaHocVien && s.Lop == className && s.IsActive)))
                throw new ArgumentException("Danh sách phải có học viên hợp lệ, không trùng mã, thuộc lớp đã chọn; địa chỉ tối đa 300 ký tự.");
            foreach (var item in activeItems)
            {
                EnsureNoOverlappingLeave(
                    item.MaHocVien,
                    tuThoiDiem,
                    denThoiDiem);
            }
            var created = new List<LeaveRequest>();
            var now = DateTime.Now;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.HoTen))
                    continue;
                var id = _data.NextId++;
                var addr = string.IsNullOrWhiteSpace(item.DiaChiNoiNghi)
                    ? LeaveCategories.DefaultWeekendAddress
                    : item.DiaChiNoiNghi.Trim();

                var req = new LeaveRequest
                {
                    Id = id,
                    MaDon = $"NP{now:yyyy}-{id:0000}",
                    MaHocVien = string.IsNullOrWhiteSpace(item.MaHocVien) ? $"{className}-{item.STT:000}" : item.MaHocVien,
                    HoTen = _data.Students.Single(x => x.MaHocVien == item.MaHocVien && x.Lop == className).HoTen,
                    Lop = className.Trim(),
                    TrungDoi = PlatoonOf(item.MaHocVien, className.Trim()),
                    TuThoiDiem = tuThoiDiem,
                    DenThoiDiem = denThoiDiem,
                    LyDo = cleanTag,
                    DiaChiNoiNghi = addr,
                    TrangThai = LeaveStatuses.WaitingPhong,
                    PhanLoaiNghi = cleanCategory,
                    TagLoaiNghi = cleanTag,
                    TagMonHoc = "",
                    NgayTao = now,
                    NguoiTao = session.UserName,
                    NguoiDuyetTrungDoi = session.UserName,
                    NgayDuyetTrungDoi = now
                };
                _data.Requests.Add(req);
                created.Add(req.Clone());
            }

            LogUnsafe(session, $"Gửi danh sách {cleanTag}", $"Lớp {className} ({created.Count} học viên) - Chờ Phòng QLHV duyệt");
            SaveUnsafe();
            return created;
        }
    }

    // Duyệt đơn nghỉ phép tranh thủ (có Tag 2 môn học để link sang Giáo viên bộ môn và đẩy lên Phòng QLHV)
    public LeaveRequest CreateTranhThuLeave(AppSession session, TranhThuLeaveInput input)
    {
        RequireRole(session, UserRole.TrungDoi);
        var ten = input.TenHocVien?.Trim() ?? "";
        var lop = input.Lop?.Trim() ?? "";
        var lyDo = input.LyDo?.Trim() ?? "";
        var diaChi = string.IsNullOrWhiteSpace(input.DiaChiNoiNghi) ? LeaveCategories.DefaultWeekendAddress : input.DiaChiNoiNghi.Trim();
        var monHoc = input.MonHoc?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(ten))
            throw new ArgumentException("Vui lòng nhập họ tên học viên xin nghỉ phép tranh thủ.");
        if (string.IsNullOrWhiteSpace(lop))
            throw new ArgumentException("Vui lòng chọn lớp.");
        if (input.DenThoiDiem <= input.TuThoiDiem)
            throw new ArgumentException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if (string.IsNullOrWhiteSpace(lyDo))
            throw new ArgumentException("Vui lòng nhập lý do xin nghỉ.");
        if (ten.Length > 100 || lyDo.Length > 500 || diaChi.Length > 300 || monHoc.Length > 300 || (input.DenThoiDiem - input.TuThoiDiem).TotalDays > 60)
            throw new ArgumentException("Dữ liệu vượt độ dài cho phép hoặc thời gian nghỉ quá 60 ngày.");

        lock (_gate)
        {
            RefreshUnsafe();
            lop = CanonicalClass(lop);
            EnsureManagedClass(session, lop);
            var matches = _data.Students.Where(s => s.IsActive && s.Lop.Equals(lop, StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(input.MaHocVien) ? s.MaHocVien.Equals(input.MaHocVien, StringComparison.OrdinalIgnoreCase) : s.HoTen.Equals(ten, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count != 1)
                throw new ArgumentException("Hãy chọn mã học viên hợp lệ; họ tên không có hoặc bị trùng trong lớp.");
            var matchedStudent = matches[0];
            EnsureNoOverlappingLeave(
               matchedStudent.MaHocVien,
               input.TuThoiDiem,
               input.DenThoiDiem);
            ClassSubject selectedSubject;

            using (var db = _store.Open())
            {
                EnsureManagedClass(session, lop);

                if (!input.SubjectId.HasValue)
                    throw new ArgumentException(
                        "Vui lòng chọn môn học.");

                selectedSubject = db.ClassSubjects
                    .AsNoTracking()
                    .SingleOrDefault(x =>
                        x.Id == input.SubjectId.Value
                        && x.ClassName == lop)
                    ?? throw new ArgumentException(
                        "Môn học không thuộc lớp đang chọn.");

                if (string.IsNullOrWhiteSpace(selectedSubject.FacultyUserId))
                    throw new ArgumentException(
                        "Môn học chưa được liên kết với tài khoản khoa.");
                if (!GetFacultyOptions(session).Any(x => x.UserId == selectedSubject.FacultyUserId))
                    throw new ArgumentException("Tài khoản khoa phụ trách đã ngừng hoạt động hoặc không còn hợp lệ.");

                monHoc = selectedSubject.Name;
            }

            string maHv = matchedStudent.MaHocVien;
            string hoTen = matchedStudent.HoTen;

            var now = DateTime.Now;
            var id = _data.NextId++;
            var request = new LeaveRequest
            {
                Id = id,
                MaDon = $"NP{now:yyyy}-{id:0000}",
                MaHocVien = maHv,
                HoTen = hoTen,
                Lop = lop,
                TrungDoi = matchedStudent.TrungDoi is { Length: > 0 } ? matchedStudent.TrungDoi : $"{LeaveCategories.PlatoonPrefix} {lop}",
                TuThoiDiem = input.TuThoiDiem,
                DenThoiDiem = input.DenThoiDiem,
                LyDo = lyDo,
                DiaChiNoiNghi = diaChi,
                TrangThai = LeaveStatuses.WaitingPhong,
                PhanLoaiNghi = LeaveCategories.TranhThu,
                TagLoaiNghi = LeaveCategories.TagTranhThu,
                TagMonHoc = monHoc,
                SubjectId = selectedSubject.Id,
                NgayTao = now,
                NguoiTao = session.UserName,
                NguoiDuyetTrungDoi = session.UserName,
                NgayDuyetTrungDoi = now
            };

            _data.Requests.Add(request);
            LogUnsafe(session, "Gửi nghỉ phép tranh thủ", $"{request.HoTen} ({request.Lop}) - Môn: {request.TagMonHoc}");
            SaveUnsafe();
            return request.Clone();
        }
    }

    public List<FacultyOption> GetFacultyOptions(AppSession session)
    {
        RequireLoggedIn(session);

        if (session.Role is not (UserRole.TrungDoi or UserRole.Admin))
            throw new UserAccessException(
                "Bạn không có quyền xem danh sách khoa để phân công.");

        using var scope = _scopes.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AuthDbContext>();

        var facultyRole = UserRole.BoMon.ToString();

        return (
            from user in db.Users.AsNoTracking()
            join userRole in db.UserRoles
                on user.Id equals userRole.UserId
            join role in db.Roles
                on userRole.RoleId equals role.Id
            where role.Name == facultyRole
                && user.IsEnabled
                && user.FacultyId != ""
                && user.FacultyName != ""
            orderby user.FacultyName
            select new FacultyOption(
                user.Id,
                user.FacultyId,
                user.FacultyName)
        ).ToList();
    }

    public List<ClassSubject> GetFacultySubjects(AppSession session)
    {
        RequireRole(session, UserRole.BoMon);

        using var db = _store.Open();

        return db.ClassSubjects
            .AsNoTracking()
            .Where(x => x.FacultyUserId == session.UserId)
            .OrderBy(x => x.ClassName)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public LeaveRequest Add(AppSession session, LeaveInput input)
    {
        RequireRole(session, UserRole.TrungDoi);
        lock (_gate)
        {
            RefreshUnsafe();
            var student = ValidateInput(input);
            EnsureManagedClass(session, student.Lop);
            EnsureNoOverlappingLeave(
                student.MaHocVien,
                input.TuThoiDiem,
                input.DenThoiDiem);
            var id = _data.NextId++;
            var request = new LeaveRequest
            {
                Id = id,
                MaDon = $"NP{DateTime.Today:yyyy}-{id:0000}",
                MaHocVien = student.MaHocVien,
                HoTen = student.HoTen,
                Lop = student.Lop,
                TrungDoi = input.TrungDoi.Trim(),
                TuThoiDiem = input.TuThoiDiem,
                DenThoiDiem = input.DenThoiDiem,
                LyDo = input.LyDo.Trim(),
                DiaChiNoiNghi = input.DiaChiNoiNghi.Trim(),
                PhanLoaiNghi = input.PhanLoaiNghi,
                TagLoaiNghi = string.IsNullOrWhiteSpace(input.TagLoaiNghi) ? LeaveCategories.TagTranhThu : input.TagLoaiNghi.Trim(),
                TagMonHoc = input.TagMonHoc?.Trim() ?? "",
                TrangThai = LeaveStatuses.WaitingPhong,
                NgayTao = DateTime.Now,
                NguoiTao = session.UserName
            };
            _data.Requests.Add(request);
            LogUnsafe(session, "Tạo đơn", $"{request.HoTen} ({request.MaHocVien})");
            SaveUnsafe();
            return request.Clone();
        }
    }

    public LeaveRequest Update(AppSession session, int id, LeaveInput input)
    {
        RequireRole(session, UserRole.TrungDoi);
        lock (_gate)
        {
            RefreshUnsafe();
            var request = FindUnsafe(id);
            if (session.Role == UserRole.TrungDoi)
                EnsureManagedClass(session, request.Lop);
            RequirePending(request);
            var student = ValidateInput(input);
            EnsureManagedClass(session, student.Lop);
            EnsureNoOverlappingLeave(
                student.MaHocVien,
                input.TuThoiDiem,
                input.DenThoiDiem,
                id);
            request.MaHocVien = student.MaHocVien;
            request.HoTen = student.HoTen;
            request.Lop = student.Lop;
            request.TrungDoi = input.TrungDoi.Trim();
            request.TuThoiDiem = input.TuThoiDiem;
            request.DenThoiDiem = input.DenThoiDiem;
            request.LyDo = input.LyDo.Trim();
            request.DiaChiNoiNghi = input.DiaChiNoiNghi.Trim();
            LogUnsafe(session, "Sửa đơn", $"{request.HoTen} ({request.MaHocVien})");
            SaveUnsafe();
            return request.Clone();
        }
    }

    public List<LeaveRequest> GetFacultyRequests(
    AppSession session,
    string? className = null)
    {
        RequireRole(session, UserRole.BoMon);

        lock (_gate)
        {
            RefreshUnsafe();
            using var db = _store.Open();

            var subjects = db.ClassSubjects
                .AsNoTracking()
                .Where(x => x.FacultyUserId == session.UserId)
                .ToDictionary(x => x.Id);

            var result = new List<LeaveRequest>();

            foreach (var request in _data.Requests)
            {
                if (request.PhanLoaiNghi != LeaveCategories.TranhThu
                    || request.TrangThai != LeaveStatuses.Approved
                    || !request.SubjectId.HasValue)
                    continue;

                if (!subjects.TryGetValue(
                    request.SubjectId.Value, out var subject))
                    continue;

                if (!string.Equals(
                    request.Lop,
                    subject.ClassName,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(className)
                    && !request.Lop.Equals(
                        className.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var copy = request.Clone();
                copy.TagMonHoc = subject.Name;

                result.Add(copy);
            }

            return result
                .OrderByDescending(x => x.TuThoiDiem)
                .ThenByDescending(x => x.Id)
                .ToList();
        }
    }

    public void Delete(AppSession session, int id)
    {
        RequireRole(session, UserRole.TrungDoi);
        lock (_gate)
        {
            RefreshUnsafe();
            var request = FindUnsafe(id);
            if (session.Role == UserRole.TrungDoi)
                EnsureManagedClass(session, request.Lop);
            RequirePending(request);
            _data.Requests.Remove(request);
            LogUnsafe(session, "Xóa đơn", $"{request.HoTen} ({request.MaHocVien})");
            SaveUnsafe();
        }
    }


    private void EnsureNoOverlappingLeave(
    string maHocVien,
    DateTime tuThoiDiem,
    DateTime denThoiDiem,
    int? excludedId = null)
    {
        var existing = _data.Requests.FirstOrDefault(request =>
            request.MaHocVien.Equals(
                maHocVien,
                StringComparison.OrdinalIgnoreCase)
            && (!excludedId.HasValue || request.Id != excludedId.Value)
            && request.TrangThai != LeaveStatuses.Rejected && request.TrangThai != LeaveStatuses.Cancelled
            && tuThoiDiem < request.DenThoiDiem
            && denThoiDiem > request.TuThoiDiem);

        if (existing is not null)
        {
            throw new ArgumentException(
                $"Học viên {existing.HoTen} ({maHocVien}) "
                + $"đã có đơn {existing.MaDon} nghỉ từ "
                + $"{existing.TuThoiDiem:dd/MM/yyyy HH:mm} đến "
                + $"{existing.DenThoiDiem:dd/MM/yyyy HH:mm}. "
                + "Không thể tạo hoặc sửa đơn trùng thời gian.");
        }
    }
    private void EnsureManagedClass(
    AppSession session,
    string className)
    {
        RequireRole(session, UserRole.TrungDoi);

        var classGroup = _data.Classes.FirstOrDefault(x =>
            x.TenLop.Equals(
                className,
                StringComparison.OrdinalIgnoreCase));

        if (classGroup is null)
            throw new ArgumentException("Không tìm thấy lớp.");

        if (!string.Equals(
            classGroup.ManagerUserId,
            session.UserId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new UserAccessException(
                "Bạn chỉ được thao tác với lớp được phân công.");
        }
    }

    public List<ClassSubject> GetClassSubjects(
        AppSession session,
        string className)
    {
        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            EnsureManagedClass(session, className);

            using var db = _store.Open();

            return db.ClassSubjects
                .Where(x => x.ClassName == className)
                .OrderBy(x => x.Name)
                .ToList();
        }
    }

    public void AddClassSubject(
    AppSession session,
    string className,
    string code,
    string name,
    string facultyUserId)
    {
        code = code.Trim().ToUpperInvariant();
        name = name.Trim();

        if (code.Length is < 1 or > 40)
            throw new ArgumentException(
                "Vui lòng nhập mã môn, tối đa 40 ký tự.");

        if (name.Length is < 1 or > 150)
            throw new ArgumentException(
                "Vui lòng nhập tên môn, tối đa 150 ký tự.");

        var faculty = GetFacultyOptions(session)
            .FirstOrDefault(x => x.UserId == facultyUserId)
            ?? throw new ArgumentException(
                "Vui lòng chọn khoa đã được cấp tài khoản.");

        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            EnsureManagedClass(session, className);

            using var db = _store.Open();

            var existing = db.ClassSubjects.FirstOrDefault(x =>
                x.ClassName == className
                && x.Code.ToUpper() == code);

            if (existing is not null)
            {
                if (!string.IsNullOrWhiteSpace(existing.FacultyUserId))
                    throw new ArgumentException(
                        "Mã môn này đã có và đã được liên kết với khoa.");

                // Bổ sung liên kết cho môn cũ chưa được gán khoa.
                existing.FacultyUserId = faculty.UserId;
                existing.Faculty = faculty.Name;
            }
            else
            {
                db.ClassSubjects.Add(new ClassSubject
                {
                    ClassName = className,
                    Code = code,
                    Name = name,
                    Faculty = faculty.Name,
                    FacultyUserId = faculty.UserId
                });
            }

            db.SaveChanges();
        }
    }
    private static void RequireClassStaff(AppSession session)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên được sửa/xóa lớp.");
    }

    public void RenameClass(AppSession session, string oldName, string newName)
    {
        RequireClassStaff(session);
        newName = newName?.Trim() ?? "";
        if (newName.Length is < 1 or > 60)
            throw new ArgumentException("Tên lớp mới phải từ 1 đến 60 ký tự.");
        lock (_gate)
        {
            RefreshUnsafe();
            oldName = CanonicalClass(oldName);
            if (_data.Classes.Any(c => c.TenLop.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Lớp {newName} đã tồn tại.");
            using (var db = _store.Open())
            {
                using var tx = db.Database.BeginTransaction();
                db.Database.ExecuteSqlRaw("PRAGMA defer_foreign_keys = ON");
                db.Database.ExecuteSqlRaw("UPDATE \"Classes\" SET \"TenLop\" = {0} WHERE \"TenLop\" = {1}", newName, oldName);
                db.Database.ExecuteSqlRaw("UPDATE \"Students\" SET \"Lop\" = {0} WHERE \"Lop\" = {1}", newName, oldName);
                db.Database.ExecuteSqlRaw("UPDATE \"Students\" SET \"TrungDoi\" = {0} WHERE \"TrungDoi\" = {1}", $"{LeaveCategories.PlatoonPrefix} {newName}", $"{LeaveCategories.PlatoonPrefix} {oldName}");
                db.Database.ExecuteSqlRaw("UPDATE \"Requests\" SET \"Lop\" = {0} WHERE \"Lop\" = {1}", newName, oldName);
                db.Database.ExecuteSqlRaw("UPDATE \"ClassSubjects\" SET \"ClassName\" = {0} WHERE \"ClassName\" = {1}", newName, oldName);
                tx.Commit();
            }
            RefreshUnsafe();
            LogUnsafe(session, "Đổi tên lớp", $"{oldName} -> {newName}");
            SaveUnsafe();
        }
    }

    public void DeleteClass(AppSession session, string className)
    {
        RequireClassStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            if (_data.Requests.Any(x => x.Lop == className))
                throw new BusinessRuleException("Lớp đã có đơn nghỉ phép nên không thể xóa (để giữ lịch sử).");
            using (var db = _store.Open())
            {
                if (_data.Students.Where(x => x.Lop == className).Any(x => BusinessStore.HasLegacyStudentHistory(db, x.MaHocVien)))
                    throw new BusinessRuleException("Lớp có học viên đã đăng ký môn học nên không thể xóa.");
                db.ClassSubjects.RemoveRange(db.ClassSubjects.Where(x => x.ClassName == className));
                db.SaveChanges();
            }
            var count = _data.Students.RemoveAll(x => x.Lop == className);
            _data.Classes.RemoveAll(x => x.TenLop == className);
            LogUnsafe(session, "Xóa lớp", $"Lớp {className} ({count} học viên)");
            SaveUnsafe();
        }
    }

    public List<Holiday> GetHolidays(AppSession session)
    {
        RequireLoggedIn(session);
        lock (_gate) { using var db = _store.Open(); return db.Holidays.AsNoTracking().OrderBy(x => x.FromDate).ToList(); }
    }

    public void AddHoliday(AppSession session, string name, DateTime from, DateTime to)
    {
        RequireClassStaff(session);
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 300) throw new ArgumentException("Tên đợt nghỉ tối đa 300 ký tự.");
        if (to <= from || (to - from).TotalDays > 60) throw new ArgumentException("Thời gian kết thúc phải sau bắt đầu và không quá 60 ngày.");
        lock (_gate)
        {
            RefreshUnsafe();
            using (var db = _store.Open()) { db.Holidays.Add(new Holiday { Name = name, FromDate = from, ToDate = to }); db.SaveChanges(); }
            LogUnsafe(session, "Thêm lịch nghỉ lễ/Tết", $"{name}: {from:dd/MM/yyyy} - {to:dd/MM/yyyy}");
            SaveUnsafe();
        }
    }

    public void DeleteHoliday(AppSession session, int id)
    {
        RequireClassStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            using (var db = _store.Open())
            {
                var h = db.Holidays.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Không tìm thấy đợt nghỉ.");
                db.Holidays.Remove(h); db.SaveChanges();
                LogUnsafe(session, "Xóa lịch nghỉ lễ/Tết", h.Name);
            }
            SaveUnsafe();
        }
    }

    public void UpdateClassSubject(AppSession session, int id, string code, string name, string facultyUserId)
    {
        code = code?.Trim().ToUpperInvariant() ?? ""; name = name?.Trim() ?? "";
        if (code.Length is < 1 or > 40) throw new ArgumentException("Mã môn tối đa 40 ký tự.");
        if (name.Length is < 1 or > 150) throw new ArgumentException("Tên môn tối đa 150 ký tự.");
        var faculty = GetFacultyOptions(session).FirstOrDefault(x => x.UserId == facultyUserId)
            ?? throw new ArgumentException("Vui lòng chọn khoa đã được cấp tài khoản.");
        lock (_gate)
        {
            RefreshUnsafe();
            using var db = _store.Open();
            var subject = db.ClassSubjects.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Không tìm thấy môn học.");
            EnsureManagedClass(session, subject.ClassName);
            if (db.ClassSubjects.Any(x => x.ClassName == subject.ClassName && x.Id != id && x.Code.ToUpper() == code))
                throw new ArgumentException("Mã môn đã tồn tại trong lớp.");
            subject.Code = code; subject.Name = name; subject.Faculty = faculty.Name; subject.FacultyUserId = faculty.UserId;
            db.SaveChanges();
            LogUnsafe(session, "Sửa môn học", $"Lớp {subject.ClassName}: {code} - {name}");
            SaveUnsafe();
        }
    }

    public void DeleteClassSubject(AppSession session, int id)
    {
        lock (_gate)
        {
            RefreshUnsafe();
            using var db = _store.Open();
            var subject = db.ClassSubjects.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Không tìm thấy môn học.");
            EnsureManagedClass(session, subject.ClassName);
            if (_data.Requests.Any(x => x.SubjectId == id))
                throw new BusinessRuleException("Môn học đã được dùng trong đơn nghỉ nên không thể xóa.");
            db.ClassSubjects.Remove(subject);
            db.SaveChanges();
            LogUnsafe(session, "Xóa môn học", $"Lớp {subject.ClassName}: {subject.Code} - {subject.Name}");
            SaveUnsafe();
        }
    }

    public LeaveRequest Review(AppSession session, int id, bool approved, string note, string className)
    {
        RequireRole(session, UserRole.PhongQLHV);
        if (string.IsNullOrWhiteSpace(className))
            throw new ArgumentException("Hãy chọn lớp trước khi xét duyệt đơn.");
        lock (_gate)
        {
            RefreshUnsafe();
            var selectedClass = CanonicalClass(className);
            var request = FindUnsafe(id);
            if (!request.Lop.Equals(selectedClass, StringComparison.OrdinalIgnoreCase))
                throw new UserAccessException("Đơn không thuộc lớp đang xét duyệt. Hãy mở đúng lớp.");
            if (request.TrangThai != LeaveStatuses.WaitingPhong)
                throw new BusinessRuleException("Đơn không còn chờ Phòng QLHV duyệt. Hãy tải lại danh sách.");
            if (!approved && string.IsNullOrWhiteSpace(note))
                throw new ArgumentException("Cần ghi lý do khi từ chối đơn.");
            if (note.Length > 300)
                throw new ArgumentException("Ghi chú xét duyệt không vượt quá 300 ký tự.");
            var now = DateTime.Now;
            request.NguoiDuyetPhong = session.UserName;
            request.NgayDuyetPhong = now;
            request.TrangThai = approved ? LeaveStatuses.Approved : LeaveStatuses.Rejected;
            request.NguoiDuyet = session.UserName;
            request.NgayDuyet = now;
            request.GhiChuDuyet = note.Trim();
            LogUnsafe(session, approved ? "Phòng QLHV duyệt" : "Phòng QLHV từ chối",
                $"Lớp {request.Lop}: {request.MaDon} - {request.HoTen} ({request.MaHocVien})");
            SaveUnsafe();
            return request.Clone();
        }
    }

    public LeaveRequest Cancel(AppSession session, int id, string reason)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.TrungDoi or UserRole.PhongQLHV))
            throw new UserAccessException("Vai trò này không có quyền hủy đơn.");
        reason = reason?.Trim() ?? "";
        if (reason.Length is < 1 or > 300)
            throw new ArgumentException("Cần ghi lý do hủy (tối đa 300 ký tự).");
        lock (_gate)
        {
            RefreshUnsafe();
            var request = FindUnsafe(id);
            if (session.Role == UserRole.TrungDoi)
                EnsureManagedClass(session, request.Lop);
            if (request.TrangThai is not (LeaveStatuses.Approved or LeaveStatuses.WaitingPhong))
                throw new BusinessRuleException("Chỉ hủy được đơn đang chờ Phòng QLHV hoặc đã duyệt.");
            var now = DateTime.Now;
            request.TrangThai = LeaveStatuses.Cancelled;
            request.NguoiDuyet = session.UserName;
            request.NgayDuyet = now;
            request.GhiChuDuyet = "Hủy: " + reason;
            LogUnsafe(session, "Hủy đơn", $"{request.MaDon} {request.HoTen} ({request.MaHocVien}) - Lý do: {reason}");
            SaveUnsafe();
            return request.Clone();
        }
    }

    public Student AddStudent(AppSession session, Student input)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên mới có quyền thêm học viên.");
        lock (_gate)
        {
            RefreshUnsafe();
            var lop = input.Lop?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(lop))
                throw new ArgumentException("Vui lòng chọn lớp cho học viên.");
            if (!_data.Classes.Any(x => x.TenLop.Equals(lop, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Hãy tạo lớp trước khi thêm học viên.");
            lop = CanonicalClass(lop);
            int nextStt = _data.Students.Where(x => x.Lop == lop).Select(x => x.STT).DefaultIfEmpty(0).Max() + 1;
            var candidate = input.Clone();
            candidate.Lop = lop;
            if (string.IsNullOrWhiteSpace(candidate.MaHocVien))
                candidate.MaHocVien = NewStudentCode(lop, nextStt);
            var student = ValidateStudent(candidate);
            if (_data.Students.Any(x => x.MaHocVien.Equals(student.MaHocVien, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Mã học viên đã tồn tại.");
            student.STT = nextStt;
            _data.Students.Add(student);
            LogUnsafe(session, "Thêm học viên", $"{student.HoTen} ({student.MaHocVien}) - Lớp {student.Lop}");
            SaveUnsafe();
            return student.Clone();
        }
    }

    public void UpdateStudent(AppSession session, string originalCode, Student input)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên mới có quyền sửa thông tin học viên.");
        lock (_gate)
        {
            RefreshUnsafe();
            var student = _data.Students.FirstOrDefault(x => x.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Không tìm thấy học viên.");
            originalCode = student.MaHocVien;
            var candidate = input.Clone();
            candidate.MaHocVien = string.IsNullOrWhiteSpace(candidate.MaHocVien) ? originalCode : candidate.MaHocVien;
            candidate.Lop = CanonicalClass(candidate.Lop);
            var updated = ValidateStudent(candidate);
            if (updated.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase))
                updated.MaHocVien = originalCode;
            if (!originalCode.Equals(updated.MaHocVien, StringComparison.OrdinalIgnoreCase)
                && _data.Students.Any(x => x.MaHocVien.Equals(updated.MaHocVien, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Mã học viên mới đã tồn tại.");
            if (!originalCode.Equals(updated.MaHocVien, StringComparison.Ordinal)
                && _data.Requests.Any(x => x.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessRuleException("Không đổi mã học viên đã có đơn để giữ nguyên liên kết và hồ sơ đã duyệt.");
            using (var db = _store.Open())
                if (originalCode != updated.MaHocVien && BusinessStore.HasLegacyStudentHistory(db, originalCode))
                    throw new BusinessRuleException("Không đổi mã học viên đã đăng ký môn học.");
            if (!_data.Classes.Any(x => x.TenLop.Equals(updated.Lop, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Lớp đích không tồn tại.");
            if (student.Lop != updated.Lop && _data.Requests.Any(x => x.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase) && x.TrangThai is LeaveStatuses.WaitingTrungDoi or LeaveStatuses.WaitingPhong))
                throw new BusinessRuleException("Cần xử lý xong đơn đang chờ trước khi chuyển lớp học viên.");
            if (student.Lop != updated.Lop)
                student.STT = _data.Students.Where(x => x.Lop == updated.Lop).Select(x => x.STT).DefaultIfEmpty(0).Max() + 1;
            student.MaHocVien = updated.MaHocVien;
            student.HoTen = updated.HoTen;
            student.Lop = updated.Lop;
            student.TrungDoi = updated.TrungDoi;
            student.NgaySinh = updated.NgaySinh;
            student.SoDienThoai = updated.SoDienThoai;
            foreach (var request in _data.Requests.Where(x => x.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase) && x.TrangThai == LeaveStatuses.WaitingTrungDoi))
            {
                request.MaHocVien = student.MaHocVien;
                request.HoTen = student.HoTen;
                request.Lop = student.Lop;
                request.TrungDoi = student.TrungDoi;
            }
            LogUnsafe(session, "Sửa học viên", $"{student.HoTen} ({student.MaHocVien})");
            SaveUnsafe();
        }
    }

    // Returns true for an unused record deleted permanently; false when archived.
    public bool DeleteStudent(AppSession session, string studentCode)
    {
        RequireStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            var student = _data.Students.FirstOrDefault(x => x.MaHocVien.Equals(studentCode, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Không tìm thấy học viên.");
            using var db = _store.Open();
            bool historical = _data.Requests.Any(x => x.MaHocVien.Equals(studentCode, StringComparison.OrdinalIgnoreCase))
                || BusinessStore.HasLegacyStudentHistory(db, studentCode);
            if (historical)
            {
                student.IsActive = false;
                LogUnsafe(session, "Ngừng theo dõi học viên", student.MaHocVien);
            }
            else
            {
                _data.Students.Remove(student);
                LogUnsafe(session, "Xóa học viên chưa có hồ sơ", student.MaHocVien);
            }
            SaveUnsafe();
            return !historical;
        }
    }
    public void ReactivateStudent(AppSession session, string studentCode)
    {
        RequireStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            var student = _data.Students.SingleOrDefault(x => x.MaHocVien.Equals(studentCode, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Không tìm thấy học viên.");
            student.IsActive = true;
            LogUnsafe(session, "Khôi phục theo dõi", studentCode);
            SaveUnsafe();
        }
    }

    private static Student ValidateStudent(Student input)
    {
        var code = input.MaHocVien?.Trim() ?? "";
        var name = input.HoTen?.Trim() ?? "";
        var lop = input.Lop?.Trim() ?? "";
        var trungDoi = string.IsNullOrWhiteSpace(input.TrungDoi) ? $"Trung đội {lop}" : input.TrungDoi.Trim();
        var ngaySinh = ValidationRules.BirthDate(input.NgaySinh);
        var sdt = ValidationRules.Phone(input.SoDienThoai);
        if (code.Length is < 1 or > 30 || name.Length is < 1 or > 100
            || lop.Length is < 1 or > 60 || trungDoi.Length is < 1 or > 80)
            throw new ArgumentException("Mã, họ tên, lớp và trung đội phải được nhập đầy đủ, đúng độ dài.");
        return new Student
        {
            STT = input.STT > 0 ? input.STT : 1,
            MaHocVien = code,
            HoTen = name,
            Lop = lop,
            TrungDoi = trungDoi,
            NgaySinh = ngaySinh,
            SoDienThoai = sdt
        };
    }

    public byte[] ExportExcel(AppSession session, IEnumerable<LeaveRequest> rows)
    {
        RequireLoggedIn(session);
        if (!session.CanExport)
            throw new UserAccessException("Vai trò hiện tại không được xuất Excel.");
        var requestedIds = rows.Select(x => x.Id).ToHashSet();
        return ExcelExporter.Build(GetRequests(session).Where(x => requestedIds.Contains(x.Id)).ToList());
    }

    private Student ValidateInput(LeaveInput input)
    {
        if (input.TrungDoi?.Length > 80)
            throw new ArgumentException("Tên trung đội tối đa 80 ký tự.");
        if (input.DenThoiDiem <= input.TuThoiDiem)
            throw new ArgumentException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if ((input.DenThoiDiem - input.TuThoiDiem).TotalDays > 60)
            throw new ArgumentException("Đơn nghỉ không được vượt quá 60 ngày.");
        if (string.IsNullOrWhiteSpace(input.LyDo) || input.LyDo.Length > 500)
            throw new ArgumentException("Lý do nghỉ phải có nội dung và không vượt quá 500 ký tự.");
        if (string.IsNullOrWhiteSpace(input.DiaChiNoiNghi) || input.DiaChiNoiNghi.Length > 300)
            throw new ArgumentException("Địa chỉ nơi nghỉ phải có nội dung và không vượt quá 300 ký tự.");
        if (input.MaHocVien == "__manual__")
            throw new UserAccessException("Phòng QLHV cần thêm học viên vào danh mục trước khi trung đội lập đơn.");
        if (string.IsNullOrWhiteSpace(input.MaHocVien))
            throw new ArgumentException("Vui lòng chọn học viên.");
        var found = _data.Students.FirstOrDefault(x => x.MaHocVien.Equals(input.MaHocVien, StringComparison.OrdinalIgnoreCase) && x.IsActive)
            ?? throw new ArgumentException("Mã học viên không tồn tại.");
        if (string.IsNullOrWhiteSpace(input.TrungDoi))
            input.TrungDoi = string.IsNullOrWhiteSpace(found.TrungDoi) ? $"{LeaveCategories.PlatoonPrefix} {found.Lop}" : found.TrungDoi;
        return found;
    }

    private static void RequireStaff(AppSession session)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.Admin or UserRole.PhongQLHV))
            throw new UserAccessException("Chỉ Phòng QLHV hoặc Admin được quản lý học viên.");
    }
    private HashSet<string> AllowedClasses(AppSession session)
    {
        if (session.Role == UserRole.TrungDoi)
            return _data.Classes.Where(x => x.ManagerUserId == session.UserId).Select(x => x.TenLop).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (session.Role == UserRole.BoMon)
        {
            using var db = _store.Open();
            return db.ClassSubjects.Where(x => x.FacultyUserId == session.UserId).Select(x => x.ClassName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return _data.Classes.Select(x => x.TenLop).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    private (string Id, string Name) FindManager(string name, bool requireEnabled = true)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var normalized = name.ToUpperInvariant();
        var user = db.Users.AsNoTracking().SingleOrDefault(x => x.NormalizedUserName == normalized);
        if (user is null || (requireEnabled && !user.IsEnabled) || !db.UserRoles.Any(ur => ur.UserId == user.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == nameof(UserRole.TrungDoi))))
            throw new ArgumentException("Chọn tài khoản chủ nhiệm lớp hợp lệ đang hoạt động.");
        return (user.Id, user.UserName ?? name);
    }
    private void InitializeManagers()
    {
        bool changed = false;
        foreach (var group in _data.Classes.Where(x => string.IsNullOrEmpty(x.ManagerUserId)))
        {
            if (string.IsNullOrWhiteSpace(group.NguoiTao))
                continue;
            try
            {
                group.ManagerUserId = FindManager(group.NguoiTao, false).Id;
                changed = true;
            }
            catch (ArgumentException) { /* Orphan assignment stays visible to staff for explicit reassignment. */ }
        }
        if (changed)
            SaveUnsafe();
    }
    private static void RequireLoggedIn(AppSession session)
    {
        session.EnsureValid();
    }

    private static void RequireRole(AppSession session, UserRole role)
    {
        RequireLoggedIn(session);
        if (session.Role != role)
            throw new UserAccessException("Tài khoản không có quyền thực hiện thao tác này.");
    }

    private LeaveRequest FindUnsafe(int id) => _data.Requests.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("Không tìm thấy đơn nghỉ phép.");

    private static void RequirePending(LeaveRequest request)
    {
        if (request.TrangThai != LeaveStatuses.WaitingTrungDoi)
            throw new BusinessRuleException("Đơn đã được xét duyệt, không thể sửa, xóa hoặc xét duyệt lần nữa.");
    }

    private void LogUnsafe(AppSession session, string action, string detail)
    {
        _data.Audit.Add(new AuditEntry
        {
            At = DateTime.Now,
            User = session.UserId,
            Action = action,
            Detail = detail
        });
    }

    private void SaveUnsafe()
    {
        try
        {
            using var db = _store.Open();
            using var tx = db.Database.BeginTransaction();
            Sync(db, _data.Classes, (ClassGroup x) => x.TenLop);
            Sync(db, _data.Students, (Student x) => x.MaHocVien);
            Sync(db, _data.Requests, (LeaveRequest x) => x.Id.ToString());
            foreach (var entry in _data.Audit.Where(x => x.Id == 0))
                db.Audit.Add(entry);
            var state = db.State.SingleOrDefault();
            if (state is null)
                db.State.Add(new BusinessState { NextLeaveId = _data.NextId });
            else
                state.NextLeaveId = _data.NextId;
            db.SaveChanges();
            tx.Commit();
            _committedJson = JsonSerializer.Serialize(_data, JsonOptions);
        }
        catch
        {
            if (_committedJson is not null)
                _data = JsonSerializer.Deserialize<RepositoryData>(_committedJson)!;
            throw;
        }
    }

    private static void Sync<T>(BusinessDbContext db, List<T> values, Func<T, string> key) where T : class
    {
        var existing = db.Set<T>().ToList().ToDictionary(key);
        foreach (var value in values)
            if (existing.Remove(key(value), out var old))
                db.Entry(old).CurrentValues.SetValues(value);
            else
                db.Set<T>().Add(value);
        db.RemoveRange(existing.Values);
    }

}

public sealed record AuditView(DateTime At, string User, string Action, string Detail);
