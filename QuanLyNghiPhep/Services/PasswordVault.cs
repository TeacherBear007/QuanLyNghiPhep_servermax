using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using QuanLyNghiPhep.Data;

namespace QuanLyNghiPhep.Services;

// Đăng nhập vẫn kiểm tra hash Identity. Bản mã riêng phục vụ yêu cầu admin xem mật khẩu.
// Không trả bản mã/mật khẩu trong DTO danh sách. Khóa nằm ngoài wwwroot, trong AppData/keys.
public sealed class PasswordVault(IDataProtectionProvider provider)
{
    private IDataProtector For(ApplicationUser user) =>
        provider.CreateProtector("QuanLyNghiPhep.AdminPassword.v1", user.Id);
    public string Protect(ApplicationUser user, string password) => For(user).Protect(password);
    public string Reveal(ApplicationUser user)
    {
        if (string.IsNullOrEmpty(user.ProtectedPassword))
            throw new ArgumentException("Mật khẩu cũ chỉ còn bản băm. Hãy đặt lại mật khẩu để có thể xem.");
        try { return For(user).Unprotect(user.ProtectedPassword); }
        catch (System.Security.Cryptography.CryptographicException)
        { throw new ArgumentException("Không đọc được mật khẩu với khóa hiện tại. Hãy đặt lại mật khẩu."); }
    }
}

// Tất cả đường tạo/đặt lại/tự đổi mật khẩu qua Identity đều đồng bộ bản mã cùng hash.
public sealed class RecoverablePasswordHasher(PasswordVault vault) : PasswordHasher<ApplicationUser>
{
    public override string HashPassword(ApplicationUser user, string password)
    {
        var hash = base.HashPassword(user, password);
        user.ProtectedPassword = vault.Protect(user, password);
        return hash;
    }
}
