using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services;

try
{
    var directory = Path.Combine(Path.GetTempPath(), "leave-fixed-tests-" + Guid.NewGuid());
    Directory.CreateDirectory(directory);
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        ContentRootPath = directory,
        EnvironmentName = "Development"
    });
    builder.Logging.ClearProviders();
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "AppData", "keys"))).SetApplicationName("QuanLyNghiPhepHV");
    builder.Services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite($"Data Source={directory}/accounts.db"));
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o => { o.Password.RequiredLength = ValidationRules.PasswordMinLength; o.Lockout.MaxFailedAccessAttempts = 5; }).AddEntityFrameworkStores<AuthDbContext>().AddDefaultTokenProviders();
    builder.Services.AddSingleton<PasswordVault>();
    builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, RecoverablePasswordHasher>();
    builder.Services.AddSingleton<AccountValidity>();
    await using var app = builder.Build();
    await AccountSetup.InitializeAsync(app.Services, directory);
    var scopes = app.Services.GetRequiredService<IServiceScopeFactory>();
    var factory = app.Services.GetRequiredService<IDbContextFactory<AuthDbContext>>();
    async Task<AppSession> Session(string name)
    {
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var pf = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var p = await pf.CreateAsync((await users.FindByNameAsync(name))!);
        ((ClaimsIdentity)p.Identity!).AddClaim(new Claim(AccountValidity.ExpiryClaim, DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString()));
        var s = new AppSession(new Provider(p), app.Services.GetRequiredService<AccountValidity>(), factory);
        await s.InitializeAsync();
        return s;
    }
    int passed = 0;
    void Check(bool ok, string label)
    {
        if (!ok)
            throw new Exception(label);
        Console.WriteLine("PASS " + label);
        passed++;
    }
    void Denied(Action action, string label)
    {
        try
        {
            action();
        }
        catch (UserAccessException) { Check(true, label); return; }
        throw new Exception(label);
    }
    void Invalid(Action action, string label)
    {
        try
        {
            action();
        }
        catch (ArgumentException) { Check(true, label); return; }
        catch (InvalidOperationException) { Check(true, label); return; }
        throw new Exception(label);
    }
    async Task BadAsync(Func<Task> action, string label)
    {
        try
        {
            await action();
        }
        catch (ArgumentException) { Check(true, label); return; }
        throw new Exception(label);
    }
    Student Student(string code, string name, string cls) => new()
    {
        MaHocVien = code,
        HoTen = name,
        Lop = cls,
        NgaySinh = "01/01/2005",
        SoDienThoai = "0912345678",
        TrungDoi = "Trung đội " + cls
    };
    LeaveInput Input(string code, DateTime start) => new()
    {
        MaHocVien = code,
        TrungDoi = "A",
        TuThoiDiem = start,
        DenThoiDiem = start.AddHours(1),
        LyDo = "Kiểm thử",
        DiaChiNoiNghi = "Hà Nội"
    };
    var admin = await Session("admin");
    var td = await Session("trungdoi");
    var phong = await Session("phongqlhv");
    var store = new BusinessStore(app.Environment);
    var accounts = new AdminService(scopes, store);
    var repo = new LeaveRepository(app.Environment, scopes, new BusinessStore(app.Environment));
    Check(repo.GetClasses(td).Any(x => x.TenLop == "B10D57" && x.ManagerUserId == td.UserId), "Seed class migrated to immutable manager ID");
    await accounts.Create(admin, "td_other", "TrungDoi", "TestPass@2026!", "Cán bộ B", new DateTime(1990, 1, 1), "0912345678", "123456", "Nam");
    await accounts.Create(admin, "faculty_one", "BoMon", "TestPass@2026!", "", null, "", "", "", "Khoa Một", "FAC01");
    await accounts.Create(admin, "faculty_two", "BoMon", "TestPass@2026!", "", null, "", "", "", "Khoa Hai", "FAC02");
    await accounts.Create(admin, "profile_test", "TrungDoi", "TestPass@2026!", "Ban đầu", new DateTime(1990, 1, 1), "0911111111", "654321", "Nam");
    var edited = (await accounts.List(admin)).Single(x => x.Name == "profile_test");
    await accounts.UpdateProfile(admin, edited.Id, "profile_updated", "TrungDoi", "Đã cập nhật", new DateTime(1992, 2, 2), "0922222222", "654322", "Nữ", "", "");
    edited = (await accounts.List(admin)).Single(x => x.Id == edited.Id);
    Check(edited.Name == "profile_updated" && edited.FullName == "Đã cập nhật" && edited.DateOfBirth == new DateTime(1992, 2, 2) && edited.PhoneNumber == "0922222222" && edited.MajorCode == "654322" && edited.Gender == "Nữ", "Account edit persists all personal fields");
    await BadAsync(() => accounts.UpdateProfile(admin, edited.Id, "profile_updated", "TrungDoi", "A", DateTime.Today, "123", "654322", "Nam", "", ""), "Account edit rejects invalid phone");
    await BadAsync(() => accounts.UpdateProfile(admin, edited.Id, "profile_updated", "TrungDoi", "A", DateTime.Today, "0922222222", "123456", "Nam", "", ""), "Account edit rejects duplicate CAND number");
    await BadAsync(() => accounts.UpdateProfile(admin, edited.Id, "profile_updated", "BoMon", "", null, "", "", "", "Trùng", "FAC01"), "Account edit rejects duplicate faculty ID");
    try { await accounts.UpdateProfile(admin, edited.Id, "rollback_name", "BoMon", "", null, "", "", "", "Khoa Sửa", "FACEDIT", "weak"); throw new Exception("Weak password accepted"); }
    catch (InvalidOperationException) { Check(true, "Invalid password rejects account edit"); }
    Check((await accounts.List(admin)).Single(x => x.Id == edited.Id).Name == "profile_updated", "Failed edit rolls back username and role");
    await accounts.UpdateProfile(admin, edited.Id, "faculty_edited", "BoMon", "", null, "", "", "", "Khoa Sửa", "FACEDIT", "NewPass@2026!");
    edited = (await accounts.List(admin)).Single(x => x.Id == edited.Id);
    Check(edited.Role == "BoMon" && edited.FacultyName == "Khoa Sửa" && edited.FacultyId == "FACEDIT" && edited.FullName == "", "Account edit supports faculty fields and role change");
    using (var editScope = scopes.CreateScope())
    {
        var manager = editScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var editedUser = (await manager.FindByIdAsync(edited.Id))!;
        Check(await manager.CheckPasswordAsync(editedUser, "NewPass@2026!"), "Account edit saves optional new password");
    }
    Check(await accounts.RevealPassword(admin, edited.Id) == "NewPass@2026!", "Admin reveal matches password changed with profile");
    try { await accounts.RevealPassword(td, edited.Id); throw new Exception("Non-admin revealed password"); }
    catch (UserAccessException) { Check(true, "Password reveal denies non-admin server-side"); }
    Check(await accounts.RevealPassword(admin, td.UserId) == "Demo@123", "Initial password and admin reveal both use Demo@123");
    await AccountSetup.InitializeAsync(app.Services, directory);
    Check(await accounts.RevealPassword(admin, edited.Id) == "NewPass@2026!", "Startup does not overwrite passwords changed after initial setup");
    using (var scope = scopes.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(edited.Id))!;
        Check(user.ProtectedPassword != "NewPass@2026!" && !user.ProtectedPassword.Contains("NewPass"), "Recoverable password is encrypted at rest");
        AccountSetup.Check(await users.ChangePasswordAsync(user, "NewPass@2026!", "SelfChanged@123"));
    }
    Check(await accounts.RevealPassword(admin, edited.Id) == "SelfChanged@123", "Self-service change synchronizes password revealed to admin");
    await accounts.Mutate(admin, edited.Id, "password", "AdminReset@123");
    Check(await accounts.RevealPassword(admin, edited.Id) == "AdminReset@123", "Reset synchronizes password revealed to admin");
    Check(repo.GetAudit(admin).Any(x => x.Action == "Xem mật khẩu tài khoản" && x.Detail == edited.Id), "Password reveal records an audit event without plaintext");
    var other = await Session("td_other");
    var f1 = await Session("faculty_one");
    var f2 = await Session("faculty_two");
    repo.CreateClassWithStudents(phong, "A", new List<Student>() { Student("A1", "Trùng Tên", "A"), Student("A2", "Trùng Tên", "A") }, "trungdoi");
    repo.CreateClassWithStudents(phong, "B", new List<Student>() { Student("B1", "Học viên B", "B") }, "td_other");
    Check(repo.GetClasses(td).Any(x => x.TenLop == "A") && !repo.GetClasses(td).Any(x => x.TenLop == "B"), "Manager sees only assigned classes");
    Denied(() => repo.GetStudents(td, "B"), "Foreign class students are denied server-side");
    Check(!repo.GetStudents(td).Any(x => x.Lop == "B"), "Unfiltered student query cannot leak foreign class");
    Denied(() => repo.GetRequests(td, "B"), "Foreign leave list is denied");
    Denied(() => repo.GetAudit(td), "Manager cannot read global audit log");
    Denied(() => repo.CreateClassWithStudents(td, "ILLEGAL", new List<Student>() { Student("X", "X", "ILLEGAL") }), "Manager cannot create classes");
    Denied(() => repo.AddStudent(td, Student("X", "X", "A")), "Manager cannot add student catalog records");
    await BadAsync(async () => { await accounts.Create(admin, "invalidphone", "TrungDoi", "TestPass@2026!", "A", DateTime.Today, "123", "123457", "Nam"); }, "Admin validates profile phone");
    await BadAsync(async () => { await accounts.Create(admin, "duplicatefaculty", "BoMon", "TestPass@2026!", "", null, "", "", "", "Duplicate", "FAC01"); }, "Faculty identifiers cannot be duplicated");
    repo.AddClassSubject(td, "A", "MON1", "Môn Một", f1.UserId);
    repo.AddClassSubject(td, "A", "MON2", "Môn Hai", f2.UserId);
    repo.AddClassSubject(other, "B", "MONB", "Môn B", f2.UserId);
    var subject = repo.GetClassSubjects(td, "A").Single(x => x.Code == "MON1");
    Denied(() => repo.AddClassSubject(td, "B", "BAD", "Bad", f1.UserId), "Cannot modify another class subject catalog");
    Check(repo.GetFacultySubjects(f1).Count == 1, "Faculty sees only its assigned subjects");
    Check(repo.GetClasses(f1).All(x => x.TenLop == "A"), "Faculty sees only assigned classes");
    Denied(() => repo.GetStudents(f1, "B"), "Faculty cannot read another faculty's class roster");
    var start = new DateTime(2020, 1, 1, 7, 0, 0);
    var published = repo.CreateTranhThuLeave(td, new()
    {
        MaHocVien = "A1",
        TenHocVien = "Trùng Tên",
        Lop = "A",
        SubjectId = subject.Id,
        LyDo = "Việc riêng",
        TuThoiDiem = start,
        DenThoiDiem = start.AddHours(1)
    });
    Check(published.TrangThai == LeaveStatuses.WaitingPhong, "Managed class leave waits for Phong QLHV approval");
    Check(repo.GetFacultyRequests(f1).Count == 0, "Faculty cannot see request before Phong approval");
    Denied(() => repo.Review(td, published.Id, true, "", "A"), "Platoon cannot approve at Phong stage");
    Check(published.NguoiDuyet == "" && published.NgayDuyet is null, "Submission does not masquerade as final approval");
    Invalid(() => repo.Review(phong, published.Id, true, "", ""), "Approval requires an explicit selected class");
    Denied(() => repo.Review(phong, published.Id, true, "", "B"), "Approval denies a request outside the selected class");
    Invalid(() => repo.Review(phong, published.Id, false, "", "A"), "Rejection requires a reason");
    published = repo.Review(phong, published.Id, true, "", "A");
    Invalid(() => repo.Review(phong, published.Id, true, "", "A"), "Repeated approval cannot overwrite completed review");
    Check(published.TrangThai == LeaveStatuses.Approved, "Phong QLHV approval makes request approved");
    Check(repo.GetRequests(f1).Single().Id == published.Id && !repo.GetRequests(f2).Any(), "Faculty request visibility respects subject assignment");
    Invalid(() => repo.CreateTranhThuLeave(td, new() { MaHocVien = "A1", TenHocVien = "Trùng Tên", Lop = "A", SubjectId = subject.Id, LyDo = "Trùng", TuThoiDiem = start, DenThoiDiem = start.AddHours(1) }), "Overlapping leave is rejected");
    Invalid(() => repo.CreateTranhThuLeave(td, new() { MaHocVien = "A2", TenHocVien = "Trùng Tên", Lop = "A", SubjectId = repo.GetClassSubjects(other, "B").Single().Id, LyDo = "Sai môn", TuThoiDiem = start, DenThoiDiem = start.AddHours(1) }), "Subject must belong to selected class");
    Denied(() => repo.CreateTranhThuLeave(td, new() { MaHocVien = "B1", TenHocVien = "Học viên B", Lop = "B", SubjectId = subject.Id, LyDo = "Sai quyền", TuThoiDiem = start, DenThoiDiem = start.AddHours(1) }), "Individual leave cannot target another manager's class");
    var batchB = new List<BatchLeaveDraftItem> { new() { MaHocVien = "B1", HoTen = "Học viên B", Lop = "B" } };
    Denied(() => repo.CreateBatchLeaveList(td, "B", LeaveCategories.CuoiTuan, "Cuối tuần", start, start.AddHours(2), batchB), "Weekend batch cannot target another manager's class");
    Denied(() => repo.CreateBatchLeaveList(td, "B", LeaveCategories.LeTet, "Lễ", start, start.AddHours(2), batchB), "Holiday batch cannot target another manager's class");
    Denied(() => repo.Add(td, Input("B1", start.AddDays(1))), "Draft creation cannot target foreign student");
    var draftB = repo.Add(other, Input("B1", start.AddDays(1)));
    Denied(() => repo.Update(td, draftB.Id, Input("B1", start.AddDays(2))), "Foreign request editing denied");
    Denied(() => repo.Delete(td, draftB.Id), "Foreign request deletion denied");
    Denied(() => repo.Review(td, draftB.Id, true, "", "B"), "Foreign review denied");
    Check(!repo.GetRequests(td).Any(x => x.Id == draftB.Id), "Unfiltered request query hides foreign requests");
    using (var ms = new MemoryStream(repo.ExportExcel(td, new[] { draftB, published })))
    using (var zip = new ZipArchive(ms))
    {
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = reader.ReadToEnd();
        var cells = XDocument.Parse(xml).Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value).ToList();
        Check(cells.Contains("A1") && !cells.Contains("B1"), "Export rechecks scope and filters foreign IDs");
        XDocument.Parse(xml);
        Check(true, "Export is valid worksheet XML");
    }
    var draft = repo.Add(td, Input("A1", start.AddDays(3)));
    var updated = Student("A1", "Tên mới", "A");
    repo.UpdateStudent(phong, "A1", updated);
    Check(repo.GetRequests(phong).Single(x => x.Id == published.Id).HoTen == "Trùng Tên", "Editing student preserves approved request snapshot");
    Check(repo.GetRequests(td).Single(x => x.Id == draft.Id).HoTen == "Trùng Tên", "Submitted request preserves original student snapshot");
    Denied(() => repo.Review(td, draft.Id, true, "", "A"), "Class manager cannot approve own submission");
    repo.UpdateStudent(phong, "A1", Student("A1", "Tên mới lần hai", "A"));
    Check(repo.GetRequests(phong).Single(x => x.Id == draft.Id).HoTen == "Trùng Tên", "Submitted request snapshot remains unchanged");
    repo.Review(phong, draft.Id, true, "", "A");
    Invalid(() => repo.Cancel(phong, draft.Id, " "), "Cancel requires a reason");
    Check(repo.Cancel(phong, draft.Id, "Lập nhầm").TrangThai == LeaveStatuses.Cancelled, "Approved request can be cancelled with reason");
    Invalid(() => repo.Cancel(phong, draft.Id, "Lần hai"), "Cancelled request cannot be cancelled again");
    Invalid(() => repo.Delete(td, published.Id), "Approved request cannot be deleted");
    Invalid(() => repo.Update(td, published.Id, Input("A1", start.AddDays(5))), "Approved request cannot be edited");
    var historyCount = repo.GetRequests(phong).Count;
    Check(!repo.DeleteStudent(phong, "A1"), "Student with history is archived instead of deleted");
    Check(!repo.GetStudents(td, "A").Any(x => x.MaHocVien == "A1"), "Archived students disappear from active roster");
    Check(repo.GetStudents(phong, "A", true).Single(x => x.MaHocVien == "A1").IsActive == false, "Staff can see archived student status");
    Check(repo.GetRequests(phong).Count == historyCount, "Archiving preserves every historical request");
    Invalid(() => repo.Add(td, Input("A1", start.AddDays(7))), "Archived student cannot receive new draft");
    Invalid(() => repo.CreateBatchLeaveList(td, "A", LeaveCategories.CuoiTuan, "Test", start.AddDays(7), start.AddDays(7).AddHours(1), new[] { new BatchLeaveDraftItem { MaHocVien = "A1", HoTen = "A", Lop = "A" } }), "Archived student cannot receive batch leave");
    Denied(() => repo.ReactivateStudent(td, "A1"), "Manager cannot restore catalog records");
    repo.ReactivateStudent(phong, "A1");
    Check(repo.GetStudents(td, "A").Any(x => x.MaHocVien == "A1"), "Staff restores archived student");
    repo.AddStudent(phong, Student("UNUSED", "Chưa có hồ sơ", "A"));
    Check(repo.DeleteStudent(phong, "UNUSED"), "Unused student may be permanently deleted");
    repo.AddStudent(phong, Student("LEGACY", "Hồ sơ cũ", "A"));
    using (var db = store.Open())
    {
        db.Database.ExecuteSqlRaw("CREATE TABLE Enrollments (Id INTEGER PRIMARY KEY, StudentCode TEXT); INSERT INTO Enrollments VALUES (1,'LEGACY'); CREATE TABLE Attendance (Id INTEGER PRIMARY KEY, Value TEXT); INSERT INTO Attendance VALUES (1,'old'); CREATE TABLE Results (Id INTEGER PRIMARY KEY, Value TEXT); INSERT INTO Results VALUES (1,'closed');");
    }
    Check(!repo.DeleteStudent(phong, "LEGACY"), "Legacy academic history forces archive, not cascade deletion");
    using (var db = store.Open())
    {
        var connection = db.Database.GetDbConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT (SELECT COUNT(*) FROM Enrollments)+(SELECT COUNT(*) FROM Attendance)+(SELECT COUNT(*) FROM Results)";
        Check(Convert.ToInt32(cmd.ExecuteScalar()) == 3, "Removed module's legacy tables remain unchanged");
    }
    await accounts.Mutate(admin, td.UserId, "rename", "td_renamed");
    Denied(() => td.EnsureValid(), "Rename revokes old authentication session");
    var renamed = await Session("td_renamed");
    Check(repo.GetClasses(renamed).Any(x => x.TenLop == "A"), "Rename preserves class assignment by immutable ID");
    Check(repo.GetClasses(phong).Single(x => x.TenLop == "A").NguoiTao == "td_renamed", "Staff class table displays current manager name");
    var restarted = new LeaveRepository(app.Environment, scopes, new BusinessStore(app.Environment));
    Check(restarted.GetClasses(renamed).Any(x => x.TenLop == "A"), "Renamed manager assignment survives restart");
    Check(restarted.GetStudents(phong, "A", true).Single(x => x.MaHocVien == "LEGACY").IsActive == false, "Archived state survives restart");
    repo.UpdateClassManager(phong, "A", "td_other");
    Denied(() => repo.GetStudents(renamed, "A"), "Reassignment immediately revokes old manager scope");
    Check(repo.GetStudents(other, "A").Any(), "New manager gains reassigned class");
    await BadAsync(() => accounts.Mutate(admin, other.UserId, "delete"), "Cannot delete an account assigned to a class");
    var otherId = other.UserId;
    await accounts.Mutate(admin, otherId, "disable");
    Denied(() => other.EnsureValid(), "Disabling account immediately blocks data access");
    await accounts.Mutate(admin, otherId, "enable");
    other = await Session("td_other");
    Check(repo.GetStudents(other, "A").Any(), "Enabled manager keeps assignments");
    await accounts.Mutate(admin, other.UserId, "password", "NewPassword@2026!");
    Denied(() => other.EnsureValid(), "Password reset revokes prior session");
    await BadAsync(() => accounts.Mutate(admin, admin.UserId, "disable"), "Admin cannot disable own account");
    await BadAsync(() => accounts.Mutate(admin, admin.UserId, "delete"), "Admin cannot delete own account");
    try
    {
        await accounts.List(renamed);
        throw new Exception("Non-admin access");
    }
    catch (UserAccessException) { Check(true, "Non-admin cannot read account administration"); }
    Invalid(() => repo.UpdateClassManager(phong, "A", "unknown"), "Invalid manager assignment rejected");
    Invalid(() => repo.UpdateClassManager(phong, "MISSING", "trungdoi"), "Assignment cannot silently create a new class");
    Check(repo.GetAudit(admin).Any(x => x.User == admin.UserId), "Audit identifies actors by immutable account ID");
    // Schema upgrade and JSON compatibility are tested without using real user data.
    var upgradeDir = Path.Combine(directory, "upgrade");
    Directory.CreateDirectory(upgradeDir);
    var upgradeBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        ContentRootPath = upgradeDir,
        EnvironmentName = "Development"
    });
    var oldRepo = new LeaveRepository(upgradeBuilder.Environment, scopes, new BusinessStore(upgradeBuilder.Environment));
    oldRepo.UpdateClassManager(phong, "B10D57", "td_other");
    using (var legacyDb = new BusinessStore(upgradeBuilder.Environment).Open())
    {
        legacyDb.Database.ExecuteSqlRaw("ALTER TABLE Classes DROP COLUMN ManagerUserId; ALTER TABLE Students DROP COLUMN IsActive;");
    }
    var upgraded = new LeaveRepository(upgradeBuilder.Environment, scopes, new BusinessStore(upgradeBuilder.Environment));
    var liveOther = await Session("td_other");
    Check(upgraded.GetClasses(liveOther).Any(x => x.TenLop == "B10D57"), "Old SQLite schema gains manager ID and retains ownership");
    Check(upgraded.GetStudents(phong, "B10D57", true).All(x => x.IsActive), "Old students default to active during schema upgrade");
    Check(upgraded.GetRequests(phong).Count == 20, "Schema upgrade retains historical leave rows");
    var jsonDir = Path.Combine(directory, "json-migration");
    Directory.CreateDirectory(Path.Combine(jsonDir, "AppData"));
    var legacyData = new RepositoryData { DataVersion = 1, NextId = 88, Classes = new() { new ClassGroup { TenLop = "JSONCLASS", NguoiTao = "td_other" } }, Students = new() { Student("JSON1", "Học viên JSON", "JSONCLASS") } };
    var jsonPath = Path.Combine(jsonDir, "AppData", "du-lieu-nghi-phep.json");
    var originalJson = JsonSerializer.Serialize(legacyData);
    File.WriteAllText(jsonPath, originalJson);
    var jsonBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = jsonDir });
    var migrated = new LeaveRepository(jsonBuilder.Environment, scopes, new BusinessStore(jsonBuilder.Environment));
    Check(migrated.GetStudents(liveOther, "JSONCLASS").Single().MaHocVien == "JSON1", "Legacy JSON migrates student and manager ownership");
    Check(File.ReadAllText(jsonPath) == originalJson, "Migration never overwrites original JSON backup");
    using (var jsonDb = new BusinessStore(jsonBuilder.Environment).Open())
        Check(jsonDb.State.Single().NextLeaveId == 88, "Migration preserves next request ID");
    // Regression cases found during the project cleanup.
    var orphanDir = Path.Combine(directory, "json-orphan");
    Directory.CreateDirectory(Path.Combine(orphanDir, "AppData"));
    File.WriteAllText(Path.Combine(orphanDir, "AppData", "du-lieu-nghi-phep.json"),
        JsonSerializer.Serialize(new RepositoryData { Students = new() { Student("ORPHAN", "Học viên cũ", "UNASSIGNED") } }));
    var orphanBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = orphanDir });
    var orphanRepo = new LeaveRepository(orphanBuilder.Environment, scopes, new BusinessStore(orphanBuilder.Environment));
    Check(orphanRepo.GetClasses(phong).Single().ManagerUserId == "", "Migration never auto-assigns an unknown legacy class");
    var badJsonDir = Path.Combine(directory, "json-invalid");
    Directory.CreateDirectory(Path.Combine(badJsonDir, "AppData"));
    var badJsonPath = Path.Combine(badJsonDir, "AppData", "du-lieu-nghi-phep.json");
    var badJson = JsonSerializer.Serialize(new RepositoryData { Classes = new() { new ClassGroup { TenLop = "DUP" }, new ClassGroup { TenLop = "dup" } } });
    File.WriteAllText(badJsonPath, badJson);
    var badJsonBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = badJsonDir });
    try
    {
        _ = new LeaveRepository(badJsonBuilder.Environment, scopes, new BusinessStore(badJsonBuilder.Environment));
        throw new Exception("Expected invalid JSON rejection");
    }
    catch (InvalidDataException) { Check(true, "Duplicate legacy JSON keys are rejected before persistence"); }
    using (var badJsonDb = new BusinessStore(badJsonBuilder.Environment).Open())
        Check(!badJsonDb.Classes.Any() && !badJsonDb.State.Any() && File.ReadAllText(badJsonPath) == badJson,
            "Rejected JSON migration preserves its source and leaves no partial records");
    await BadAsync(() => accounts.Mutate(admin, otherId, "rename", "x"), "Rename enforces the same username length as account creation");
    await BadAsync(() => accounts.Mutate(admin, otherId, "password", new string('a', 257)), "Admin password reset enforces the server length limit");
    List<Student> Csv(string text)
    {
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return ExcelExporter.ParseClassExcel(input, "A");
    }
    void BadFile(Action action, string label)
    {
        try
        {
            action();
        }
        catch (InvalidDataException) { Check(true, label); return; }
        throw new Exception(label);
    }
    byte[] RewriteWorkbook(byte[] bytes, Action<Dictionary<string, string>> edit)
    {
        var files = new Dictionary<string, string>();
        using (var input = new MemoryStream(bytes))
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                files[entry.FullName] = reader.ReadToEnd();
            }
        edit(files);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var item in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(item.Key).Open());
                writer.Write(item.Value);
            }
        return output.ToArray();
    }
    var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
        Check(ValidationRules.BirthDate("04/03/2005") == "04/03/2005", "Birth date uses Vietnamese day/month order on an English machine");
        var csv = Csv("\uFEFFSTT,Tên,Lớp,Ngày sinh,Số điện thoại\n1,\"Nguyễn, Văn A\",A,04/03/2005,0912345678");
        Check(csv.Single().HoTen == "Nguyễn, Văn A" && csv.Single().NgaySinh == "04/03/2005", "CSV preserves quoted commas, UTF-8 BOM and date order");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = originalCulture; }
    Check(Csv("STT;Tên;Lớp;Ngày sinh;Số điện thoại\n1;\"Nguyễn, An\";A;2005-03-04;0912345678").Single().HoTen == "Nguyễn, An", "CSV separator is detected outside quoted fields");
    Check(Csv("STT,Tên,Lớp,Ngày sinh,Số điện thoại\n1,\"Nguyễn \"\"An\"\"\",A,04/03/2005,0912345678").Single().HoTen == "Nguyễn \"An\"", "CSV escaped quotes are preserved");
    Check(Csv("STT,Tên,Lớp,Ngày sinh,Số điện thoại\n1,\"Nguyễn\nAn\",A,04/03/2005,0912345678").Single().HoTen == "Nguyễn\nAn", "Quoted multiline CSV field is parsed as one record");
    Check(Csv("Nguyễn An,A,04/03/2005,0912345678").Single().HoTen == "Nguyễn An", "Four-column CSV without headers remains supported");
    BadFile(() => Csv("STT,Tên,Lớp,Ngày sinh,Số điện thoại\n1,\"broken,A,04/03/2005,0912345678"), "Malformed CSV quoting is rejected");
    var missing = Csv("STT,Tên,Lớp,Ngày sinh,Số điện thoại\n1,Nguyễn An,A,,").Single();
    Check(missing.NgaySinh == "" && missing.SoDienThoai == "", "Import never fabricates missing birth dates or phone numbers");
    BadFile(() => Csv("STT,Tên,Lớp\n1,Nguyễn An,A"), "Incomplete spreadsheet headers are rejected");
    BadFile(() => Csv("STT,Tên,Lớp,Ngày sinh,Số điện thoại\n1,,A,04/03/2005,0912345678"), "Partially entered row cannot silently lose a student");
    BadFile(() => Csv("STT,Tên,Lớp,Ngày sinh,Số điện thoại\n" + string.Join("\n", Enumerable.Range(1, 1001).Select(i => $"{i},Nguyễn An,A,04/03/2005,0912345678"))), "Import enforces the 1000-student limit");
    var template = ExcelExporter.BuildClassTemplate("A", new[] { Student("TEMP", "Nguyễn An", "A") });
    using (var input = new MemoryStream(template))
        Check(ExcelExporter.ParseClassExcel(input).Single().HoTen == "Nguyễn An", "Generated class template round-trips through the Excel importer");
    var noHeader = ExcelExporter.BuildTable("Data", new List<string[]> { new[] { "Nguyễn An", "A", "04/03/2005", "0912345678" } });
    using (var input = new MemoryStream(noHeader))
        Check(ExcelExporter.ParseClassExcel(input).Single().HoTen == "Nguyễn An", "Four-column XLSX without headers is parsed correctly");
    XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var nativeDate = RewriteWorkbook(template, files => { var doc = XDocument.Parse(files["xl/worksheets/sheet1.xml"]); var cell = doc.Descendants(main + "c").Single(x => x.Attribute("r")?.Value == "D2"); cell.SetAttributeValue("t", null); cell.RemoveNodes(); cell.Add(new XElement(main + "v", new DateTime(2005, 3, 4).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture))); files["xl/worksheets/sheet1.xml"] = doc.ToString(); });
    using (var input = new MemoryStream(nativeDate))
        Check(ExcelExporter.ParseClassExcel(input).Single().NgaySinh == "04/03/2005", "Native Excel serial birth dates are normalized");
    var date1904 = RewriteWorkbook(nativeDate, files => { var book = XDocument.Parse(files["xl/workbook.xml"]); book.Root!.AddFirst(new XElement(main + "workbookPr", new XAttribute("date1904", "1"))); files["xl/workbook.xml"] = book.ToString(); var doc = XDocument.Parse(files["xl/worksheets/sheet1.xml"]); doc.Descendants(main + "c").Single(x => x.Attribute("r")?.Value == "D2").Element(main + "v")!.Value = (new DateTime(2005, 3, 4).ToOADate() - 1462).ToString(System.Globalization.CultureInfo.InvariantCulture); files["xl/worksheets/sheet1.xml"] = doc.ToString(); });
    using (var input = new MemoryStream(date1904))
        Check(ExcelExporter.ParseClassExcel(input).Single().NgaySinh == "04/03/2005", "Excel 1904 date system is handled without shifting birth dates");
    var sheetOrder = RewriteWorkbook(template, files => { files["xl/worksheets/sheet2.xml"] = files["xl/worksheets/sheet1.xml"]; files["xl/worksheets/sheet1.xml"] = "<invalid/>"; files["xl/_rels/workbook.xml.rels"] = files["xl/_rels/workbook.xml.rels"].Replace("worksheets/sheet1.xml", "worksheets/sheet2.xml"); });
    using (var input = new MemoryStream(sheetOrder))
        Check(ExcelExporter.ParseClassExcel(input).Single().HoTen == "Nguyễn An", "Importer follows workbook sheet order instead of sorting ZIP filenames");
    var badXml = RewriteWorkbook(template, files => files["xl/worksheets/sheet1.xml"] = "<!DOCTYPE x [<!ENTITY evil SYSTEM 'file:///unused'>]><x>&evil;</x>");
    BadFile(() => { using var input = new MemoryStream(badXml); ExcelExporter.ParseClassExcel(input); }, "External XML entities are prohibited");
    var data = published.Clone();
    data.LyDo = "=SUM(1,2)";
    data.HoTen = "An\u0001B";
    using (var input = new MemoryStream(ExcelExporter.Build(new[] { data })))
    using (var zip = new ZipArchive(input))
    {
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var doc = XDocument.Parse(reader.ReadToEnd());
        var rows = doc.Descendants(main + "row").ToList();
        Check(rows[0].Elements(main + "c").Count() == rows[1].Elements(main + "c").Count(), "Every exported data column matches its header");
        Check(rows[0].Descendants(main + "t").Select(x => x.Value).Contains("Mã học viên"), "Student ID column has the correct Excel header");
        Check(!doc.Descendants(main + "f").Any() && doc.Descendants(main + "t").Any(x => x.Value == "=SUM(1,2)"), "User-entered spreadsheet formulas are exported as literal text");
        Check(doc.Descendants(main + "t").Any(x => x.Value == "AnB"), "Invalid XML control characters cannot corrupt the exported workbook");
    }
    var liveManager = await Session("td_other");
    repo.AddStudent(phong, Student("CASE", "Kiểm tra chữ hoa", "a"));
    Check(repo.GetStudents(phong, "A").Any(x => x.MaHocVien == "CASE" && x.Lop == "A"), "Student creation canonicalizes class foreign keys");
    repo.UpdateStudent(phong, "case", Student("case", "Đã sửa", "a"));
    Check(repo.GetStudents(phong, "A").Single(x => x.MaHocVien == "CASE").HoTen == "Đã sửa", "Case-only student ID input does not break references");
    Check(repo.GetClassSubjects(liveManager, "a").Any(), "Class subject queries accept equivalent class casing");
    Invalid(() => repo.AddStudent(phong, new Student { MaHocVien = "BAD-DATE", HoTen = "Sai ngày", Lop = "A", NgaySinh = "31/02/2005", SoDienThoai = "0912345678" }), "Student date validation rejects impossible calendar dates");
    Invalid(() => repo.AddStudent(phong, new Student { MaHocVien = "BAD-PHONE", HoTen = "Sai số", Lop = "A", NgaySinh = "01/01/2005", SoDienThoai = "abc" }), "Student phone validation is enforced server-side");
    Invalid(() => repo.CreateClassWithStudents(phong, "INVALID_CLASS", new List<Student> { Student("VALID-ROW", "Hợp lệ", "INVALID_CLASS"), new() { MaHocVien = "BAD-ROW", HoTen = "Sai", Lop = "INVALID_CLASS", NgaySinh = "31/02/2005", SoDienThoai = "0912345678" } }, "td_other"), "A bad class row cancels the entire create operation");
    Check(!repo.GetClasses(phong).Any(x => x.TenLop == "INVALID_CLASS") && !repo.GetStudents(phong).Any(x => x.MaHocVien == "VALID-ROW"), "Failed class creation leaves no partial class or student records");
    Invalid(() => repo.CreateClassWithStudents(phong, "DUP_CLASS", new List<Student> { Student("A1", "Trùng mã", "DUP_CLASS") }, "td_other"), "Explicit duplicate student codes are rejected rather than silently changed");
    Denied(() => repo.Add(liveManager, new LeaveInput { MaHocVien = "__manual__", LyDo = "Bypass", DiaChiNoiNghi = "A", TuThoiDiem = start, DenThoiDiem = start.AddHours(1) }), "Legacy manual-student path cannot bypass staff-only roster management");
    repo.CreateClassWithStudents(phong, "DEST", new List<Student> { Student("DEST1", "Đích", "DEST") }, "td_other");
    var pending = repo.Add(liveManager, Input("CASE", start.AddDays(20)));
    Invalid(() => repo.UpdateStudent(phong, "CASE", Student("CASE", "Chuyển lớp", "DEST")), "Class transfer is blocked while a leave request is pending");
    Check(repo.GetStudents(phong, "A").Any(x => x.MaHocVien == "CASE"), "Failed transfer keeps original roster membership");
    repo.Cancel(liveManager, pending.Id, "Rút đơn trước khi chuyển lớp");
    repo.UpdateStudent(phong, "CASE", Student("CASE", "Chuyển lớp", "DEST"));
    Check(repo.GetStudents(phong, "DEST").Any(x => x.MaHocVien == "CASE"), "Class transfer succeeds after pending request is resolved");
    var secondRepo = new LeaveRepository(app.Environment, scopes, new BusinessStore(app.Environment));
    repo.AddStudent(phong, Student("INSTANCE1", "Phiên một", "A"));
    secondRepo.AddStudent(phong, Student("INSTANCE2", "Phiên hai", "A"));
    Check(repo.GetStudents(phong, "A").Count(x => x.MaHocVien.StartsWith("INSTANCE")) == 2, "Two repository instances cannot overwrite each other's changes");
    Parallel.For(0, 12, i => { (i % 2 == 0 ? repo : secondRepo).AddStudent(phong, Student("PAR" + i, "Song song " + i, "A")); });
    Check(repo.GetStudents(phong, "A").Count(x => x.MaHocVien.StartsWith("PAR")) == 12, "Parallel roster updates retain every student");
    using (var db = store.Open())
        db.Database.ExecuteSqlRaw("CREATE TRIGGER FailReviewRequest BEFORE INSERT ON Requests WHEN NEW.LyDo='FAIL_SAVE' BEGIN SELECT RAISE(ABORT,'temporary test fault'); END;");
    int beforeId;
    using (var db = store.Open())
        beforeId = db.State.Single().NextLeaveId;
    var failing = Input("A2", start.AddDays(30));
    failing.LyDo = "FAIL_SAVE";
    try
    {
        repo.Add(liveManager, failing);
        throw new Exception("Expected SQLite fault");
    }
    catch (DbUpdateException) { Check(true, "Storage failure is surfaced without claiming success"); }
    using (var db = store.Open())
        Check(db.State.Single().NextLeaveId == beforeId && !db.Requests.Any(x => x.LyDo == "FAIL_SAVE"), "Failed write rolls back the request and ID counter");
    Check(repo.Add(liveManager, Input("A2", start.AddDays(30))).Id == beforeId, "Repository recovers correctly after a failed database transaction");
    repo.CreateClassWithStudents(phong, "ZZ", new List<Student>() { Student("ZZ1", "Học viên ZZ", "ZZ") }, "td_other");
    repo.RenameClass(phong, "ZZ", "ZZ2");
    Check(repo.GetStudents(phong, "ZZ2").Single().MaHocVien == "ZZ1" && repo.GetClasses(phong).Any(x => x.TenLop == "ZZ2") && repo.GetClasses(phong).All(x => x.TenLop != "ZZ"), "Class rename moves students");
    Invalid(() => repo.RenameClass(phong, "ZZ2", "A"), "Class rename rejects an existing name");
    Denied(() => repo.RenameClass(liveManager, "ZZ2", "ZZ3"), "Platoon cannot rename classes");
    repo.DeleteClass(phong, "ZZ2");
    Check(repo.GetClasses(phong).All(x => x.TenLop != "ZZ2"), "Class without requests can be deleted");
    Invalid(() => repo.DeleteClass(phong, "A"), "Class with requests cannot be deleted");
    repo.AddHoliday(phong, "Nghỉ Quốc khánh", start, start.AddDays(3));
    Check(repo.GetHolidays(liveManager).Count(x => x.Name == "Nghỉ Quốc khánh") == 1, "Holiday catalog readable by platoon");
    Denied(() => repo.AddHoliday(liveManager, "Trái phép", start, start.AddDays(1)), "Platoon cannot edit holiday catalog");
    Invalid(() => repo.AddHoliday(phong, "Sai ngày", start, start.AddDays(-1)), "Holiday end must follow start");
    repo.DeleteHoliday(phong, repo.GetHolidays(phong).Single(x => x.Name == "Nghỉ Quốc khánh").Id);
    var generatedOk = Enumerable.Range(0, 20).Select(_ => AccountSetup.NewPassword()).All(w => w.Length == 10 && w.Any(char.IsUpper) && w.Any(char.IsLower) && w.Any(char.IsDigit) && w.Any(c => !char.IsLetterOrDigit(c)) && w.Distinct().Count() >= 4 && !w.Any(c => "0O1lI".Contains(c)));
    Check(generatedOk, "Generated passwords are 10 chars, meet policy and avoid ambiguous characters");
    // Backups must retain the keys needed to reveal encrypted passwords on another host.
    using (var sourceDb = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={directory}/accounts.db"))
    using (var targetDb = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={directory}/AppData/accounts.db"))
    {
        sourceDb.Open(); targetDb.Open(); sourceDb.BackupDatabase(targetDb);
    }
    var backups = new BackupService(app.Environment);
    var backupFile = backups.CreateBackup(admin);
    using (var archive = ZipFile.OpenRead(backupFile))
        Check(archive.Entries.Any(x => x.FullName.StartsWith("keys/key-") && x.FullName.EndsWith(".xml")), "Backup includes encryption keys");
    var restoreDirectory = Path.Combine(directory, "restore-fixture");
    Directory.CreateDirectory(restoreDirectory);
    var restoreBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = restoreDirectory });
    var restoreService = new BackupService(restoreBuilder.Environment);
    using (var input = File.OpenRead(backupFile)) restoreService.StageRestore(admin, input);
    BackupService.ApplyPendingRestore(Path.Combine(restoreDirectory, "AppData"));
    Check(File.Exists(Path.Combine(restoreDirectory, "AppData", "accounts.db")) && Directory.GetFiles(Path.Combine(restoreDirectory, "AppData", "keys"), "key-*.xml").Length > 0, "Restore retains database and password encryption keys");
    var restoredProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(restoreDirectory, "AppData", "keys")), configuration => configuration.SetApplicationName("QuanLyNghiPhepHV"));
    using (var scope = scopes.CreateScope())
    {
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(edited.Id))!;
        Check(new PasswordVault(restoredProvider).Reveal(user) == "AdminReset@123", "Restored keys decrypt the existing password on another host");
    }
    Console.WriteLine($"ALL {passed} BUSINESS CHECKS PASSED");
}
catch (Exception ex) { Console.Error.WriteLine("TEST FAILURE: " + ex); Environment.ExitCode = 1; }
sealed class Provider(ClaimsPrincipal p) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(p));
}






