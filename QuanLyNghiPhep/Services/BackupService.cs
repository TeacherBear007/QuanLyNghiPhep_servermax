using System.IO.Compression;
using Microsoft.Data.Sqlite;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services;

// Sao lưu: VACUUM INTO tạo bản sao nhất quán khi app đang chạy. Khôi phục: đặt tệp chờ, áp dụng khi khởi động lại.
public sealed class BackupService(IWebHostEnvironment env)
{
    private static readonly string[] DbFiles = ["accounts.db", "business.db"];
    private string Dir => Path.Combine(env.ContentRootPath, "AppData");
    private string BackupDir => Path.Combine(Dir, "backups");
    private string PendingDir => Path.Combine(Dir, "restore-pending");
    private readonly SemaphoreSlim gate = new(1, 1);

    private static void RequireAdmin(AppSession s)
    {
        s.EnsureValid();
        if (s.Role != UserRole.Admin)
            throw new UserAccessException("Chỉ Admin được sao lưu/khôi phục.");
    }

    public List<FileInfo> List()
        => Directory.Exists(BackupDir) ? new DirectoryInfo(BackupDir).GetFiles("*.zip").OrderByDescending(x => x.CreationTime).ToList() : new();

    public bool HasPendingRestore => Directory.Exists(PendingDir);

    public string CreateBackup(AppSession session) { RequireAdmin(session); return CreateBackupCore("sao-luu"); }

    public string CreateBackupCore(string prefix)
    {
        gate.Wait();
        try
        {
            Directory.CreateDirectory(BackupDir);
            var temp = Path.Combine(Path.GetTempPath(), "qlnp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                foreach (var name in DbFiles)
                {
                    var src = Path.Combine(Dir, name);
                    if (!File.Exists(src)) continue;
                    var dst = Path.Combine(temp, name);
                    using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = src, Mode = SqliteOpenMode.ReadWrite }.ToString());
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"VACUUM INTO '{dst.Replace("'", "''")}'";
                    cmd.ExecuteNonQuery();
                }
                CopyKeys(Path.Combine(Dir, "keys"), Path.Combine(temp, "keys"));
                var zipPath = Path.Combine(BackupDir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                ZipFile.CreateFromDirectory(temp, zipPath);
                return zipPath;
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }
        finally { gate.Release(); }
    }

    public byte[] Read(AppSession session, string fileName)
    {
        RequireAdmin(session);
        var path = Path.Combine(BackupDir, Path.GetFileName(fileName));
        return File.Exists(path) ? File.ReadAllBytes(path) : throw new ArgumentException("Không tìm thấy bản sao lưu.");
    }

    public void StageRestore(AppSession session, Stream zip)
    {
        RequireAdmin(session);
        var temp = Path.Combine(Dir, "restore-staging");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        try
        {
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
            {
                foreach (var name in DbFiles)
                {
                    var entry = archive.GetEntry(name) ?? throw new ArgumentException($"Tệp sao lưu thiếu {name}.");
                    entry.ExtractToFile(Path.Combine(temp, name));
                }
                foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("keys/", StringComparison.Ordinal)))
                {
                    var name = entry.FullName[5..];
                    if (Path.GetFileName(name) != name || !name.StartsWith("key-", StringComparison.Ordinal) || !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                        || name.Contains('\\') || entry.Length > 1024 * 1024)
                        throw new ArgumentException("Tệp khóa trong bản sao lưu không hợp lệ.");
                    Directory.CreateDirectory(Path.Combine(temp, "keys"));
                    entry.ExtractToFile(Path.Combine(temp, "keys", name));
                }
            }
            foreach (var name in DbFiles)
            {
                var header = new byte[16];
                using var fs = File.OpenRead(Path.Combine(temp, name));
                if (fs.Read(header, 0, 16) < 16 || !System.Text.Encoding.ASCII.GetString(header).StartsWith("SQLite format 3"))
                    throw new ArgumentException($"{name} không phải database hợp lệ.");
            }
            if (Directory.Exists(PendingDir)) Directory.Delete(PendingDir, true);
            Directory.Move(temp, PendingDir);
        }
        catch { if (Directory.Exists(temp)) Directory.Delete(temp, true); throw; }
    }

    // Gọi ngay khi khởi động, trước khi mở bất kỳ database nào.
    public static void ApplyPendingRestore(string dataDir)
    {
        var pending = Path.Combine(dataDir, "restore-pending");
        if (!Directory.Exists(pending) || !DbFiles.All(n => File.Exists(Path.Combine(pending, n)))) return;
        var backups = Path.Combine(dataDir, "backups");
        Directory.CreateDirectory(backups);
        var before = Path.Combine(backups, $"truoc-khoi-phuc-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using (var zip = ZipFile.Open(before, ZipArchiveMode.Create))
        {
            foreach (var n in DbFiles)
                if (File.Exists(Path.Combine(dataDir, n)))
                    zip.CreateEntryFromFile(Path.Combine(dataDir, n), n);
            var currentKeys = Path.Combine(dataDir, "keys");
            if (Directory.Exists(currentKeys))
                foreach (var key in Directory.GetFiles(currentKeys, "key-*.xml"))
                    zip.CreateEntryFromFile(key, "keys/" + Path.GetFileName(key));
        }
        foreach (var n in DbFiles)
        {
            foreach (var ext in new[] { "", "-wal", "-shm" })
                if (File.Exists(Path.Combine(dataDir, n + ext))) File.Delete(Path.Combine(dataDir, n + ext));
            File.Copy(Path.Combine(pending, n), Path.Combine(dataDir, n));
        }
        CopyKeys(Path.Combine(pending, "keys"), Path.Combine(dataDir, "keys"));
        Directory.Delete(pending, true);
    }

    private static void CopyKeys(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "key-*.xml"))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }

    public void Prune(int keep = 14)
    {
        foreach (var f in List().Where(x => x.Name.StartsWith("tu-dong-")).Skip(keep))
            try { f.Delete(); } catch { }
    }
}

public sealed class DailyBackupService(BackupService backup, ILogger<DailyBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!backup.List().Any(x => x.Name.StartsWith("tu-dong-") && x.CreationTime > DateTime.Now.AddHours(-23)))
                {
                    backup.CreateBackupCore("tu-dong");
                    backup.Prune();
                }
            }
            catch (Exception ex) { logger.LogError(ex, "Sao lưu tự động thất bại"); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
