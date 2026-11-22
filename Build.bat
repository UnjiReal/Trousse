@echo off
setlocal
pushd "%~dp0" || exit /b 1

if not exist "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    echo The 64-bit .NET Framework compiler was not found.
    echo Build Trousse on 64-bit Windows with .NET Framework 4.x installed.
    set "BUILD_EXIT=1"
    goto finish
)

if not exist "bin\" (
    mkdir "bin" || (
        set "BUILD_EXIT=1"
        goto finish
    )
)

rem Windows PowerShell must load its own modules when called from PowerShell 7.
set "PSModulePath="
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0Compila.ps1" -OutputFile "%~dp0bin\Trousse.exe" -PublicRelease
set "BUILD_EXIT=%errorlevel%"
if "%BUILD_EXIT%"=="0" (
    echo.
    echo Built "%~dp0bin\Trousse.exe"
    echo Open that file to start Trousse.
) else (
    echo.
    echo Build failed with exit code %BUILD_EXIT%.
)

:finish
popd
if /i not "%~1"=="--no-pause" pause
exit /b %BUILD_EXIT%
