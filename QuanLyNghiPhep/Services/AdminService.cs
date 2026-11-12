using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services;

public sealed record AccountRow(string Id, string Name, string Role, bool Enabled, bool Locked, string FullName, DateTime? DateOfBirth, string Gender, string PhoneNumber, string MajorCode, string FacultyName = "", string FacultyId = "");
public sealed class AdminService(IServiceScopeFactory scopes, BusinessStore store, ILogger<AdminService>? logger = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static void Check(AppSession s)
    {
        s.EnsureValid();
        if (s.Role != UserRole.Admin)
            throw new UserAccessException("Chỉ quản trị viên được quản lý tài khoản.");
    }
    private static string Password() => AccountSetup.NewPassword();
    public async Task<List<AccountRow>> List(AppSession s)
    {
        Check(s);

        using var scope = scopes.CreateScope();

        var users = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        var result = new List<AccountRow>();

        foreach (var user in await users.Users
            .OrderBy(x => x.UserName)
            .ToListAsync())
        {
            var roles = await users.GetRolesAsync(user);

            result.Add(new AccountRow(
                user.Id,
                user.UserName ?? "",
                string.Join(",", roles),
                user.IsEnabled,
                user.LockoutEnd > DateTimeOffset.UtcNow,
                user.FullName ?? "",
                user.DateOfBirth,
                user.Gender ?? "",
                user.PhoneNumber ?? "",
                user.MajorCode ?? "", user.FacultyName, user.FacultyId));
        }

        return result;
    }
    public async Task<string> RevealPassword(AppSession session, string id)
    {
        Check(session);
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id) ?? throw new ArgumentException("Không tìm thấy tài khoản.");
        var plain = scope.ServiceProvider.GetRequiredService<PasswordVault>().Reveal(user);
        if (users.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash!, plain) == PasswordVerificationResult.Failed)
            throw new ArgumentException("Bản mật khẩu đã cũ. Hãy đặt lại mật khẩu.");
        Check(session);
        Log(session, "Xem mật khẩu tài khoản", id);
        return plain;
    }
    public async Task<List<string>> ListTrungDoiUsers(AppSession s)
    {
        s.EnsureValid();
        if (s.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên hoặc Admin mới được xem danh sách tài khoản Trung đội.");
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var inRole = await users.GetUsersInRoleAsync(UserRole.TrungDoi.ToString());
        return inRole
            .Where(u => u.IsEnabled && !string.IsNullOrWhiteSpace(u.UserName))
            .Select(u => u.UserName!)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    public async Task<string> Create(
    AppSession s,
    string name,
    string role,
    string password,
    string fullName,
    DateTime? dateOfBirth,
    string phoneNumber,
    string majorCode,
    string gender,
    string facultyName = "",
    string facultyId = "")
    {
        Check(s);

        var isFaculty = role == UserRole.BoMon.ToString();

        fullName = fullName.Trim();
        phoneNumber = phoneNumber.Trim();
        majorCode = majorCode.Trim();
        gender = gender.Trim();

        facultyName = facultyName.Trim();
        facultyId = facultyId.Trim().ToUpperInvariant();

        ValidateProfile(isFaculty, fullName, dateOfBirth, phoneNumber, majorCode, gender, facultyName, facultyId);

        await gate.WaitAsync();

        try
        {
            Check(s);

            using var scope = scopes.CreateScope();

            var users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var db = scope.ServiceProvider
                .GetRequiredService<AuthDbContext>();

            await using var tx =
                await db.Database.BeginTransactionAsync();
            if (isFaculty)
            {
                if (await users.Users.AnyAsync(u =>
                    u.FacultyId.ToUpper() == facultyId))
                {
                    throw new ArgumentException(
                        "ID khoa này đã được sử dụng. Không thể tạo tài khoản");
                }
            }
            else
            {
                if (await users.Users.AnyAsync(u => u.MajorCode == majorCode))
                {
                    throw new ArgumentException(
                        "Số hiệu CAND này đã được sử dụng. Không thể tạo tài khoản.");
                }
            }
            var actualPassword =
                await CreateUser(users, name, role, password);

            var user = await users.FindByNameAsync(name.Trim())
                ?? throw new InvalidOperationException(
                    "Không tìm thấy tài khoản vừa tạo.");
            if (isFaculty)
            {
                user.FacultyName = facultyName;
                user.FacultyId = facultyId;

                user.FullName = "";
                user.DateOfBirth = null;
                user.PhoneNumber = null;
                user.MajorCode = "";
                user.Gender = "";
            }
            else
            {
                user.FullName = fullName;
                user.DateOfBirth = dateOfBirth?.Date;
                user.PhoneNumber = phoneNumber;
                user.MajorCode = majorCode;
                user.Gender = gender;
                user.FacultyName = "";
                user.FacultyId = "";
            }
            AccountSetup.Check(await users.UpdateAsync(user));

            await tx.CommitAsync();

            Log(s, "Tạo tài khoản", name);

            return actualPassword;
        }
        finally
        {
            gate.Release();
        }
    }
    private static void ValidateProfile(bool isFaculty, string fullName, DateTime? dateOfBirth, string phoneNumber, string majorCode, string gender, string facultyName, string facultyId)
    {
        if (isFaculty)
        {
            if (facultyName.Length is < 1 or > 150)
                throw new ArgumentException(
                    "Vui lòng nhập tên khoa, tối đa 150 ký tự.");

            if (facultyId.Length is < 1 or > 40)
                throw new ArgumentException(
                    "Vui lòng nhập ID khoa, tối đa 40 ký tự.");
        }
        else
        {
            if (fullName.Length is < 1 or > 150)
                throw new ArgumentException(
                    "Vui lòng nhập họ tên, tối đa 150 ký tự.");

            if (!dateOfBirth.HasValue
                || dateOfBirth.Value.Date > DateTime.Today
                || dateOfBirth.Value.Year < 1900)
                throw new ArgumentException(
                    "Vui lòng nhập ngày sinh hợp lệ.");

            if (!Regex.IsMatch(phoneNumber, @"\A0[0-9]{9}\z"))
                throw new ArgumentException(
                    "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.");

            if (!Regex.IsMatch(majorCode, @"\A[0-9]{6}\z"))
                throw new ArgumentException(
                    "Số hiệu CAND phải gồm đúng 6 chữ số.");

            if (gender is not ("Nam" or "Nữ" or "Khác"))
                throw new ArgumentException(
                    "Vui lòng chọn giới tính.");
        }


    }

    public async Task UpdateProfile(AppSession s, string id, string name, string role,
        string fullName, DateTime? dateOfBirth, string phoneNumber, string majorCode,
        string gender, string facultyName, string facultyId, string password = "")
    {
        Check(s);
        name = name.Trim(); fullName = fullName.Trim(); phoneNumber = phoneNumber.Trim();
        majorCode = majorCode.Trim(); gender = gender.Trim(); facultyName = facultyName.Trim();
        facultyId = facultyId.Trim().ToUpperInvariant();
        if (name.Length is < 3 or > 100) throw new ArgumentException("Tên tài khoản phải từ 3–100 ký tự.");
        if (!Enum.TryParse<UserRole>(role, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != role)
            throw new ArgumentException("Vai trò không hợp lệ.");
        if (password.Length > ValidationRules.PasswordMaxLength) throw new ArgumentException("Mật khẩu tối đa 256 ký tự.");
        var isFaculty = parsed == UserRole.BoMon;
        ValidateProfile(isFaculty, fullName, dateOfBirth, phoneNumber, majorCode, gender, facultyName, facultyId);
        await gate.WaitAsync();
        try
        {
            Check(s);
            if (id == s.UserId) throw new ArgumentException("Không thay đổi tài khoản quản trị viên đang đăng nhập.");
            using var scope = scopes.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var connectionOptions = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false };
            db.Database.SetConnectionString(connectionOptions.ToString());
            await db.Database.OpenConnectionAsync();
            using var catalog = store.Open();
            await db.Database.ExecuteSqlRawAsync("ATTACH DATABASE {0} AS catalog", catalog.Database.GetDbConnection().DataSource);
            await using var tx = await db.Database.BeginTransactionAsync();
            var user = await users.FindByIdAsync(id) ?? throw new ArgumentException("Không tìm thấy tài khoản.");
            var roles = await users.GetRolesAsync(user);
            var duplicate = await users.FindByNameAsync(name);
            if (duplicate is not null && duplicate.Id != id) throw new ArgumentException("Tên tài khoản đã tồn tại.");
            if (isFaculty && await users.Users.AnyAsync(u => u.Id != id && u.FacultyId.ToUpper() == facultyId))
                throw new ArgumentException("ID khoa này đã được sử dụng.");
            if (!isFaculty && await users.Users.AnyAsync(u => u.Id != id && u.MajorCode == majorCode))
                throw new ArgumentException("Số hiệu CAND này đã được sử dụng.");
            if (!roles.Contains(role))
            {
                if (roles.Contains(nameof(UserRole.Admin)))
                {
                    var admins = await users.GetUsersInRoleAsync(nameof(UserRole.Admin));
                    if (!admins.Any(u => u.Id != id && u.IsEnabled && !(u.LockoutEnd > DateTimeOffset.UtcNow)))
                        throw new ArgumentException("Phải giữ ít nhất một quản trị viên hoạt động.");
                }
                using var business = store.Open();
                if (business.Classes.Any(c => c.ManagerUserId == id || (c.ManagerUserId == "" && c.NguoiTao == user.UserName))
                    || business.ClassSubjects.Any(c => c.FacultyUserId == id) || BusinessStore.HasLegacyAccountHistory(business, id))
                    throw new ArgumentException("Tài khoản đang được phân công lớp hoặc môn học. Hãy gỡ phân công trước khi đổi vai trò.");
                AccountSetup.Check(await users.RemoveFromRolesAsync(user, roles));
                AccountSetup.Check(await users.AddToRoleAsync(user, role));
            }
            user.FullName = isFaculty ? "" : fullName;
            user.DateOfBirth = isFaculty ? null : dateOfBirth?.Date;
            user.PhoneNumber = isFaculty ? null : phoneNumber;
            user.MajorCode = isFaculty ? "" : majorCode;
            user.Gender = isFaculty ? "" : gender;
            user.FacultyName = isFaculty ? facultyName : "";
            user.FacultyId = isFaculty ? facultyId : "";
            AccountSetup.Check(await users.SetUserNameAsync(user, name));
            if (password.Length > 0)
                AccountSetup.Check(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password));
            AccountSetup.Check(await users.UpdateSecurityStampAsync(user));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE catalog.ClassSubjects SET Faculty={user.FacultyName} WHERE FacultyUserId={id}");
            await tx.CommitAsync();
            Log(s, "Đổi thông tin tài khoản", id);
        }
        finally { gate.Release(); }
    }
    private static async Task<string> CreateUser(UserManager<ApplicationUser> users, string name, string role, string password)
    {
        if (!Enum.TryParse<UserRole>(role, out var r) || !Enum.IsDefined(r) || r.ToString() != role)
            throw new ArgumentException("Vai trò không hợp lệ.");
        name = (name ?? "").Trim();
        if (name.Length is < 3 or > 100)
            throw new ArgumentException("Tên tài khoản phải từ 3–100 ký tự.");
        if (await users.FindByNameAsync(name) is not null)
            throw new ArgumentException($"Tài khoản {name} đã tồn tại.");
        password = string.IsNullOrEmpty(password) ? Password() : password;
        if (password.Length > ValidationRules.PasswordMaxLength)
            throw new ArgumentException("Mật khẩu tối đa 256 ký tự.");
        var u = new ApplicationUser { UserName = name, LockoutEnabled = true };
        AccountSetup.Check(await users.CreateAsync(u, password));
        AccountSetup.Check(await users.AddToRoleAsync(u, role));
        return password;
    }
    public async Task Mutate(AppSession s, string id, string action, string value = "")
    {
        Check(s);
        await gate.WaitAsync();
        try
        {
            Check(s);
            using var scope = scopes.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var u = await users.FindByIdAsync(id) ?? throw new ArgumentException("Không tìm thấy tài khoản.");
            if (id == s.UserId && action is "disable" or "delete" or "rename" or "password")
                throw new ArgumentException("Dùng trang đổi mật khẩu cho chính mình; không khóa, xóa hoặc đổi tên tài khoản đang dùng.");
            if (action is "disable" or "delete" && await users.IsInRoleAsync(u, "Admin"))
            {
                var admins = await users.GetUsersInRoleAsync("Admin");
                if (!admins.Any(x => x.Id != id && x.IsEnabled && !(x.LockoutEnd > DateTimeOffset.UtcNow)))
                    throw new ArgumentException("Phải giữ ít nhất một quản trị viên hoạt động.");
            }
            switch (action)
            {
                case "enable":
                    u.IsEnabled = true;
                    AccountSetup.Check(await users.UpdateAsync(u));
                    AccountSetup.Check(await users.SetLockoutEndDateAsync(u, null));
                    AccountSetup.Check(await users.ResetAccessFailedCountAsync(u));
                    break;
                case "disable":
                    u.IsEnabled = false;
                    AccountSetup.Check(await users.UpdateAsync(u));
                    break;
                case "rename":
                    if (string.IsNullOrWhiteSpace(value) || value.Trim().Length is < 3 or > 100)
                        throw new ArgumentException("Tên tài khoản phải từ 3–100 ký tự.");
                    AccountSetup.Check(await users.SetUserNameAsync(u, value.Trim()));
                    break;
                case "password":
                    if (string.IsNullOrEmpty(value) || value.Length > ValidationRules.PasswordMaxLength)
                        throw new ArgumentException("Vui lòng nhập mật khẩu mới, tối đa 256 ký tự.");
                    AccountSetup.Check(await users.ResetPasswordAsync(u, await users.GeneratePasswordResetTokenAsync(u), value));
                    break;
                case "delete":
                    using (var business = store.Open())
                        if (business.ClassSubjects.Any(x => x.FacultyUserId == id)
                            || business.Classes.Any(x => x.ManagerUserId == id || (x.ManagerUserId == "" && x.NguoiTao == u.UserName)) || BusinessStore.HasLegacyAccountHistory(business, id)
                            || business.Audit.Any(x => x.User == id || x.User == u.UserName || x.Detail == "rename: " + id) || business.Requests.Any(x => x.NguoiTao == u.UserName || x.NguoiDuyet == u.UserName || x.NguoiDuyetPhong == u.UserName || x.NguoiDuyetTrungDoi == u.UserName))
                            throw new ArgumentException("Tài khoản đã có lịch sử. Hãy khóa để giữ nguyên hồ sơ.");
                    AccountSetup.Check(await users.DeleteAsync(u));
                    break;
                default:
                    throw new ArgumentException("Thao tác không hợp lệ.");
            }
            if (action != "delete")
                AccountSetup.Check(await users.UpdateSecurityStampAsync(u));
            await tx.CommitAsync();
            Log(s, "Quản lý tài khoản", $"{action}: {id}");
        }
        finally { gate.Release(); }
    }
    private void Log(AppSession s, string action, string detail)
    {
        try
        {
            using var db = store.Open();
            db.Audit.Add(new AuditEntry { User = s.UserId, Action = action, Detail = detail });
            db.SaveChanges();
        }
        catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            logger?.LogError(ex, "Không ghi được nhật ký {Action} cho tài khoản {UserId}; thay đổi tài khoản đã được lưu.", action, s.UserId);
        }
    }
}
