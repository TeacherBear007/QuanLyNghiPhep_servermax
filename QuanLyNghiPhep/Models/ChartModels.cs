namespace QuanLyNghiPhep.Models;

public sealed record Slice(string Label, int Value, string Color);
public sealed record ChartPoint(string Label, int Value);
public sealed record RankItem(string Label, int Value, string Sub = "");

public static class ChartColors
{
    public const string Weekend = "#2f7a5a";
    public const string Holiday = "#d99a1e";
    public const string Tranh = "#3b6fd0";
    public const string Ok = "#2f7a5a";
    public const string Wait = "#d99a1e";
    public const string Bad = "#c0504d";
    public const string Cancel = "#9aa5b1";
    public const string Brand = "#2f6b4a";
}
