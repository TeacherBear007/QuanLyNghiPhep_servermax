# Hoanganh123 — Quản lý học viên nghỉ phép

Bản sửa ngày 12/11/2026, dùng C# / ASP.NET Core Blazor Server, .NET 8 và Visual Studio 2022.

## Cập nhật giao diện đăng nhập theo hanh009

- Chỉ lấy mẫu giao diện đăng nhập của `hanh009.zip`: khung kính mờ, logo nổi, tiêu đề, biểu tượng và nút xanh. Không nhập code nghiệp vụ, tài khoản hay database từ ZIP tham khảo.
- Hai ô đăng nhập không có giá trị mặc định, không lấy tên tài khoản từ URL. Khi quay lại trang bằng Back/Forward, xóa nội dung được trình duyệt khôi phục. Nút mắt vẫn hoạt động.
- Đặt gợi ý tắt tự điền, khóa nhập lúc đầu rồi mở ngay khi click/Tab vào ô. Không dùng vòng lặp xóa nội dung khi người dùng đang nhập. Đăng nhập sai vẫn giữ tên vừa nhập và xóa mật khẩu.
- Checkbox “Ghi nhớ phiên đăng nhập” trong mẫu chỉ là giao diện, chưa nối với xử lý đăng nhập; không đưa checkbox không hoạt động đó vào bản bàn giao. Chính sách phiên đăng nhập hiện tại được giữ nguyên.
- Script có mã phiên bản để trình duyệt tải bản mới sau khi cập nhật. Trình quản lý mật khẩu có thể bỏ qua gợi ý của website: nếu vẫn tự điền, xóa thông tin đã lưu cho `localhost:10999` hoặc tắt tự điền trong trình duyệt. Xóa cache không đồng nghĩa xóa mật khẩu đã lưu.
- Đã đối chiếu SHA-256: toàn bộ AppData, code backend và các trang nghiệp vụ giữ nguyên so với bản trước khi đổi giao diện.

## Mở và chạy

1. Giải nén toàn bộ `Hoanganh123.zip` ra một thư mục mới, không chạy trực tiếp trong ZIP.
2. Mở `QuanLyNghiPhep.sln` bằng Visual Studio 2022 có workload **ASP.NET and web development** và SDK .NET 8.
3. Đặt dự án **QuanLyNghiPhep** làm Startup Project, chờ NuGet restore rồi nhấn F5/Ctrl+F5.
4. Đăng nhập bằng tài khoản trong bảng dưới. Giữ nguyên thư mục `QuanLyNghiPhep/AppData`, gồm database và `keys`.

Có thể chạy bằng terminal:

```sh
dotnet restore QuanLyNghiPhep.sln
dotnet run --project QuanLyNghiPhep
```

## Tài khoản trong database bàn giao

| Tên đăng nhập | Vai trò | Mật khẩu bàn giao |
| --- | --- | --- |
| admin | Quản trị viên | Demo@123 |
| phongqlhv | Phòng Quản lý học viên | Demo@123 |
| thaythuyetk1 | Chủ nhiệm lớp | Demo@123 |
| khoanv7 | Giáo viên bộ môn/khoa | Demo@123 |
| khoallct | Giáo viên bộ môn/khoa | Demo@123 |

Đã kiểm tra đăng nhập thực tế qua HTTP cho cả 5 tài khoản. Không tạo lại `trungdoi` hoặc `bomon` trong database bàn giao: đây là các tên cũ trong tệp hướng dẫn, không còn là tên tài khoản hiện tại. Tệp `AppData/tai-khoan-ban-dau.txt` đã được đồng bộ với database.

Mật khẩu chỉ được đồng bộ **một lần** khi nâng cấp bản này; có mốc trong bảng `AppMaintenance`. Khởi động lại không đặt mật khẩu người dùng đã đổi về `Demo@123`.

Nếu cài mới hoàn toàn với database rỗng, các tên mặc định là `admin`, `trungdoi`, `phongqlhv`, `bomon`, cũng dùng `Demo@123`. Đây không phải dữ liệu của gói bàn giao đang có 5 tài khoản ở trên. Không xóa database để sửa lỗi đăng nhập.

## Các lỗi đã sửa và nguyên nhân

