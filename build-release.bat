@echo off
setlocal
pushd "%~dp0" || exit /b 1

dotnet publish "src\PromptNotebook\PromptNotebook.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "publish\PromptNotebook-win-x64-portable"

if errorlevel 1 (
    popd
    exit /b 1
)

echo Release: "%CD%\publish\PromptNotebook-win-x64-portable\PromptNotebook.exe"
popd
exit /b 0
