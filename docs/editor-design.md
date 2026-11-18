# Thiết kế Notebook

## Mã nguồn gốc

Base `d9866cc` là Prompt Notebook chạy trên WPF, lưu prompt vào SQLite và xem Markdown qua một khung riêng. Bản mới giữ WPF, tài nguyên giao diện tối/sáng, lớp thông báo thay đổi và cách lưu tùy chọn bằng JSON. Các màn hình thư viện prompt, đăng nhập và xem trước thuộc phiên bản cũ, có thể tìm lại trong lịch sử Git. Bản mới dùng thư mục cấu hình riêng `%LocalAppData%\NotebookEditor`. Thư mục `%LocalAppData%\PromptNotebook` vẫn thuộc bản cũ.

## Giao diện

- Dùng thanh tiêu đề chuẩn của Windows để giữ thao tác thu phóng, kéo cửa sổ và menu hệ thống.
- Dưới thanh tiêu đề là các thao tác tệp, hoàn tác, tìm kiếm và cài đặt. Tài liệu JSON có thêm nhóm kiểm tra và định dạng.
- Một hàng tab có dấu thay đổi chưa lưu. Vùng soạn thảo chiếm phần còn lại, có thanh trạng thái ở đáy.
- Mỗi tab giữ văn bản, lịch sử hoàn tác, vùng chọn và vị trí cuộn. Cửa sổ dùng một AvalonEdit để hiển thị tài liệu hiện tại.

## Markdown và lưu tệp

- Markdown gốc nằm trong cùng tài liệu mà AvalonEdit chỉnh sửa. Markdig phân tích phiên bản văn bản sau khi người dùng ngừng gõ. AvalonEdit dùng các phạm vi nguồn để trình bày nội dung định dạng và hiện cú pháp tại dòng đang chỉnh sửa.
- Tệp mở/lưu giữ kiểu mã hóa và phần xuống dòng hiện có. Bản nháp tự động được giữ cục bộ để khôi phục sau khi ứng dụng bị đóng bất ngờ. Lệnh Lưu ghi vào tệp người dùng đã chọn.
- Hình ảnh Markdown lấy từ tệp trên ổ đĩa cục bộ. Trình soạn thảo hoạt động với các gói đã đóng kèm khi phát hành.