| Vấn đề | Nguyên nhân trong source | Cách sửa |
| --- | --- | --- |
| Đăng nhập không có mắt | Input chỉ có `type="password"`, không có nút chuyển hiển thị | Thêm mắt có nhãn Hiện/Ẩn; dùng được bằng bàn phím |
| Nhập sai rồi thấy lại `phongqlhv` | Server chuyển về trang đăng nhập mới, làm mất tên vừa nhập; trình duyệt có tài khoản đã lưu như ảnh | Đăng nhập sai trả lỗi ngay tại form, giữ tên vừa gửi, xóa ô mật khẩu, không tải lại trang |
| Tên/mật khẩu trong tệp ban đầu không khớp | Tệp cũ không cập nhật sau khi đổi tên/tạo tài khoản | Đối chiếu database, đặt 5 mật khẩu thành `Demo@123`, cập nhật tệp bàn giao |
| Hai khối tải tài khoản giống nhau | `Admin.razor` lặp hai khối cùng điều kiện, cùng gọi `DownloadCredentials` | Còn một khối, một nút **Tải thông tin tài khoản (Excel)** |
| Admin chưa xem được mật khẩu | Database chỉ có hash; giao diện ghi “Đã thiết lập” | Thêm mắt ở từng tài khoản; chỉ giải mã khi admin bấm; ghi nhật ký |
| Ô mật khẩu mới không có mắt | Chưa có điều khiển hiển thị | Bổ sung ở cấp tài khoản, đặt lại, sửa thông tin và trang tự đổi mật khẩu |
| Duyệt đơn hiển thị trộn lớp | Tab chờ duyệt đưa toàn bộ danh sách vào bảng chung | Bắt đầu bằng danh sách lớp và số đơn chờ; chọn lớp mới thấy thao tác |
| Có thể thao tác duyệt từ bảng chung | Bảng tái sử dụng tự hiện nút theo vai trò | Chỉ bật nút duyệt ở màn hình lớp; backend bắt buộc kiểm tra lớp của đơn |
| Nội dung “đã duyệt” khi mới gửi | Thông báo/nhật ký và trường người duyệt chung được ghi ngay khi lập | Thông báo đúng “đã gửi/chờ duyệt”; người duyệt cuối chỉ ghi khi Phòng xử lý |
| In giấy cho phép khi đơn còn chờ | Nút in hiện cả cho đơn chờ | Chỉ in giấy cho phép với đơn đã duyệt, kiểm tra cả trang in |
| Thanh quản lý lớp bị chật chữ | Quá nhiều nhóm điều khiển chung hàng | Cho phép xuống hàng, nút không bị ép hẹp; bỏ phần gán trung đội |
| Sửa học viên bị lệch cột | Dòng sửa có thêm một ô lớp nhưng tiêu đề không có cột tương ứng | Đưa trường lớp vào cùng ô thông tin; đồng bộ số cột |
| Mật khẩu có thể mất khả năng xem sau phục hồi | Sao lưu cũ chỉ chứa hai DB, thiếu khóa giải mã mới | Sao lưu/khôi phục kèm `keys/key-*.xml`; chặn đường dẫn khóa không hợp lệ |

Ứng dụng không gán cứng tên đăng nhập `phongqlhv`. Hiện tượng trong ảnh phù hợp với **Saved passwords/autofill** của trình duyệt, không phải cache dữ liệu nghỉ phép. Phần mềm không thể xóa tài khoản đã lưu trong trình duyệt. Nếu lần đầu mở trang vẫn tự điền mật khẩu cũ, cập nhật hoặc xóa mục đã lưu cho `localhost:10999` trong trình quản lý mật khẩu của trình duyệt.

## Luồng duyệt theo lớp (phương án A)

1. Phòng QLHV tạo lớp và phân công tài khoản **Chủ nhiệm lớp**; một chủ nhiệm có thể quản lý nhiều lớp.
2. Chủ nhiệm chỉ thấy lớp được phân công. Chọn lớp, lập nghỉ tranh thủ/cuối tuần/lễ Tết rồi bấm **Gửi Phòng QLHV duyệt**.
3. Đơn mang lớp của học viên trong danh sách gốc, ở trạng thái **Chờ duyệt Phòng QLHV**. Chủ nhiệm không tự duyệt cuối.
4. Phòng QLHV vào **Đơn chờ duyệt**, chọn lớp, kiểm tra từng đơn rồi **Duyệt** hoặc **Từ chối**. Từ chối bắt buộc lý do.
5. Khoa/bộ môn chỉ thấy các đơn đã duyệt thuộc phạm vi môn/lớp được phân công.

