@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo 未找到 dotnet。请先安装 .NET 10 SDK。
    pause
    exit /b 1
)
echo 正在执行全部 CFG 的 C# 模拟回归检查...
dotnet run --project "%~dp0Tests\CfgRegression\CfgRegression.csproj" -c Release -- "%~dp0PerfectWorld\cfg" > "%~dp0cfg-regression-result.txt" 2>&1
if errorlevel 1 (
    type "%~dp0cfg-regression-result.txt"
    echo 回归检查失败，尚未发布。请保留上方错误用于定位。
    pause
    exit /b 1
)
type "%~dp0cfg-regression-result.txt"
echo 正在编译 Windows x64 自包含版本...
dotnet publish "%~dp0MyAceEditor.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0dist"
if errorlevel 1 (
    echo 发布失败。请检查上方错误及 .NET SDK、NuGet 连接。
    pause
    exit /b 1
)
echo 发布完成：dist\MyAceEditor.exe
echo 请保留整个 dist 文件夹及 PerfectWorld\cfg 中的全部配置。
pause
endlocal
