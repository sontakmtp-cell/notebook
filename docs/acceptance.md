# Nghiệm thu Notebook 2.0

Kiểm tra ngày 18/11/2026 trên Windows 11 Pro 24H2 (build 26100), Core i3-12100F, ổ NTFS. Ứng dụng chạy với .NET 8 Desktop Runtime x64. Các bài thử dùng tệp mẫu riêng.

| Hạng mục | Kết quả |
| --- | --- |
| TXT và Unicode | Mở, sửa và lưu lại tiếng Việt, chữ Trung Quốc và emoji. Kiểm tra từng byte cho UTF-8, UTF-8 BOM, UTF-16 LE/BE có BOM và các mẫu UTF-16 LE/BE nhận diện được dù thiếu BOM. Giữ LF và CRLF trong cùng tệp. |
| JSON | Định dạng, thu gọn, tô màu cả hai giao diện, báo dòng/cột sai và tự thụt lề. Kiểm tra thao tác hoàn tác/làm lại cùng thay đổi thụt lề. Lỗi thêm dòng trắng khi định dạng trên Windows được phát hiện khi thử desktop và đã có bài kiểm tra hồi quy chạy qua. |
| Markdown | Một AvalonEdit định dạng nội dung ngay tại chỗ. Kiểm tra tiêu đề, kiểu chữ, danh sách lồng, trích dẫn, liên kết, ảnh, bảng, đường kẻ và code fence. Nhấn vào dòng làm hiện cú pháp gốc. Đã sửa, sao chép/dán, hoàn tác/làm lại, lưu và đọc lại đúng Markdown và BOM. Nút Sao chép code đưa phần mã vào clipboard. |
| Tab, tìm kiếm, tệp | Mở các tab thật, giữ tài liệu và lịch sử riêng, tìm tiếng Việt không phân biệt hoa/thường, Lưu tất cả, tệp gần đây, và hộp thoại Hủy/Bỏ thay đổi khi đóng. Kiểm tra không ghi đè khi dấu thời gian hoặc kích thước tệp đích vừa thay đổi. |
| Khôi phục | Bản nháp được ghi sau khi ngừng gõ. Kiểm tra bằng cách giải phóng khóa của phiên trước mà chưa dọn bản nháp, sau đó phục hồi qua phiên mới. Bản nháp của phiên đang chạy không bị lấy sang phiên khác. |
| Giao diện | Kiểm tra trực tiếp giao diện tối/sáng và thao tác gõ, đổi tab, đóng tab. Bài thử WPF kiểm tra các nhóm nút còn vừa ở chiều rộng tối thiểu. |
| Offline | Chặn kết nối đi ra bằng quy tắc Windows Firewall riêng cho hai file EXE dùng để thử nghiệm. Bộ kiểm thử vẫn qua, bản Release mở được TXT/JSON/MD và không có kết nối TCP đang mở trong các lần đo. Quy tắc tạm đã được gỡ. |
| Release | Build Release thành công, không có cảnh báo hay lỗi biên dịch. |

## Đo trên máy thử

- Cửa sổ trống xuất hiện trong **722–926 ms** ở ba lần khởi chạy liên tiếp. Working set sau khoảng nửa giây là **119,7–120,8 MiB**. Bộ nhớ riêng là **77,4–78,2 MiB**.
- Khi mở ba tệp nhỏ từ dòng lệnh, tiêu đề tab Markdown xuất hiện sau **956 ms**. Working set là **132,3 MiB**.
- Với Markdown **1.520.000 byte, 64.001 dòng**, mở tệp mất **101–153 ms**. Vùng nhìn đầu tiên định dạng xong sau **389–476 ms** và có **21 dòng** được AvalonEdit dựng trong vùng nhìn. Đây là số đo trên cấu hình trên.

## Chạy lại

```powershell
dotnet build PromptNotebook.sln -c Release
dotnet run --project tests/PromptNotebook.Tests -c Release
dotnet run --project tests/PromptNotebook.Tests -c Release -- --clipboard
```

Tùy chọn `--clipboard` kiểm tra nút sao chép code và thao tác sao chép/dán với clipboard hệ thống, rồi khôi phục clipboard trước thử nghiệm. Lượt thử trực tiếp dùng Windows 11. Windows 10 chưa được thử trên máy riêng.
