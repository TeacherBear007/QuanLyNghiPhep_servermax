using System.Net;
using System.Security.Claims;
using Microsoft.Data.Sqlite;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Components;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services;

var builder = WebApplication.CreateBuilder(args);
var nginxProxy = builder.Configuration["Deployment:NginxProxyIp"];
if (!string.IsNullOrWhiteSpace(nginxProxy))
{
    if (!IPAddress.TryParse(nginxProxy, out var proxyAddress))
        throw new InvalidOperationException("Deployment:NginxProxyIp phải là một địa chỉ IP hợp lệ.");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Add(proxyAddress);
    });
}
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "AppData");
Directory.CreateDirectory(dataDirectory);
BackupService.ApplyPendingRestore(dataDirectory);
var accountConnection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDirectory, "accounts.db"), ForeignKeys = true };
builder.Services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite(accountConnection.ToString()));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = ValidationRules.PasswordMinLength;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredUniqueChars = 4;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<AuthDbContext>().AddDefaultTokenProviders();
if (builder.Environment.IsProduction())
{
    builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, ProductionPasswordValidator>();
    builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
}
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "LeaveManagement.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(AccountValidity.SessionMinutes);
    options.SlidingExpiration = false;
    options.LoginPath = "/";
    options.Events.OnValidatePrincipal = async context =>
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<AccountValidity>().IsValid(context.Principal!))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("QuanLyNghiPhepHV");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingProvider>();
builder.Services.AddSingleton<AccountValidity>();
builder.Services.AddScoped<AppSession>();
builder.Services.AddSingleton<LeaveRepository>();
builder.Services.AddSingleton<BusinessStore>();
builder.Services.AddSingleton<PasswordVault>();
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, RecoverablePasswordHasher>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<DailyBackupService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
LeaveCategories.DefaultWeekendAddress = builder.Configuration["Leave:DefaultAddress"] ?? LeaveCategories.DefaultWeekendAddress;
LeaveCategories.PlatoonPrefix = builder.Configuration["Leave:PlatoonPrefix"] ?? LeaveCategories.PlatoonPrefix;
LeaveCategories.TranhThuMonthlyLimit = builder.Configuration.GetValue("Leave:TranhThuMonthlyLimit", LeaveCategories.TranhThuMonthlyLimit);
var app = builder.Build();
await AccountSetup.InitializeAsync(app.Services, dataDirectory, app.Environment.IsProduction());
if (args.Length > 0 && args[0] == "--account")
{
    await AccountSetup.RunCommandAsync(app.Services, args);
    return;
}
// Khoa và môn học được nhập bởi người dùng, không tự tạo dữ liệu mẫu.
if (!string.IsNullOrWhiteSpace(nginxProxy))
    app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
// Với Nginx, HTTP được chuyển hướng tại proxy, riêng health check nội bộ dùng HTTP.
if (string.IsNullOrWhiteSpace(nginxProxy))
    app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
// Tuần tự hóa cập nhật số lần sai trong một tiến trình, tránh mất lượt khi POST đồng thời.
var loginGate = new SemaphoreSlim(1, 1);
app.MapPost("/account/login", async (HttpContext http, IAntiforgery antiforgery,
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Phiên biểu mẫu hết hạn. Hãy tải lại trang."); }
    var form = await http.Request.ReadFormAsync();
    string username = form["username"].ToString().Trim();
    string password = form["password"].ToString();

    IResult LoginFailed() => http.Request.Headers["X-Login-Form"] == "1"
        ? Results.Json(new { ok = false, message = "Tên đăng nhập hoặc mật khẩu không đúng, tài khoản bị khóa hoặc tạm khóa 5 phút sau nhiều lần nhập sai." })
        : Results.LocalRedirect("/?loginError=invalid&username=" + Uri.EscapeDataString(username.Length <= 256 ? username : ""));

    if (username.Length is < 1 or > 256
        || password.Length is < 1 or > 256
        || (app.Environment.IsProduction() && password == AccountSetup.DemoPassword))
    {
        return LoginFailed();
    }
    await loginGate.WaitAsync(http.RequestAborted);
    try
    {
        var user = await users.FindByNameAsync(username);
        if (user is null || !user.IsEnabled)
            return LoginFailed();
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return LoginFailed();

        // Lấy quyền được cấp trong database.
        var roles = await users.GetRolesAsync(user);

        if (roles.Count != 1
            || !Enum.TryParse<UserRole>(roles[0], out var assignedRole)
            || !Enum.IsDefined(assignedRole))
        {
            return LoginFailed();
        }
        var expires = DateTimeOffset.UtcNow.AddMinutes(AccountValidity.SessionMinutes);
        await signIn.SignInWithClaimsAsync(user, new AuthenticationProperties { IsPersistent = false, ExpiresUtc = expires },
            new[] { new Claim(AccountValidity.ExpiryClaim, expires.ToUnixTimeSeconds().ToString()) });
        return http.Request.Headers["X-Login-Form"] == "1"
            ? Results.Json(new { ok = true }) : Results.LocalRedirect("/");
    }
    finally { loginGate.Release(); }
}).RequireRateLimiting("login");
app.MapPost("/account/renew", async (HttpContext http, IAntiforgery antiforgery,
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AccountValidity validity) =>
{
    try { await antiforgery.ValidateRequestAsync(http); }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Phiên biểu mẫu hết hạn."); }
    if (!validity.IsValid(http.User)) return Results.LocalRedirect("/");
    var user = await users.GetUserAsync(http.User);
    if (user is null) return Results.LocalRedirect("/");
    var expires = DateTimeOffset.UtcNow.AddMinutes(AccountValidity.SessionMinutes);
    await signIn.SignInWithClaimsAsync(user, new AuthenticationProperties { IsPersistent = false, ExpiresUtc = expires },
        new[] { new Claim(AccountValidity.ExpiryClaim, expires.ToUnixTimeSeconds().ToString()) });
    return Results.LocalRedirect("/");
}).RequireAuthorization();
app.MapPost("/account/logout", async (HttpContext http, IAntiforgery antiforgery,
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Phiên biểu mẫu hết hạn. Hãy tải lại trang."); }
    var user = await users.GetUserAsync(http.User);
    // Thu hồi cả các circuit và cookie cũ của cùng tài khoản.
    if (user is not null)
        AccountSetup.Check(await users.UpdateSecurityStampAsync(user));
    await signIn.SignOutAsync();
    return Results.LocalRedirect("/");
}).RequireAuthorization();
app.MapPost("/account/change-password", async (HttpContext http, IAntiforgery antiforgery, UserManager<ApplicationUser> users, AccountValidity validity) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Phiên biểu mẫu hết hạn."); }
    if (!validity.IsValid(http.User))
        return Results.LocalRedirect("/");
    var form = await http.Request.ReadFormAsync();
    string current = form["current"].ToString(), next = form["next"].ToString(), confirm = form["confirm"].ToString();
    if (next.Length < ValidationRules.PasswordMinLength || next.Length > ValidationRules.PasswordMaxLength || current.Length > 256 || next != confirm || next == current)
        return Results.LocalRedirect("/password?error=validation");
    await loginGate.WaitAsync(http.RequestAborted);
    try
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null || !validity.IsValid(http.User))
            return Results.LocalRedirect("/");
        var result = await users.ChangePasswordAsync(user, current, next);
        if (!result.Succeeded)
            return Results.LocalRedirect("/password?error=invalid");
        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.LocalRedirect("/?passwordChanged=1");
    }
    finally { loginGate.Release(); }
}).RequireAuthorization().RequireRateLimiting("login");
app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
