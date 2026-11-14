@echo off
chcp 65001 >nul
echo ==========================================================
echo   BUILD VA DONG GOI PROMPT NOTEBOOK (RELEASE WIN-X64)
echo ==========================================================

dotnet publish src\PromptNotebook\PromptNotebook.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish\PromptNotebook-win-x64-portable

if %ERRORLEVEL% NEQ 0 (
    echo [LOI] Qua trinh build that bai. Vui long kiem tra .NET SDK.
    exit /b %ERRORLEVEL%
)

echo.
echo [HOAN TAT] Ban Release Portable da duoc tao tai:
echo   publish\PromptNotebook-win-x64-portable\PromptNotebook.exe
