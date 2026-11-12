namespace QuanLyNghiPhep.Models;

public sealed class Holiday
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}

public sealed class PlatoonItem
{
    public string Name { get; set; } = "";
}
