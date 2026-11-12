> Tài liệu phân tích ban đầu của dự án. Quy trình và chức năng bản Hoanganh123 đã cập nhật; xem README.md ở thư mục gốc để biết luồng duyệt theo lớp, tài khoản và cách dùng hiện hành.

# Phân tích bài toán — phạm vi hiện tại

## Công việc, người làm và dữ liệu

Admin cấp tài khoản và quản lý trạng thái, tên đăng nhập/mật khẩu. Phòng QLHV tạo lớp và danh sách học viên (nhập tay hoặc Excel), phân công tài khoản trung đội, cập nhật và quản lý trạng thái học viên. Trung đội quản lý các lớp được giao, thêm môn/khoa phụ trách và lập/duyệt danh sách nghỉ. Khoa xem đơn tranh thủ đã duyệt thuộc môn được phân công.

Quy trình: Phòng QLHV tạo lớp/phân công → Trung đội nhập môn và khoa → Trung đội lập/duyệt nghỉ → Phòng QLHV lưu trữ/tra cứu, Khoa nhận đơn đúng môn. Mỗi thao tác kiểm tra quyền phía server.

## Đối tượng và trường quản lý

- ApplicationUser (Identity): ID, tên đăng nhập, hash mật khẩu, vai trò, trạng thái; tài khoản cá nhân có họ tên/ngày sinh/giới tính/số điện thoại/số hiệu, tài khoản khoa có FacultyName/FacultyId.
- ClassGroup: TenLop (khóa), ManagerUserId (ID người quản lý), NguoiTao (nhãn tên kế thừa), NgayTao. Tên tài khoản không phải khóa liên kết quyền.
- Student: MaHocVien (khóa), STT, HoTen, Lop (khóa ngoại), NgaySinh, SoDienThoai, TrungDoi, IsActive.
- ClassSubject: Id, ClassName, Code, Name, Faculty, FacultyUserId. Mã môn duy nhất trong lớp.
- LeaveRequest: Id, MaDon, MaHocVien, bản chụp họ tên/lớp/trung đội, bắt đầu/kết thúc, lý do, địa chỉ, loại nghỉ, SubjectId, nhãn môn, trạng thái, người/thời điểm lập và duyệt.
- AuditEntry: Id, thời gian, ID tài khoản thực hiện, hành động và chi tiết.
- BusinessState: ID bản trạng thái và bộ đếm mã đơn kế tiếp.

## Công thức

Loại đơn: Trong ngày nếu hai thời điểm cùng ngày, ngược lại Dài ngày.
Số giờ = Max(0, (kết thúc − bắt đầu).TotalHours).
Số ngày quy đổi = Round(số giờ / 24, 2); không phải số ngày học.
Lượt nghỉ học viên = số đơn có cùng MaHocVien, không so sánh họ tên.
Hai khoảng nghỉ trùng khi bắt đầu mới < kết thúc cũ và kết thúc mới > bắt đầu cũ. Đơn từ chối không chặn khoảng mới; hai khoảng tiếp giáp được phép.
Báo cáo tháng chọn đơn có khoảng thời gian giao với [đầu tháng, đầu tháng tiếp theo).

## Biểu mẫu đầu vào / đầu ra

Đầu vào: biểu mẫu cấp tài khoản, Excel/CSV danh sách lớp, biểu mẫu học viên, môn/khoa và đơn nghỉ (học viên, thời gian, lý do, địa chỉ; tranh thủ chọn môn).
Đầu ra: danh sách lớp/học viên theo quyền, bảng đơn có bộ lọc và báo cáo Excel. Chưa có mẫu in đơn trên giao diện.

## Lịch sử và an toàn

Hồ sơ đã xử lý giữ nguyên bản chụp. Ngừng theo dõi thay vì xóa học viên có lịch sử; có khôi phục. SQLite dùng khóa ngoại và giao dịch. Kiểm tra quyền theo lớp/môn/khoa, CSRF, Identity hash mật khẩu, khóa đăng nhập sai, thu hồi phiên khi thay đổi tài khoản. Tài khoản có lớp hoặc lịch sử không bị xóa hẳn.

Không còn thời khóa biểu, điểm danh hoặc tự đánh giá điều kiện thi. Các bảng lịch sử cũ được giữ nguyên nếu nâng cấp, không tạo nghiệp vụ mới trên chúng.

## Ràng buộc nhập liệu và cập nhật

Ngày sinh phải là ngày hợp lệ từ năm 1900 đến hiện tại, theo ngày/tháng/năm hoặc ISO. Số điện thoại bắt buộc có 10 chữ số, bắt đầu bằng 0. Mã học viên nhập trùng bị từ chối; mã tự sinh dựa trên số thứ tự lớn nhất còn có trong lớp. Không chuyển lớp khi học viên còn đơn chờ trung đội hoặc chờ Phòng QLHV xử lý.

Nhập lớp tối đa 1.000 học viên, tệp tối đa 10 MB. Excel đọc đúng sheet đầu theo workbook và hỗ trợ ngày kiểu số; CSV xử lý dấu phân cách trong trường có ngoặc kép. Dòng nhập dở hoặc giá trị sai phải được sửa trước khi lưu cả lớp.

Các thao tác nghiệp vụ nạp trạng thái SQLite mới nhất dưới khóa dùng chung trong tiến trình. Ghi dữ liệu dùng giao dịch; nếu lưu thất bại, không giữ bản ghi hoặc tăng bộ đếm mã đơn trong bộ nhớ. Không sử dụng đường nhập học viên thủ công ẩn để vượt quyền quản lý danh sách của Phòng QLHV.
