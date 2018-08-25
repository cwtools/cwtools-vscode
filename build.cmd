@echo off
cls

SET BUILD_PACKAGES=BuildPackages

IF NOT EXIST "%BUILD_PACKAGES%\fake.exe" (
  dotnet tool install fake-cli ^
    --tool-path ./%BUILD_PACKAGES% ^
    --version 5.*
)

IF EXIST ".fake"          (RMDIR /Q /S ".fake"         )
IF EXIST "build.fsx.lock" (DEL         "build.fsx.lock")

"%BUILD_PACKAGES%/fake.exe" run build.fsx --target %*

REM .paket\paket.exe restore
REM if errorlevel 1 (
REM   exit /b %errorlevel%
REM )

REM packages\build\FAKE\tools\FAKE.exe build.fsx %* --nocache