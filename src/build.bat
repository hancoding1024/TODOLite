@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [错误] 未检测到 .NET Framework 4.x 自带的 csc.exe。
    echo        请先安装 .NET Framework 4.8 运行时/开发包后重试。
    echo        已尝试路径：
    echo          %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    echo          %WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
    pause
    exit /b 1
)

echo 使用编译器: %CSC%

set "WEBEXT=System.Web.Extensions.dll"
for /d %%D in ("%WINDIR%\Microsoft.NET\Assembly\GAC_MSIL\System.Web.Extensions\v4.0_*") do (
    if exist "%%~D\System.Web.Extensions.dll" set "WEBEXT=%%~D\System.Web.Extensions.dll"
)

echo 正在编译 ...
:: /noconfig 跳过 csc.rsp 的默认引用，避免与显式引用重复
:: 产物直接落在上级目录（项目根），与源码分离
"%CSC%" /nologo /noconfig /target:winexe /platform:anycpu /optimize+ /debug- /langversion:5 ^
    /win32manifest:"app.manifest" /win32icon:"app.ico" /resource:"app.ico",TodoLite.app.ico ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"%WEBEXT%" ^
    /out:"..\TodoLite.exe" ^
    Program.cs MainForm.cs InboxCompose.cs TaskListView.cs TodoStore.cs

if errorlevel 1 (
    echo.
    echo [失败] 编译未通过，请查看上面的错误信息。
    pause
    exit /b 1
)

if not exist "..\TodoLite.exe" (
    echo.
    echo [失败] 未生成 ..\TodoLite.exe。
    pause
    exit /b 1
)

echo.
echo [成功] 已生成 TodoLite.exe（项目根目录）
endlocal
exit /b 0
