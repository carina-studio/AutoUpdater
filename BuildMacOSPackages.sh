APP_NAME="AutoUpdater.Avalonia"
FRAMEWORK="net10.0"
RID_LIST=("osx-x64" "osx-arm64")
PUB_PLATFORM_LIST=("osx-x64" "osx-arm64")
CONFIG="Release"
MACOS_SDK_VERSION="26.0" # Linked SDK version to write into the application binary, opts-in to the window design of macOS 26+
CERT_NAME="" # Name of certification to sign the application

echo "********** Start building $APP_NAME **********"

# Get application version
VERSION=$(dotnet run PackagingTool.cs -- get-current-version $APP_NAME/$APP_NAME.csproj)
if [ "$?" != "0" ]; then
    echo "Unable to get version of $APP_NAME"
    exit
fi
echo "Version: $VERSION"

# Create output directory
if [[ ! -d "./Packages/$VERSION" ]]; then
    echo "Create directory 'Packages/$VERSION'"
    if [[ ! -d "./Packages" ]]; then
        mkdir ./Packages
    fi
    mkdir ./Packages/$VERSION
    if [ "$?" != "0" ]; then
        exit
    fi
fi

# Build packages
for i in "${!RID_LIST[@]}"; do
    RID=${RID_LIST[$i]}
    PUB_PLATFORM=${PUB_PLATFORM_LIST[$i]}

    echo " " 
    echo "[$PUB_PLATFORM ($RID)]"
    echo " "

    # clean
    rm -r ./$APP_NAME/bin/$CONFIG/$FRAMEWORK/$RID
    dotnet clean $APP_NAME
    dotnet restore $APP_NAME
    if [ "$?" != "0" ]; then
        exit
    fi
    
    # build
    dotnet publish $APP_NAME -c $CONFIG -p:SelfContained=true -p:PublishSingleFile=false -p:RuntimeIdentifier=$RID -p:PublishAot=true
    dotnet msbuild $APP_NAME -t:BundleApp -property:Configuration=$CONFIG -p:SelfContained=true -p:PublishSingleFile=false -p:RuntimeIdentifier=$RID -p:PublishAot=true
    if [ "$?" != "0" ]; then
        exit
    fi

    # create output directory
    if [[ -d "./Packages/$VERSION/$PUB_PLATFORM" ]]; then
        rm -r ./Packages/$VERSION/$PUB_PLATFORM
    fi
    echo "Create directory 'Packages/$VERSION/$PUB_PLATFORM'"
    mkdir ./Packages/$VERSION/$PUB_PLATFORM
    if [ "$?" != "0" ]; then
        exit
    fi

    # copy .app directory to output directoty
    mv ./$APP_NAME/bin/$CONFIG/$FRAMEWORK/$RID/publish/$APP_NAME.app ./Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app
    if [ "$?" != "0" ]; then
        exit
    fi

    # copy application icon and remove unnecessary files
    cp ./$APP_NAME/$APP_NAME.icns ./Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app/Contents/Resources/$APP_NAME.icns
    if [ "$?" != "0" ]; then
        exit
    fi
    rm -r ./Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app/Contents/MacOS/$APP_NAME.dSYM # Debug symbols of the Native AOT binary, not needed at runtime

    # [Workaround] Rewrite the linked SDK version of the application binary to opt-in to the window design of macOS 26+.
    # AppKit selects window chrome by the linked SDK version of the main executable, and the .NET apphost is still
    # linked against an old SDK. Must be done before signing, otherwise the signature will be invalidated.
    APP_BINARY="./Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app/Contents/MacOS/$APP_NAME"
    if [ -z "$(command -v vtool)" ]; then
        echo "Unable to find 'vtool', please install Xcode"
        exit
    fi
    MIN_OS_VERSION=$(vtool -show-build-version "$APP_BINARY" | awk '/minos/ { print $2; exit }')
    if [ -z "$MIN_OS_VERSION" ]; then
        echo "Unable to get minimum OS version from '$APP_BINARY'"
        exit
    fi
    echo "Set linked SDK version of '$APP_BINARY' to $MACOS_SDK_VERSION"
    vtool -set-build-version macos "$MIN_OS_VERSION" "$MACOS_SDK_VERSION" -replace -output "$APP_BINARY" "$APP_BINARY"
    if [ "$?" != "0" ]; then
        exit
    fi

    # sign application
    if [ -n "$CERT_NAME" ]; then
        echo "Sign package 'Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app'"
        codesign --deep --force --options=runtime --timestamp --entitlements "./$APP_NAME/$APP_NAME.entitlements" -s "$CERT_NAME" "./Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app"
        if [ "$?" != "0" ]; then
            echo "Failed to sign package 'Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app'"
            rm -f "./Packages/$VERSION/$APP_NAME-$VERSION-$PUB_PLATFORM.zip"
            exit 1
        fi
    else
        echo "Skip signing package 'Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app'"
        codesign --deep --force -s - "./Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app" # Restore ad-hoc signature invalidated by vtool
        if [ "$?" != "0" ]; then
            echo "Failed to sign package 'Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app'"
            exit 1
        fi
    fi

    # zip .app directory
    ditto -c -k --sequesterRsrc --keepParent "./Packages/$VERSION/$PUB_PLATFORM/$APP_NAME.app" "./Packages/$VERSION/$APP_NAME-$VERSION-$PUB_PLATFORM.zip"

done

# Generate package manifest
# dotnet run PackagingTool.cs -- create-package-manifest osx $APP_NAME $VERSION