Backend chặn duyệt sai lớp, thiếu lớp, sai vai trò và duyệt lại đơn đã xử lý. Hủy đơn vẫn yêu cầu lý do và giữ lịch sử. Muốn sửa thông tin nghỉ sau khi gửi, hủy đơn rồi lập lại. Định mức nghỉ tranh thủ hiện là cảnh báo, không phải chặn cứng; giữ nguyên quy tắc này của dự án.

## Danh mục trung đội trước đây dùng để làm gì?

Phần này tạo danh sách tên trung đội, rồi gán tên cho học viên trong một lớp thông qua `SetClassPlatoon`. Nó không phải cơ chế quyết định tài khoản được quản lý lớp nào: quyền đó dựa trên `Classes.ManagerUserId`.

Đã bỏ danh mục trung đội, điều khiển gán trung đội và các hàm quản lý danh mục tương ứng. Giữ quản lý **Lớp**, phân công chủ nhiệm và lịch nghỉ lễ/Tết. Đổi nhãn vai trò thành **Chủ nhiệm lớp** trên giao diện và giấy in. Mã vai trò nội bộ `TrungDoi` và các cột dữ liệu cũ còn giữ để tương thích database/lịch sử, không còn là một danh mục trên giao diện.

## Mật khẩu và nút mắt của admin

- Identity vẫn xác thực bằng **hàm băm**. Bản băm không thể giải ngược ra mật khẩu cũ.
- Để đáp ứng yêu cầu admin xem lại mật khẩu hiện tại, hệ thống lưu thêm một bản **mã hóa** bằng ASP.NET Core Data Protection, gắn với ID tài khoản.
- Sau lần đặt lại này, admin có thể xem `Demo@123` hoặc mật khẩu mới nhất. Không thể khôi phục mật khẩu trước đó đã chỉ được lưu dưới dạng hash.
- Các đường tạo, admin đặt lại, đổi mật khẩu khi sửa hồ sơ, tự đổi và lệnh quản trị đều đồng bộ bản mã với hash. Tên tài khoản có thể thay đổi nhưng liên kết theo ID được giữ.
- Danh sách tài khoản không tự tải mật khẩu ra frontend. Mật khẩu chỉ được trả sau khi kiểm tra quyền admin khi bấm mắt; mỗi lần xem có nhật ký, không ghi mật khẩu vào nhật ký.
- Giữ kín cả DB và thư mục khóa. Vì có chức năng admin xem lại, đây là mô hình có mật khẩu phục hồi được theo yêu cầu bài demo, khác mô hình chỉ lưu hash. Không đưa gói có dữ liệu/mật khẩu này lên GitHub công khai.

## Bảo toàn dữ liệu

Đã so sánh trước và sau: giữ nguyên **1 lớp B10D57, 12 học viên, 21 đơn nghỉ** cùng dữ liệu nghiệp vụ có trong ZIP nguồn. Các ảnh chụp có thể ở thời điểm khác (ảnh hiển thị 23 đơn và tên lớp đã đổi); không có thêm hai đơn đó trong database được đính kèm. Không tạo giả dữ liệu để khớp ảnh. Database nguồn không có môn học; cần chủ nhiệm khai báo/gán khoa cho môn trước khi tạo đơn nghỉ tranh thủ mới.

Các bản sao lưu cũ được giữ. Khôi phục bản sao lưu cũ có thể đưa tên tài khoản/dữ liệu về thời điểm cũ; chúng không thay thế database bàn giao hiện tại.

## Kết quả kiểm tra

- Biên dịch ứng dụng và dự án kiểm thử bằng .NET 8: **0 lỗi, 0 cảnh báo**.
- **150 kiểm tra nghiệp vụ** đạt: phân quyền, phạm vi lớp, duyệt, tài khoản, thu hồi phiên, Excel, bảo toàn dữ liệu, sao lưu và giải mã sau phục hồi.
- **26 kiểm tra HTTP** đạt: đăng nhập, các trang theo vai trò, chống CSRF, chặn truy cập AppData, đổi mật khẩu và thu hồi cookie cũ.
- **15 kiểm tra JavaScript bằng DOM giả lập** đạt: mắt mật khẩu, giữ tên khi sai, xóa mật khẩu, lỗi ngay tại form, giới hạn đăng nhập và chống gửi lặp.
- **17 kiểm tra trên bản dữ liệu bàn giao** đạt: đăng nhập cả 5 tài khoản với `Demo@123`, trang theo vai trò, mắt admin trong HTML, danh sách lớp chờ duyệt, bỏ danh mục trung đội và tệp tài khoản chính xác.

