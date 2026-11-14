# Prompt Notebook (v1.0)

Ứng dụng Windows chuyên lưu trữ, quản lý, chỉnh sửa và sao chép prompt AI với hiệu năng tức thì, hoạt động hoàn toàn offline.

## 1. Đặc điểm nổi bật
- **Gọn nhẹ & Phản hồi tức thì**: Xây dựng bằng **C# / .NET 8 (hoặc mới hơn) / WPF** thuần và **Microsoft.Data.Sqlite**, không dùng ORM nặng, không dùng Electron hay trình duyệt nhúng.
- **Hoạt động 100% Offline**: Không cần tài khoản, không kết nối Internet, dữ liệu lưu trực tiếp trên máy tại `%LocalAppData%\PromptNotebook\prompts.db`.
- **Hỗ trợ đa ngôn ngữ Unicode đầy đủ**: Tiếng Việt, tiếng Anh và tiếng Trung (Giản thể / Phồn thể) không lỗi font, hỗ trợ tìm kiếm tức thì cả tên và nội dung.
- **Giao diện 2 cột tối ưu (40% / 60%)**:
  - **Cột trái (40%)**: Thanh tìm kiếm tức thì, bộ lọc danh mục, lọc yêu thích, thùng rác, danh sách ảo hóa (`VirtualizingStackPanel`) cuộn mượt với hơn 5.000 prompt. Tên prompt dài (150–300+ ký tự) tự động xuống dòng (`TextWrapping="Wrap"`), không bị cắt chữ. Hỗ trợ menu chuột phải (*Đổi tên, Nhân bản, Yêu thích, Xóa/Khôi phục, Xóa vĩnh viễn*) và kéo `GridSplitter` thay đổi độ rộng cột (tự động ghi nhớ).
  - **Cột phải (60%)**: Hiển thị tên đầy đủ, trình soạn thảo văn bản thuần nhiều dòng, tự động lưu sau **700ms** ngừng gõ, lưu tức thì khi chuyển prompt hoặc đóng ứng dụng, hiển thị trạng thái *Đang lưu... / Đã lưu*, nút **Sao chép nội dung** 1 click, chỉnh cỡ chữ (`A-` / `A+`).
- **An toàn dữ liệu**:
  - Xóa mềm vào **Thùng rác** và hỗ trợ khôi phục bất kỳ lúc nào.
  - Tự động sao lưu cơ sở dữ liệu bằng cơ chế Online Backup an toàn của SQLite tại `%LocalAppData%\PromptNotebook\Backups`, tự động giữ **7 bản sao lưu gần nhất**.
  - Hỗ trợ **Nhập / Xuất file JSON UTF-8**.
  - **Nhập file TXT/Markdown**: Bấm **Nhập TXT/MD** để chọn nhiều file `.txt`/`.md` trong thư mục, hoặc kéo thả file vào bất kỳ vị trí nào trong cửa sổ. Mỗi file tạo một prompt mới, lấy tên file (bỏ phần mở rộng) làm tiêu đề và giữ nguyên nội dung, kể cả dấu xuống dòng. File Unicode UTF-8 và UTF-16/UTF-32 có BOM được hỗ trợ. Prompt được thêm vào danh mục đang lọc (hoặc **Chung** nếu đang xem tất cả); ứng dụng bỏ các bộ lọc sau khi nhập để hiển thị prompt mới. File lỗi được báo riêng và không cản các file hợp lệ; nhập lại cùng file sẽ tạo thêm prompt mới.
- **Giao diện Tiếng Việt**: Mặc định **Dark Mode**, chuyển đổi nhanh sang **Light Mode**, sử dụng font `Segoe UI`, ghi nhớ kích thước và vị trí cửa sổ.

## 2. Phím tắt hỗ trợ
| Phím tắt | Chức năng |
| :--- | :--- |
| `Ctrl + N` | Tạo prompt mới |
| `Ctrl + F` | Đưa con trỏ vào ô tìm kiếm |
| `Ctrl + S` | Lưu ngay lập tức |
| `Ctrl + Shift + C` | Sao chép toàn bộ nội dung prompt đang chọn vào Clipboard |
| `Delete` | Xóa prompt đang chọn vào thùng rác (có xác nhận) |
| `Esc` | Xóa từ khóa tìm kiếm hoặc bỏ chọn prompt / đóng hộp thoại |
| `Ctrl + Z` / `Ctrl + Y` / `Ctrl + A` | Hoàn tác / Làm lại / Chọn toàn bộ trong trình soạn thảo |

