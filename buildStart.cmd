@echo off
setlocal

pushd "%~dp0" || exit /b 1

set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Package %*
set "buildExitCode=%errorlevel%"
if not "%buildExitCode%"=="0" goto :build_failed

echo.
echo Build completed successfully:
echo   %~dp0dist\PointCursor
echo   %~dp0dist\PointCursor-Windows-x64.zip

popd
endlocal
exit /b 0

:build_failed
echo.
echo Build failed with exit code %buildExitCode%.
popd
endlocal & exit /b %buildExitCode%