Môi trường kiểm tra dùng .NET 8 SDK và đúng các thư viện .NET/EF Core đi kèm ZIP nguồn để biên dịch do NuGet trực tuyến không khả dụng. Các tệp dự án bàn giao vẫn dùng PackageReference chuẩn (8.0.22), không gắn đường dẫn của máy kiểm tra.

**Giới hạn:** chưa kiểm tra trực quan/click toàn bộ giao diện trong trình duyệt, vì trình duyệt kiểm tra không truy cập được ứng dụng cục bộ. Chưa chạy Visual Studio trên máy Windows của bạn. Các kiểm tra JavaScript giả lập không thay thế kiểm tra hiển thị thực tế.

Chạy kiểm thử sau khi cài SDK:

```sh
dotnet run --project Tests -c Release
dotnet build QuanLyNghiPhep.sln -c Release
node Tests/http_smoke.mjs
node Tests/login_ui.mjs
```

Kiểm tra giao diện trên máy bạn: nhập sai tên/mật khẩu; thử mắt đăng nhập; admin bấm mắt tài khoản và đổi mật khẩu; tải thông tin vừa cấp; chủ nhiệm gửi đơn; Phòng chọn đúng lớp để duyệt; thử thanh quản lý lớp ở cửa sổ hẹp. Không cần chạy các script dọn dữ liệu cũ để khởi động ứng dụng.

## Đưa lên server với GitHub Actions

Trên server **Ubuntu/Debian x86-64 dùng systemd**, chuẩn bị Docker Engine và Docker Compose v2 có hỗ trợ `docker compose up --wait`. SSH phải truy cập được bằng khóa. Tài khoản SSH có thể là root hoặc có quyền chạy `sudo -n bash`. Workflow cài Nginx, Certbot và curl nếu còn thiếu.

1. Tạo tên miền mới trỏ DNS về IP server. Mở TCP **80/443** cho HTTP và HTTPS, cùng cổng SSH để GitHub Actions kết nối. Nếu có bản ghi AAAA, IPv6 cũng phải trỏ đúng server. Cổng **10999** chỉ được publish ở `127.0.0.1` cho Nginx.
2. Giải nén và đưa **nội dung bên trong thư mục `Hoanganh123`** lên thư mục gốc của repo GitHub, gồm cả `.github` và `.dockerignore`. Chỉ đưa mã nguồn lên GitHub. `AppData` trong ZIP dành cho bản chạy cục bộ và đã được loại khỏi Git/Docker. Nếu repo từng track thư mục này, dùng `git rm -r --cached --ignore-unmatch QuanLyNghiPhep/AppData` trước khi commit. Dùng `git status` để kiểm tra.
3. Trong **Settings → Environments → production**, thêm các giá trị dưới đây. Tạo environment và khai báo trước khi push lên `main`.
4. Push lên `main` hoặc mở **Actions → CI/CD QLHV Pro Max → Run workflow** và chọn `main`. Khi cả CI và deploy đều thành công, truy cập `https://<DEPLOY_DOMAIN>`.

| Loại | Tên | Giá trị |
| --- | --- | --- |
| Variable | `DEPLOY_DOMAIN` | Tên miền mới, chữ thường, không kèm giao thức/cổng, ví dụ `qlhv.example.com` |
| Variable | `CERTBOT_EMAIL` | Email nhận thông báo về chứng chỉ Let's Encrypt |
| Variable | `SERVER_PORT` | Cổng SSH, mặc định `22` |
| Secret | `SERVER_HOST` | IP hoặc hostname SSH của server |
| Secret | `SERVER_USER` | Tài khoản SSH có quyền triển khai |
| Secret | `SERVER_SSH_KEY` | Toàn bộ private key của tài khoản triển khai, dạng dùng được trong CI không cần nhập passphrase |
| Secret | `SERVER_KNOWN_HOSTS` | Dòng khóa máy chủ SSH đã xác minh, dạng `host ssh-ed25519 AAAA...`. Với cổng tùy chỉnh dùng `[host]:port ssh-ed25519 AAAA...`. Lấy phần khóa công khai từ `/etc/ssh/ssh_host_ed25519_key.pub` qua console tin cậy của server. |

