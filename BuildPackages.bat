@echo off

IF [%1] == [] (
	echo No version specified
	exit
)

echo ***** Start building packages %1 *****

IF not exist Packages (
	mkdir Packages
)

echo ***** Windows (x64) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=win-x64
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net5.0\publish\win-x64\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-win-x64.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-win-x64.zip > .\Packages\AutoUpdater.Avalonia-%1-win-x64.txt

echo ***** Linux (x64) *****
dotnet publish AutoUpdater.Avalonia -c Release -p:PublishProfile=linux-x64
IF %ERRORLEVEL% NEQ 0 ( 
   exit
)

PowerShell -NoLogo -Command Compress-Archive -Force -Path .\AutoUpdater.Avalonia\bin\Release\net5.0\publish\linux-x64\* -DestinationPath .\Packages\AutoUpdater.Avalonia-%1-linux-x64.zip
PowerShell -NoLogo -Command Get-FileHash .\Packages\AutoUpdater.Avalonia-%1-linux-x64.zip > .\Packages\AutoUpdater.Avalonia-%1-linux-x64.txt