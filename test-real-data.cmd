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
if "%~1"=="" (
    echo 请把仅存放 elements.data 样本的文件夹拖到此脚本上。
    echo 也可在命令行执行：test-real-data.cmd "D:\elements-samples"
    pause
    exit /b 1
)
dotnet run --project "%~dp0Tests\CfgRegression\CfgRegression.csproj" -c Release -- "%~dp0PerfectWorld\cfg" "%~1" > "%~dp0real-data-result.txt" 2>&1
set "test_result=%errorlevel%"
type "%~dp0real-data-result.txt"
if not "%test_result%"=="0" echo 测试失败，请保留 real-data-result.txt 中的错误。
if "%test_result%"=="0" echo 全部测试通过。输入文件未修改。
pause
exit /b %test_result%
