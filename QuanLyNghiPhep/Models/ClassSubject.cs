namespace QuanLyNghiPhep.Models
{
    public class ClassSubject
    {
        public int Id
        {
            get; set;
        }
        public string ClassName { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Faculty { get; set; } = "";
        public string FacultyUserId { get; set; } = "";
    }
    public sealed record FacultyOption(
    string UserId,
    string FacultyId,
    string Name);
}
