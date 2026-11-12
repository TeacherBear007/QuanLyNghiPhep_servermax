using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services;

public sealed class UserAccessException(string message) : UnauthorizedAccessException(message);

// Mỗi thao tác đọc/ghi kiểm tra lại tài khoản trong DB, kể cả trên circuit đang mở.
public sealed class AccountValidity(IDbContextFactory<AuthDbContext> factory)
{
    public const string ExpiryClaim = "leave:expires";
    public const int SessionMinutes = 120;
    public bool IsValid(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !long.TryParse(principal.FindFirstValue(ExpiryClaim), out var expires)
            || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return false;
        using var db = factory.CreateDbContext();
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = db.Users.AsNoTracking().SingleOrDefault(x => x.Id == id);
        if (user is null || !user.IsEnabled || user.LockoutEnd > DateTimeOffset.UtcNow
            || user.SecurityStamp != principal.FindFirstValue("AspNet.Identity.SecurityStamp"))
            return false;
        var role = principal.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(role, out var parsed) && Enum.IsDefined(parsed)
            && db.UserRoles.Any(ur => ur.UserId == id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role));
    }
}
public sealed record AccountProfile(
    string FullName,
    DateTime? DateOfBirth,
    string Gender,
    string PhoneNumber,
    string MajorCode);
public sealed class AppSession(AuthenticationStateProvider provider, AccountValidity validity, IDbContextFactory<AuthDbContext> factory)
{
    private ClaimsPrincipal principal = new(new ClaimsIdentity());
    public event Action? AccountRequested;
    public event Action? SecurityRequested;
    public void RequestAccount() => AccountRequested?.Invoke();
    public void RequestSecurity() => SecurityRequested?.Invoke();
    public string UserName => principal.Identity?.Name ?? "";
    public string UserId => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    public UserRole Role => Enum.TryParse<UserRole>(principal.FindFirstValue(ClaimTypes.Role), out var role) ? role : default;
    public bool IsAuthenticated => principal.Identity?.IsAuthenticated == true;
    public bool CanManage => IsAuthenticated && Role == UserRole.TrungDoi;
    public bool CanApprove => IsAuthenticated && Role == UserRole.PhongQLHV;
    public bool CanExport => IsAuthenticated && Role is UserRole.TrungDoi or UserRole.PhongQLHV;
    public DateTimeOffset? ExpiresAt => long.TryParse(principal.FindFirstValue(AccountValidity.ExpiryClaim), out var e) ? DateTimeOffset.FromUnixTimeSeconds(e) : null;
    public string RoleDisplay => RoleLabels.Display(Role);
    public async Task InitializeAsync()
    {
        principal = (await provider.GetAuthenticationStateAsync()).User;
        if (IsAuthenticated)
            EnsureValid();
    }
    public void EnsureValid()
    {
        if (validity.IsValid(principal))
            return;
        principal = new(new ClaimsIdentity());
        throw new UserAccessException("Phiên đăng nhập hết hạn hoặc tài khoản bị khóa. Vui lòng đăng nhập lại.");
    }
    public async Task<AccountProfile> GetAccountInfoAsync()
    {
        EnsureValid();

        await using var db = await factory.CreateDbContextAsync();

        var profile = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == UserId)
            .Select(user => new AccountProfile(
                user.FullName,
                user.DateOfBirth,
                user.Gender,
                user.PhoneNumber ?? "",
                user.MajorCode))
            .SingleOrDefaultAsync();

        return profile
            ?? throw new UserAccessException(
                "Không tìm thấy thông tin tài khoản.");
    }
}

public sealed class IdentityRevalidatingProvider(ILoggerFactory loggerFactory, AccountValidity validity)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);
    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
        => Task.FromResult(validity.IsValid(state.User));
}
