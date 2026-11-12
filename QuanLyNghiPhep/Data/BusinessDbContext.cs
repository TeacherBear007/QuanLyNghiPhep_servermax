using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Data;

public sealed class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    public DbSet<ClassGroup> Classes => Set<ClassGroup>();
    public DbSet<BusinessState> State => Set<BusinessState>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<LeaveRequest> Requests => Set<LeaveRequest>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<ClassSubject> ClassSubjects => Set<ClassSubject>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<PlatoonItem> Platoons => Set<PlatoonItem>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ClassGroup>().HasKey(x => x.TenLop);
        b.Entity<Holiday>().HasKey(x => x.Id);
        b.Entity<PlatoonItem>().HasKey(x => x.Name);
        b.Entity<Student>().HasKey(x => x.MaHocVien);
        b.Entity<Student>().HasOne<ClassGroup>().WithMany().HasForeignKey(x => x.Lop).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LeaveRequest>().Ignore(x => x.DanhSachTagMon);
        b.Entity<LeaveRequest>().HasOne<Student>().WithMany().HasForeignKey(x => x.MaHocVien).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClassSubject>().ToTable("ClassSubjects");
        b.Entity<ClassSubject>().HasIndex(x => new { x.ClassName, x.Code }).IsUnique();
        b.Entity<ClassSubject>().HasOne<ClassGroup>().WithMany().HasForeignKey(x => x.ClassName).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LeaveRequest>().HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
