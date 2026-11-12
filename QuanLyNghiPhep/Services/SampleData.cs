using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services;

// Dữ liệu mẫu cho Khoa và Môn học. Chỉ chạy khi hệ thống chưa có tài khoản khoa nào (không ghi đè dữ liệu thật).
public static class SampleData
{
    private static readonly (string User, string Id, string Name)[] Faculties =
    {
        ("khoa_luat", "KLUAT", "Khoa Luật"),
        ("khoa_nvcb", "KNVCB", "Khoa Nghiệp vụ cơ bản"),
        ("khoa_ancm", "KANCM", "Khoa An ninh mạng"),
    };

    private static readonly (string Code, string Name, string FacultyUser)[] Subjects =
    {
        ("LHS", "Luật hình sự", "khoa_luat"),
        ("TTHS", "Luật tố tụng hình sự", "khoa_luat"),
        ("NVCB", "Nghiệp vụ cơ bản", "khoa_nvcb"),
        ("TLNV", "Tâm lý học nghiệp vụ", "khoa_nvcb"),
        ("ANM", "An ninh mạng", "khoa_ancm"),
    };

    public static async Task EnsureAsync(IServiceProvider services, string dataDirectory)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.Users.AnyAsync(u => u.FacultyId != ""))
            return;

        var lines = new List<string> { "Tài khoản khoa mẫu. Đổi mật khẩu hoặc xóa tài khoản sau khi dùng thử." };
        var ids = new Dictionary<string, (string Id, string Name)>();
        foreach (var (name, facultyId, facultyName) in Faculties)
        {
            if (await users.FindByNameAsync(name) is not null) continue;
            var password = AccountSetup.NewPassword();
            var user = new ApplicationUser { UserName = name, LockoutEnabled = true, FacultyId = facultyId, FacultyName = facultyName };
            AccountSetup.Check(await users.CreateAsync(user, password));
            AccountSetup.Check(await users.AddToRoleAsync(user, nameof(UserRole.BoMon)));
            ids[name] = (user.Id, facultyName);
            lines.Add($"{name}\t{nameof(UserRole.BoMon)}\t{password}");
        }
        if (ids.Count == 0) return;
        await File.AppendAllLinesAsync(Path.Combine(dataDirectory, "khoa-mau.txt"), lines);

        // Khởi tạo kho nghiệp vụ trước (tạo dữ liệu mẫu hoặc chuyển JSON cũ nếu cần).
        services.GetRequiredService<LeaveRepository>();
        var store = services.GetRequiredService<BusinessStore>();
        lock (store.SyncRoot)
        {
            using var db = store.Open();
            foreach (var cls in db.Classes.Select(x => x.TenLop).ToList())
            {
                if (db.ClassSubjects.Any(x => x.ClassName == cls)) continue;
                foreach (var (code, name, facultyUser) in Subjects)
                    if (ids.TryGetValue(facultyUser, out var f))
                        db.ClassSubjects.Add(new ClassSubject { ClassName = cls, Code = code, Name = name, Faculty = f.Name, FacultyUserId = f.Id });
            }
            db.SaveChanges();
            // Gắn đơn tranh thủ cũ (chỉ có tên môn) vào môn học tương ứng để Khoa nhìn thấy.
            var subjects = db.ClassSubjects.AsNoTracking().ToList();
            foreach (var request in db.Requests.Where(x => x.PhanLoaiNghi == LeaveCategories.TranhThu && x.SubjectId == null).ToList())
            {
                var names = request.TagMonHoc.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var match = subjects.FirstOrDefault(s => s.ClassName == request.Lop && names.Contains(s.Name, StringComparer.OrdinalIgnoreCase));
                if (match is not null) request.SubjectId = match.Id;
            }
            db.SaveChanges();
        }
    }
}
