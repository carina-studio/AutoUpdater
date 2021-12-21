@echo off

IF [%1] == [] (
	echo No version specified
	exit
)

echo ***** Start building packages %1 *****

IF not exist Packages (
	mkdir Packages
)

REM ********** Windows **********

echo ***** Windows (x86) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=win-x86
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\win-x86\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-win-x86.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-win-x86.zip > .\Packages\AutoUpdater.Avalonia-%1-win-x86.txt

echo .
echo ***** Windows (x64) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=win-x64
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\win-x64\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-win-x64.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-win-x64.zip > .\Packages\AutoUpdater.Avalonia-%1-win-x64.txt

echo .
echo ***** Windows (arm64) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=win-arm64
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\win-arm64\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-win-arm64.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-win-arm64.zip > .\Packages\AutoUpdater.Avalonia-%1-win-arm64.txt

echo .
echo ***** Windows (x86, Framework-Dependent) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=win-x86-fx-dependent
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\win-x86-fx-dependent\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-win-x86-fx-dependent.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-win-x86-fx-dependent.zip > .\Packages\AutoUpdater.Avalonia-%1-win-x86-fx-dependent.txt

echo .
echo ***** Windows (x64, Framework-Dependent) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=win-x64-fx-dependent
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\win-x64-fx-dependent\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-win-x64-fx-dependent.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-win-x64-fx-dependent.zip > .\Packages\AutoUpdater.Avalonia-%1-win-x64-fx-dependent.txt

echo .
echo ***** Windows (arm64, Framework-Dependent) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=win-arm64-fx-dependent
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\win-arm64-fx-dependent\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-win-arm64-fx-dependent.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-win-arm64-fx-dependent.zip > .\Packages\AutoUpdater.Avalonia-%1-win-arm64-fx-dependent.txt


REM ********** Linux **********

echo .
echo ***** Linux (x64) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=linux-x64
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\linux-x64\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-linux-x64.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-linux-x64.zip > .\Packages\AutoUpdater.Avalonia-%1-linux-x64.txt

echo .
echo ***** Linux (arm64) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=linux-arm64
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\linux-arm64\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-linux-arm64.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-linux-arm64.zip > .\Packages\AutoUpdater.Avalonia-%1-linux-arm64.txt

echo .
echo ***** Linux (x64, Framework-Dependent) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=linux-x64-fx-dependent
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\linux-x64-fx-dependent\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-linux-x64-fx-dependent.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-linux-x64-fx-dependent.zip > .\Packages\AutoUpdater.Avalonia-%1-linux-x64-fx-dependent.txt

echo .
echo ***** Linux (arm64, Framework-Dependent) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=linux-arm64-fx-dependent
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net6.0\publish\linux-arm64-fx-dependent\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-linux-arm64-fx-dependent.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-linux-arm64-fx-dependent.zip > .\Packages\AutoUpdater.Avalonia-%1-linux-arm64-fx-dependent.txt
