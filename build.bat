@echo off
rem Build StrataLauncher.exe.
rem Needs: Windows (for the .NET Framework 4.8 runtime, which ships with Windows 10/11)
rem and any C# compiler - Visual Studio's Roslyn csc.exe or the in-box .NET Framework one.
setlocal
cd /d "%~dp0"

rem ---- WebView2 SDK (3 files, ~1 MB) is not stored in this repo; fetch it once ----
if not exist Microsoft.Web.WebView2.Core.dll (
  echo Fetching the WebView2 SDK from nuget.org ...
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$v='1.0.4258.31'; $d=Join-Path $env:TEMP ('strata-wv2-'+$v); New-Item -ItemType Directory -Force -Path $d|Out-Null; $z=Join-Path $d 'wv2.zip'; Invoke-WebRequest ('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/'+$v+'/microsoft.web.webview2.'+$v+'.nupkg') -OutFile $z; Add-Type -AssemblyName System.IO.Compression.FileSystem; $a=[System.IO.Compression.ZipFile]::OpenRead($z); $m=[ordered]@{'lib/net462/Microsoft.Web.WebView2.Core.dll'='Microsoft.Web.WebView2.Core.dll';'lib/net462/Microsoft.Web.WebView2.WinForms.dll'='Microsoft.Web.WebView2.WinForms.dll';'runtimes/win-x64/native/WebView2Loader.dll'='WebView2Loader.dll'}; foreach($k in $m.Keys){ [System.IO.Compression.ZipFileExtensions]::ExtractToFile($a.GetEntry($k),(Join-Path $PWD $m[$k]),$true) }; $a.Dispose(); 'ok'"
  if not exist Microsoft.Web.WebView2.Core.dll (echo FETCH FAILED - check your connection & pause & exit /b 1)
)

set CSC="C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
if not exist %CSC% set CSC="C:\Program Files (x86)\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
if not exist %CSC% set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

%CSC% /nologo /target:winexe /platform:x64 ^
  /out:StrataLauncher.exe ^
  /r:Microsoft.Web.WebView2.Core.dll ^
  /r:Microsoft.Web.WebView2.WinForms.dll ^
  /r:System.Web.Extensions.dll ^
  Program.cs
if errorlevel 1 (echo BUILD FAILED & pause & exit /b 1)
echo BUILD OK - StrataLauncher.exe