Public key tương ứng với `SERVER_SSH_KEY` cần nằm trong `~/.ssh/authorized_keys` của `SERVER_USER`. Chỉ cho phép commit tin cậy vào nhánh `main` vì workflow dùng quyền quản trị server.

**Luồng CI/CD.** Pull request vào `main` chạy build, kiểm thử nghiệp vụ, HTTP, JavaScript và smoke test Docker/Nginx, gồm HTTPS và WebSocket Blazor. Push hoặc chạy thủ công trên `main` triển khai image đã qua CI. Workflow checkout đúng commit, đối chiếu SHA-256 của file `qlhvpromax` với bản lưu trên server và chỉ gửi mẫu Nginx khi khác. Server áp cấu hình vào `/etc/nginx/sites-available/qlhvpromax`, chạy `nginx -t` và reload khi cấu hình đổi. Certbot xác thực HTTP-01 qua cổng 80. File `qlhvpromax` ở gốc repo là mẫu HTTP. Phần lắng nghe HTTPS và đường dẫn chứng chỉ được tạo trên server trong `/etc/nginx/qlhvpromax/tls.conf`. Certbot giữ chứng chỉ còn hạn và dùng timer để gia hạn. Deploy kiểm tra `/healthz` trong Docker và qua tên miền, đồng thời thử mở trang đăng nhập HTTPS qua Nginx. Nếu container mới lỗi, script thử chạy lại image trước đó với cùng volume.

**Đăng nhập lần đầu trên server.** Container Production mới tạo một admin có mật khẩu ngẫu nhiên và lưu trong volume `qlhvpromax_appdata`. Mở SSH với quyền root trên server (dùng `sudo -i` nếu đang đăng nhập bằng tài khoản triển khai khác) để xem thông tin, rồi đăng nhập bằng HTTPS, đổi mật khẩu và xóa tệp cấp ban đầu. Đừng in tệp này vào log CI.

```bash
cd /opt/qlhvpromax/current
docker compose --env-file release.env exec -T qlhvpromax cat /app/AppData/admin-ban-dau.txt
# Sau khi đổi mật khẩu
docker compose --env-file release.env exec -T qlhvpromax rm /app/AppData/admin-ban-dau.txt
```

Volume chứa SQLite, các bản sao lưu và khóa mã hóa. Không dùng `docker compose down -v` trên production. Container mới bắt đầu với dữ liệu trống. Nếu cần chuyển dữ liệu cũ, sao lưu/copy cả `AppData` và `keys` qua kênh riêng khi ứng dụng đã dừng. Đổi mật khẩu bàn giao của các tài khoản trước khi dùng trên server. Production chặn mật khẩu `Demo@123`. Docker/Nginx tự khởi động lại theo cấu hình. Một lần cập nhật container có thể ngắt WebSocket đang mở và phiên đăng nhập vẫn theo thời hạn của ứng dụng.

Cổng HTTPS khi chạy từ Visual Studio là `https://localhost:10999`. Profile HTTP cục bộ dùng `5187` để hai giao thức không tranh cổng. Docker chạy HTTP nội bộ ở `10999` và Nginx xử lý HTTPS ở `443`.

Nếu subnet Docker mặc định `172.30.99.0/24` đã được dùng trên server, đặt `QLHV_DOCKER_SUBNET` và `QLHV_PROXY_GATEWAY` của một subnet trống trong `/opt/qlhvpromax/network.env` trước khi deploy lần đầu. Kestrel chỉ tin header của Nginx qua gateway đã khai báo. Nếu dùng nhiều site Nginx, đặt tên miền mới riêng để tránh trùng `server_name`.

Tài liệu: [GitHub deployment environments](https://docs.github.com/en/actions/concepts/workflows-and-actions/deployment-environments) · [Nginx WebSocket proxy](https://nginx.org/en/docs/http/websocket.html) · [Certbot webroot và renewal](https://eff-certbot.readthedocs.io/en/stable/using.html).