## 3. Yêu cầu hệ thống
- Hệ điều hành: **Windows 10 / Windows 11 (64-bit / x64)**.
- Runtime: **.NET 8 (hoặc mới hơn) Desktop Runtime (x64)** (nhờ sử dụng chế độ framework-dependent portable nên dung lượng bộ chạy rất nhỏ gọn, chỉ vài trăm KB).
- Để biên dịch từ mã nguồn: Cài đặt **.NET 8 SDK** (hoặc mới hơn).

## 4. Hướng dẫn Build bằng một lệnh duy nhất

Mở **PowerShell** hoặc **Command Prompt** tại thư mục gốc chứa file `PromptNotebook.sln` và chạy đúng **1 lệnh** sau để xuất bản Release Portable (`win-x64`):

```powershell
dotnet publish src/PromptNotebook/PromptNotebook.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/PromptNotebook-win-x64-portable
```

*(Hoặc nhấp đúp vào file `build-release.bat` đi kèm).*

Sau khi lệnh chạy xong, file thực thi nằm tại:
`publish/PromptNotebook-win-x64-portable/PromptNotebook.exe`

*(Lưu ý: Nếu muốn đóng gói kèm cả .NET Runtime vào một file `.exe` độc lập không cần cài đặt trước .NET Desktop Runtime, chỉ cần đổi `--self-contained false` thành `--self-contained true`).*

## 5. Chạy bộ kiểm thử nghiệm thu (5.000 Prompt & Benchmark < 200ms)

Dự án đi kèm bộ kiểm thử tự động tại `tests/PromptNotebook.Tests`. Chạy lệnh sau để kiểm tra toàn bộ tiêu chí nghiệm thu với 5.000 prompt mẫu:

```powershell
dotnet run --project tests/PromptNotebook.Tests/PromptNotebook.Tests.csproj -c Release
```

## 6. Cấu trúc thư mục mã nguồn
```text
PromptNotebook/
├── PromptNotebook.sln                 # File Solution Visual Studio
├── build-release.bat                  # Script build bản Release x64 bằng 1 click
├── README.md                          # Tài liệu hướng dẫn tiếng Việt
├── src/
│   └── PromptNotebook/
│       ├── PromptNotebook.csproj      # Cấu hình project .NET 8 WPF (win-x64)
│       ├── App.xaml / App.xaml.cs     # Điểm khởi chạy, nạp theme & xử lý lỗi toàn cục
│       ├── Helpers/
│       │   ├── ObservableObject.cs    # Lớp cơ sở INotifyPropertyChanged tối giản
│       │   └── RelayCommand.cs        # Lớp ICommand tối giản cho MVVM
│       ├── Models/
│       │   ├── PromptItem.cs          # Thực thể Prompt (Id, Title, Content, Category...)
│       │   └── AppSettings.cs         # Cấu hình lưu vị trí cửa sổ, độ rộng cột, cỡ chữ, theme
│       ├── Services/
│       │   ├── AppPaths.cs            # Định nghĩa %LocalAppData%\PromptNotebook\prompts.db
│       │   ├── DatabaseService.cs     # Truy cập SQLite thuần, tìm kiếm Unicode & Backup 7 bản
│       │   ├── JsonExportImportService.cs # Xuất / Nhập JSON chuẩn UTF-8
│       │   └── SettingsService.cs     # Đọc / ghi settings.json
│       ├── Themes/
│       │   ├── DarkTheme.xaml         # Giao diện Tối (Mặc định)
│       │   └── LightTheme.xaml        # Giao diện Sáng
│       ├── ViewModels/
│       │   └── MainViewModel.cs       # Logic 2 cột, debounce lưu 700ms, lọc & tìm kiếm
│       └── Views/
│           ├── MainWindow.xaml        # Giao diện chính 2 cột (40%/60%), VirtualizingStackPanel
│           ├── MainWindow.xaml.cs     # Phím tắt, menu chuột phải, lưu bố cục khi đóng
│           ├── RenameDialog.xaml      # Hộp thoại đổi tên & danh mục
│           └── RenameDialog.xaml.cs
└── tests/
    └── PromptNotebook.Tests/
        ├── PromptNotebook.Tests.csproj
        └── Program.cs                 # Kiểm thử nghiệm thu tự động 5.000 prompt
```
