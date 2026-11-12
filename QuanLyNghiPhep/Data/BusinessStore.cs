using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace QuanLyNghiPhep.Data;

public sealed class BusinessStore
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly DbContextOptions<BusinessDbContext> options;
    public object SyncRoot
    {
        get;
    }
    public BusinessStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "AppData");
        Directory.CreateDirectory(dir);
        var file = Path.GetFullPath(Path.Combine(dir, "business.db"));
        SyncRoot = Locks.GetOrAdd(file, _ => new object());
        var connection = new SqliteConnectionStringBuilder { DataSource = file, ForeignKeys = true };
        options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection.ToString()).Options;
        lock (SyncRoot)
        {
            using var db = Open();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("""
    CREATE TABLE IF NOT EXISTS "ClassSubjects" (
        "Id" INTEGER NOT NULL
            CONSTRAINT "PK_ClassSubjects" PRIMARY KEY AUTOINCREMENT,
        "ClassName" TEXT NOT NULL,
        "Code" TEXT NOT NULL,
        "Name" TEXT NOT NULL,
        "Faculty" TEXT NOT NULL,
        CONSTRAINT "FK_ClassSubjects_Classes_ClassName"
            FOREIGN KEY ("ClassName") REFERENCES "Classes" ("TenLop")
            ON DELETE RESTRICT
    );
    """);

            db.Database.ExecuteSqlRaw("""
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_ClassSubjects_ClassName_Code"
    ON "ClassSubjects" ("ClassName", "Code");
    """);
            db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS \"Holidays\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Holidays\" PRIMARY KEY AUTOINCREMENT, \"Name\" TEXT NOT NULL, \"FromDate\" TEXT NOT NULL, \"ToDate\" TEXT NOT NULL)");
            db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS \"Platoons\" (\"Name\" TEXT NOT NULL CONSTRAINT \"PK_Platoons\" PRIMARY KEY)");
            EnsureColumn(db, "Classes", "ManagerUserId", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(db, "Students", "IsActive", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(
        db,
        "ClassSubjects",
        "FacultyUserId",
        "TEXT NOT NULL DEFAULT ''");

            EnsureColumn(
                db,
                "Requests",
                "SubjectId",
                "INTEGER NULL REFERENCES \"ClassSubjects\"(\"Id\") ON DELETE RESTRICT");
        }
    }
    public BusinessDbContext Open() => new(options);
    // Old academic tables remain untouched when upgrading an existing installation.
    public static bool HasLegacyStudentHistory(BusinessDbContext db, string code)
        => Referenced(db, "Enrollments", "StudentCode", code);
    public static bool HasLegacyAccountHistory(BusinessDbContext db, string id)
        => Referenced(db, "Assignments", "TeacherId", id)
        || Referenced(db, "Attendance", "UpdatedBy", id)
        || Referenced(db, "Courses", "CreatedBy", id);
    private static bool Referenced(BusinessDbContext db, string table, string column, string value)
    {
        db.Database.OpenConnection();
        try
        {
            var connection = db.Database.GetDbConnection();
            using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
            var name = exists.CreateParameter();
            name.ParameterName = "$name";
            name.Value = table;
            exists.Parameters.Add(name);
            if (Convert.ToInt64(exists.ExecuteScalar()) == 0)
                return false;
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE \"{column}\"=$value";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$value";
            parameter.Value = value;
            command.Parameters.Add(parameter);
            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }
        finally { db.Database.CloseConnection(); }
    }
    private static void EnsureColumn(
    BusinessDbContext db,
    string table,
    string column,
    string definition)
    {
        db.Database.OpenConnection();

        try
        {
            var connection = db.Database.GetDbConnection();
            bool exists = false;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info(\"{table}\");";

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    if (string.Equals(
                        reader.GetString(1),
                        column,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                using var command = connection.CreateCommand();

                command.CommandText =
                    $"ALTER TABLE \"{table}\" " +
                    $"ADD COLUMN \"{column}\" {definition};";

                command.ExecuteNonQuery();
            }
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }
}
