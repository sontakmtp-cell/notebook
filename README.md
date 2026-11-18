# Notebook 2.0

Notebook là trình soạn thảo Windows cho tệp **TXT, JSON và Markdown**, dùng được offline. Mở nhiều tài liệu bằng tab, kéo thả tệp vào cửa sổ và chuyển giữa giao diện tối và sáng.

Khi mở tệp từ File Explorer, Notebook dùng lại cửa sổ đang chạy. Nếu tệp đã có tab, Notebook chuyển sang tab đó.

## Chạy ứng dụng

Mở `PromptNotebook.exe` trên Windows x64. Máy cần **.NET 8 Desktop Runtime x64**. Các tệp được mở từ ổ đĩa và lưu lại theo mã hóa hiện có. Nếu tạo tệp mới, Notebook dùng UTF-8.

1. Bấm **Mới** hoặc mũi tên cạnh nút này để chọn TXT, JSON hay Markdown. Bấm **Mở** để chọn một hoặc nhiều tệp. Mũi tên cạnh **Mở** hiện các tệp gần đây.
2. Với JSON, dùng **Định dạng**, **Thu gọn** hoặc **Kiểm tra**. Khi có lỗi cú pháp, Notebook báo dòng và cột.
3. Với Markdown, gõ cú pháp ngay trong vùng soạn thảo. Khi chuyển con trỏ sang dòng khác, Notebook ẩn các dấu Markdown và giữ cách trình bày. Đưa con trỏ trở lại dòng để sửa cú pháp. Chọn **Sao chép code** ở đầu khối code, hoặc đặt con trỏ trong khối rồi bấm `Ctrl+Shift+C`.
4. Bấm **Lưu** để ghi vào tệp. Mở mũi tên cạnh nhóm nút lưu để dùng **Lưu tất cả**. Notebook hỏi trước khi đóng tab còn thay đổi.
5. Mở nút bánh răng để đổi giao diện, cỡ chữ và tự động xuống dòng.

| Phím tắt | Thao tác |
| --- | --- |
| Ctrl+N / Ctrl+O | Tạo TXT / mở tệp |
| Ctrl+S / Ctrl+Shift+S / Ctrl+Alt+S | Lưu / lưu thành / lưu tất cả |
| Ctrl+W / Ctrl+Tab / Ctrl+Shift+Tab | Đóng tab / tab tiếp / tab trước |
| Ctrl+Z / Ctrl+Y | Hoàn tác / làm lại |
| Ctrl+F / F3 / Shift+F3 | Tìm / kết quả tiếp / kết quả trước |
| Ctrl+Shift+I | Định dạng JSON |
| Ctrl+bấm vào liên kết Markdown | Sao chép địa chỉ liên kết |

Ảnh Markdown hỗ trợ các tệp ảnh cục bộ mà Windows đọc được. Lưu tệp Markdown trước khi dùng đường dẫn ảnh tương đối. Với ảnh trên web, Notebook hiện mô tả thay thế. Code fence có màu cú pháp cho các ngôn ngữ có sẵn như JSON, C#, JavaScript, Python và XML. Khối lớn vẫn có nền riêng và chữ đơn cách.

Notebook giữ bản nháp sau khi ngừng gõ. Sau khi ứng dụng đóng bất ngờ, bản nháp mở lại trong tab có nhãn **Khôi phục**. Dùng **Lưu** để chọn đường dẫn. Cài đặt, tệp gần đây và bản nháp nằm trong `%LocalAppData%\NotebookEditor`. Dữ liệu Prompt Notebook 1.x vẫn nằm trong `%LocalAppData%\PromptNotebook`.

## Build Release

Dùng .NET 8 SDK hoặc SDK mới hơn hỗ trợ đích .NET 8. Chạy `build-release.bat` hoặc lệnh sau từ thư mục gốc:

```powershell
dotnet publish src/PromptNotebook/PromptNotebook.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/PromptNotebook-win-x64-portable
```

Thư mục phát hành chứa `PromptNotebook.exe` và `THIRD-PARTY-NOTICES.txt`.

## Thư viện và kiểm thử

- **.NET 8 / WPF** cho cửa sổ Windows, hộp thoại tệp và giao diện.
- **AvalonEdit 6.3.1.120** cho tab tài liệu, thao tác chỉnh sửa và dựng phần văn bản đang nhìn thấy.
- **Markdig 1.6.0** để nhận diện cú pháp và vị trí của từng phần trong Markdown gốc.

Xem [kết quả nghiệm thu](docs/acceptance.md) và [thiết kế](docs/editor-design.md).
