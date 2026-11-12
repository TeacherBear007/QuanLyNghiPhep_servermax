using System.Globalization;
using System.Text.RegularExpressions;

namespace QuanLyNghiPhep.Services;

public static class ValidationRules
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 256;
    public const int MaxImportRows = 1000;

    public static string BirthDate(string? value)
    {
        if (!DateTime.TryParseExact(value?.Trim(), ["d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date.Date < new DateTime(1900, 1, 1) || date.Date > DateTime.Today)
            throw new ArgumentException("Ngày sinh phải hợp lệ, theo định dạng dd/MM/yyyy và không ở tương lai.");
        return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    public static string Phone(string? value)
    {
        var phone = value?.Trim() ?? "";
        if (!Regex.IsMatch(phone, @"\A0[0-9]{9}\z"))
            throw new ArgumentException("Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.");
        return phone;
    }

    public static string ErrorMessage(Exception error) => error is UserAccessException or ArgumentException or InvalidDataException or BusinessRuleException
        ? error.Message : "Không thể hoàn tất thao tác. Vui lòng kiểm tra dữ liệu và thử lại.";
}

public sealed class BusinessRuleException(string message) : InvalidOperationException(message);
