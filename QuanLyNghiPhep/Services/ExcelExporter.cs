using System.IO.Compression;
using System.Globalization;
using System.Xml;
using Microsoft.VisualBasic.FileIO;
using System.Security;
using System.Text;
using System.Xml.Linq;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services;

// Tạo và đọc tệp Excel .xlsx chuẩn OpenXML bằng thư viện hệ thống, không cần tải NuGet.
public static class ExcelExporter
{
    public static byte[] BuildTable(string sheet, IReadOnlyList<string[]> rows)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int i = 0; i < rows.Count; i++)
            WriteRow(xml, i + 1, rows[i]);
        xml.Append("</sheetData></worksheet>");
        return BuildWorkbook(sheet, xml.ToString());
    }
    private static readonly string[] Headers =
    {
        "STT", "Mã học viên", "Họ và tên", "Lớp", "Loại nghỉ (Tag 1)", "Môn học (Tag 2)",
        "Bắt đầu nghỉ", "Kết thúc nghỉ", "Số giờ nghỉ",
        "Lý do nghỉ", "Địa chỉ nơi nghỉ", "Trạng thái", "Ngày lập", "Người duyệt"
    };

    private static readonly string[] ClassHeaders =
    {
        "STT", "Tên", "Lớp", "Ngày sinh", "Số điện thoại"
    };

    public static byte[] Build(IReadOnlyList<LeaveRequest> records)
    {
        return BuildWorkbook("Danh sach nghi phep", Worksheet(records));
    }

    public static byte[] BuildClassTemplate(string className, IReadOnlyList<Student> students)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        WriteRow(xml, 1, ClassHeaders);
        if (students.Count > 0)
        {
            for (int i = 0; i < students.Count; i++)
            {
                var s = students[i];
                WriteRow(xml, i + 2, new[]
                {
                    (i + 1).ToString(), s.HoTen, string.IsNullOrWhiteSpace(s.Lop) ? className : s.Lop, s.NgaySinh, s.SoDienThoai
                });
            }
        }
        else
        {
            WriteRow(xml, 2, new[] { "1", "Nguyễn Văn An", string.IsNullOrWhiteSpace(className) ? "B9D55" : className, "15/05/2005", "0912345678" });
            WriteRow(xml, 3, new[] { "2", "Trần Minh Đức", string.IsNullOrWhiteSpace(className) ? "B9D55" : className, "20/08/2005", "0987654321" });
        }
        xml.Append("</sheetData></worksheet>");
        return BuildWorkbook("Danh sach lop", xml.ToString());
    }

    public static List<Student> ParseClassExcel(Stream stream, string fallbackClass = "")
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > 10 * 1024 * 1024)
                throw new InvalidDataException("Tệp nhập không được lớn hơn 10 MB.");
            memory.Write(buffer, 0, read);
        }
        var bytes = memory.ToArray();
        List<string[]> rows;
        bool date1904 = false;
        if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            memory.Position = 0;
            rows = ReadWorkbook(memory, out date1904);
        }
        else
            rows = ReadDelimited(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        return ConvertRows(rows, fallbackClass, date1904);
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 20 * 1024 * 1024
        });
        try
        {
            return XDocument.Load(reader);
        }
        catch (XmlException error) { throw new InvalidDataException("Nội dung XML trong tệp Excel không hợp lệ.", error); }
    }

    private static List<string[]> ReadWorkbook(Stream stream, out bool date1904)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > 1000 || zip.Entries.Sum(e => e.Length) > 20 * 1024 * 1024)
            throw new InvalidDataException("Tệp Excel vượt giới hạn giải nén 20 MB / 1000 thành phần.");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbook = ReadXml(zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("Thiếu workbook Excel."));
        date1904 = workbook.Root?.Element(main + "workbookPr")?.Attribute("date1904")?.Value is "1" or "true";
        var firstSheet = workbook.Descendants(main + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("Không tìm thấy sheet dữ liệu.");
        var relations = ReadXml(zip.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("Thiếu liên kết sheet."));
        var sheetRel = relations.Descendants(packageRel + "Relationship").SingleOrDefault(x => x.Attribute("Id")?.Value == firstSheet.Attribute(rel + "id")?.Value)
            ?? throw new InvalidDataException("Liên kết sheet không hợp lệ.");
        if (sheetRel.Attribute("TargetMode")?.Value == "External")
            throw new InvalidDataException("Không đọc sheet từ liên kết bên ngoài.");
        var target = sheetRel.Attribute("Target")?.Value ?? "";
        var sheetPath = new Uri(new Uri("https://workbook.invalid/xl/workbook.xml"), target).AbsolutePath.TrimStart('/');
        var sheet = ReadXml(zip.GetEntry(sheetPath) ?? throw new InvalidDataException("Không tìm thấy sheet đầu tiên."));
        var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
        var shared = sharedEntry is null ? new List<string>() : ReadXml(sharedEntry).Descendants(main + "si")
            .Select(x => string.Concat(x.Descendants(main + "t").Select(t => t.Value))).ToList();
        var rows = new List<string[]>();
        foreach (var row in sheet.Descendants(main + "row"))
        {
            var values = new Dictionary<int, string>();
            int sequential = 0;
            foreach (var cell in row.Elements(main + "c"))
            {
                int column = cell.Attribute("r") is { } reference ? ParseColumnIndex(reference.Value) : sequential;
                sequential = column + 1;
                if (column >= 32)
                    continue;
                var type = cell.Attribute("t")?.Value;
                var value = type == "inlineStr" ? string.Concat(cell.Descendants(main + "t").Select(x => x.Value))
                    : cell.Element(main + "v")?.Value ?? "";
                if (type == "s")
                {
                    if (!int.TryParse(value, out var index) || index < 0 || index >= shared.Count)
                        throw new InvalidDataException("Ô văn bản Excel không hợp lệ.");
                    value = shared[index];
                }
                values[column] = value.Trim();
            }
            if (values.Values.All(string.IsNullOrWhiteSpace))
                continue;
            var cells = new string[values.Keys.Max() + 1];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = values.GetValueOrDefault(i, "");
            rows.Add(cells);
            if (rows.Count > ValidationRules.MaxImportRows + 1)
                throw new InvalidDataException($"Chỉ nhập tối đa {ValidationRules.MaxImportRows} học viên mỗi lần.");
        }
        return rows;
    }

    private static List<string[]> ReadDelimited(string text)
    {
        var firstLine = text.Split('\n')[0];
        var separators = new Dictionary<char, int> { ['\t'] = 0, [';'] = 0, [','] = 0 };
        bool quoted = false;
        foreach (var ch in firstLine)
        {
            if (ch == '"')
                quoted = !quoted;
            else if (!quoted && separators.ContainsKey(ch))
                separators[ch]++;
        }
        var separator = separators.OrderByDescending(x => x.Value).First().Key;
        using var parser = new TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(separator.ToString());
        var rows = new List<string[]>();
        try
        {
            while (!parser.EndOfData)
            {
                var row = parser.ReadFields() ?? [];
                if (row.All(string.IsNullOrWhiteSpace))
                    continue;
                rows.Add(row.Select(x => x.Trim()).ToArray());
                if (rows.Count > ValidationRules.MaxImportRows + 1)
                    throw new InvalidDataException($"Chỉ nhập tối đa {ValidationRules.MaxImportRows} học viên mỗi lần.");
            }
        }
        catch (MalformedLineException error) { throw new InvalidDataException($"Dòng {parser.ErrorLineNumber}: CSV không đúng cú pháp.", error); }
        return rows;
    }

    private static string HeaderKey(string value)
        => string.Concat(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c)).Where(char.IsLetterOrDigit));

    private static List<Student> ConvertRows(List<string[]> rows, string fallbackClass, bool date1904)
    {
        if (rows.Count == 0)
            return [];
        var headers = rows[0].Select(HeaderKey).ToList();
        int Index(params string[] names) => headers.FindIndex(names.Contains);
        int nameCol = Index("ten", "hoten", "hovaten", "tenhocvien", "hovatenhocvien");
        int classCol = Index("lop", "tenlop"), dateCol = Index("ngaysinh"), phoneCol = Index("sodienthoai", "dienthoai", "sdt", "phone");
        bool header = nameCol >= 0 && (classCol >= 0 || dateCol >= 0 || phoneCol >= 0);
        int codeCol = header ? Index("mahocvien", "mahv") : -1;
        if (header && (dateCol < 0 || phoneCol < 0))
            throw new InvalidDataException("Thiếu cột Ngày sinh hoặc Số điện thoại trong tiêu đề.");
        if (!header)
        {
            int offset = rows[0].Length == 4 ? 0 : 1;
            nameCol = offset;
            classCol = offset + 1;
            dateCol = offset + 2;
            phoneCol = offset + 3;
        }
        var result = new List<Student>();
        for (int r = header ? 1 : 0; r < rows.Count; r++)
        {
            string Cell(int column) => column >= 0 && column < rows[r].Length ? rows[r][column].Trim() : "";
            var name = Cell(nameCol);
            if (name.Length == 0)
                throw new InvalidDataException($"Dòng {r + 1}: thiếu họ tên học viên.");
            var date = Cell(dateCol);
            if (double.TryParse(date, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial > 10000 && serial < 100000)
                date = DateTime.FromOADate(serial + (date1904 ? 1462 : 0)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            else if (DateTime.TryParseExact(date, ["d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                date = parsed.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            result.Add(new Student
            {
                STT = result.Count + 1,
                MaHocVien = Cell(codeCol),
                HoTen = name,
                Lop = string.IsNullOrWhiteSpace(Cell(classCol)) ? fallbackClass.Trim() : Cell(classCol),
                NgaySinh = date,
                SoDienThoai = Cell(phoneCol)
            });
        }
        if (result.Count > ValidationRules.MaxImportRows)
            throw new InvalidDataException($"Chỉ nhập tối đa {ValidationRules.MaxImportRows} học viên.");
        return result;
    }

    private static int ParseColumnIndex(string reference)
    {
        int column = 0, letters = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch))
                break;
            if (++letters > 3 || char.ToUpperInvariant(ch) is < 'A' or > 'Z')
                throw new InvalidDataException("Địa chỉ ô Excel không hợp lệ.");
            column = column * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
        }
        if (column is < 1 or > 16384)
            throw new InvalidDataException("Cột Excel vượt giới hạn.");
        return column - 1;
    }

    private static byte[] BuildWorkbook(string sheetName, string worksheetXml)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Write(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Write(zip, "xl/workbook.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="{EscapeCell(sheetName)}" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Write(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            Write(zip, "xl/worksheets/sheet1.xml", worksheetXml);
        }
        return output.ToArray();
    }

    private static string Worksheet(IReadOnlyList<LeaveRequest> records)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        WriteRow(xml, 1, Headers);
        for (int i = 0; i < records.Count; i++)
        {
            var x = records[i];
            WriteRow(xml, i + 2, new string[]
            {
                (i + 1).ToString(), x.MaHocVien, x.HoTen, x.Lop,
                x.TagLoaiNghi, x.HienThiMonHoc, x.TuThoiDiem.ToString("dd/MM/yyyy HH:mm"),
                x.DenThoiDiem.ToString("dd/MM/yyyy HH:mm"), x.SoGio.ToString("0.##"),
                x.LyDo, x.DiaChiNoiNghi, x.TrangThai,
                x.NgayTao.ToString("dd/MM/yyyy HH:mm"), x.NguoiDuyet
            });
        }
        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    private static void WriteRow(StringBuilder xml, int number, IEnumerable<string> cells)
    {
        xml.Append("<row r=\"").Append(number).Append("\">");
        int col = 0;
        foreach (var value in cells)
        {
            xml.Append("<c r=\"").Append(ColumnName(col++)).Append(number)
                .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(EscapeCell(value))
                .Append("</t></is></c>");
        }
        xml.Append("</row>");
    }

    private static string EscapeCell(string? value)
    {
        var text = new StringBuilder();
        foreach (var rune in (value ?? "").EnumerateRunes())
            if (rune.Value is 9 or 10 or 13 or (>= 0x20 and <= 0xD7FF)
                or (>= 0xE000 and <= 0xFFFD) or (>= 0x10000 and <= 0x10FFFF))
                text.Append(rune);
        return SecurityElement.Escape(text.ToString()) ?? "";
    }

    private static string ColumnName(int zeroBased)
    {
        var result = "";
        for (int x = zeroBased + 1; x > 0; x = (x - 1) / 26)
            result = (char)('A' + (x - 1) % 26) + result;
        return result;
    }

    private static void Write(ZipArchive zip, string name, string contents)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }
}
