@echo off
setlocal
cd /d "%~dp0"
::dotnet build ProxyTool.sln --configuration Release --no-incremental --nologo --verbosity normal
dotnet publish ProxyTool.sln --configuration Release --no-self-contained --nologo --verbosity normal
endlocal
pause
exit /b 0
