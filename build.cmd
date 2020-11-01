@echo off

:main
setlocal
set __verbose=0
set __need_to_restore=1
set __update_paket=0
set __clear=1

for %%x in (%*) do (
    call :check_params %%x
)
if %__clear% == 1 (cls)
if %__verbose% == 1 (@echo on)

if %__need_to_restore% == 1 (
    dotnet tool restore
    if %__update_paket% == 1 (
        dotnet paket update
    )
    dotnet paket restore
)

if errorlevel 1 (
  exit /b %errorlevel%
)


dotnet fake run build.fsx --target %__other_parametars%
@echo off
endlocal
exit /B

:check_params
    if "%~1%" == "-v" ( 
        set __verbose=1
        goto end
    )
    if "%~1%" == "-no-restore" ( 
        set __need_to_restore=0
        goto end
    )
    if "%~1%" == "-paket-update" (
        set __update_paket=1
        goto end
    )
    if "%~1%" == "-no-clear" (
        set __clear=0
        goto end
    )

    set "__other_parametars=%__other_parametars% %1"
    
    :end
        exit /B
