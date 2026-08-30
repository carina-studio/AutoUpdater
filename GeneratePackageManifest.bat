@echo off

set APP_NAME=AutoUpdater.Avalonia
set ERRORLEVEL=0

echo ********** Start generating package manifest of %APP_NAME% **********

REM Get current version
dotnet run PackagingTool.cs -- get-current-version %APP_NAME%\%APP_NAME%.csproj > Packages\Packaging.txt
if %ERRORLEVEL% neq 0 ( 
    del /Q Packages\Packaging.txt
    exit
)
set /p CURRENT_VERSION=<Packages\Packaging.txt
dotnet run PackagingTool.cs -- get-current-informational-version %APP_NAME%\%APP_NAME%.csproj > Packages\Packaging.txt
if %ERRORLEVEL% neq 0 ( 
    del /Q Packages\Packaging.txt
    exit
)
set /p CURRENT_INFORMATIONAL_VERSION=<Packages\Packaging.txt
echo Version: %CURRENT_VERSION% (%CURRENT_INFORMATIONAL_VERSION%)

REM Generate package manifest
dotnet run PackagingTool.cs -- create-package-manifest %APP_NAME% %CURRENT_VERSION% %CURRENT_INFORMATIONAL_VERSION%

REM Complete
del /Q Packages\Packaging.txt
