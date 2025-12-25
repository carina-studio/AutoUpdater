APP_NAME="AutoUpdater.Avalonia"

echo "********** Start generating package manifest of $APP_NAME **********"

# Get application version
VERSION=$(dotnet run --project PackagingTool get-current-version $APP_NAME/$APP_NAME.csproj)
if [ "$?" != "0" ]; then
    echo "Unable to get version of $APP_NAME"
    exit
fi
echo "Version: $VERSION"

# Generate package manifest
dotnet run --project PackagingTool create-package-manifest $APP_NAME $VERSION
