@echo off

set APP_NAME=AutoUpdater.Avalonia
set FRAMEWORK=net10.0
set PACKAGING_TOOL_PATH=PackagingTool\bin\Release\%FRAMEWORK%\CarinaStudio.ULogViewer.Packaging.dll
set ERRORLEVEL=0

echo ********** Start generating package manifest of %APP_NAME% **********

REM Build packaging tool
dotnet build PackagingTool -c Release -f %FRAMEWORK%
if %ERRORLEVEL% neq 0 ( 
    exit
)

REM Get current version
dotnet %PACKAGING_TOOL_PATH% get-current-version %APP_NAME%\%APP_NAME%.csproj > Packages\Packaging.txt
if %ERRORLEVEL% neq 0 ( 
    del /Q Packages\Packaging.txt
    exit
)
set /p CURRENT_VERSION=<Packages\Packaging.txt
dotnet %PACKAGING_TOOL_PATH% get-current-informational-version %APP_NAME%\%APP_NAME%.csproj > Packages\Packaging.txt
if %ERRORLEVEL% neq 0 ( 
    del /Q Packages\Packaging.txt
    exit
)
set /p CURRENT_INFORMATIONAL_VERSION=<Packages\Packaging.txt
echo Version: %CURRENT_VERSION% (%CURRENT_INFORMATIONAL_VERSION%)

REM Generate package manifest
dotnet %PACKAGING_TOOL_PATH% create-package-manifest %APP_NAME% %CURRENT_VERSION% %CURRENT_INFORMATIONAL_VERSION%

REM Complete
del /Q Packages\Packaging.txt
