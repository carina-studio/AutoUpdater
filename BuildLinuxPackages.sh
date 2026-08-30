#!/bin/bash

APP_NAME="AutoUpdater.Avalonia"
FRAMEWORK="net10.0"
RID_LIST=("linux-x64" "linux-arm64")
CONFIG="Release"
SELF_CONTAINED="true"
TRIM_ASSEMBLIES="true"
READY_TO_RUN="false"

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

    echo " "
    echo "[$RID]"
    echo " "

    # clean
    rm -r ./$APP_NAME/bin/$CONFIG/$FRAMEWORK/$RID
    dotnet restore $APP_NAME -r $RID
    if [ "$?" != "0" ]; then
        exit
    fi
    dotnet clean $APP_NAME -c $CONFIG -r $RID
    if [ "$?" != "0" ]; then
        exit
    fi

    # build
    dotnet publish $APP_NAME -c $CONFIG -r $RID --self-contained $SELF_CONTAINED -p:PublishTrimmed=$TRIM_ASSEMBLIES -p:PublishReadyToRun=$READY_TO_RUN
    if [ "$?" != "0" ]; then
        exit
    fi

    # zip package
    ditto -c -k --sequesterRsrc "./$APP_NAME/bin/$CONFIG/$FRAMEWORK/$RID/publish/" "./Packages/$VERSION/$APP_NAME-$VERSION-$RID.zip"
    if [ "$?" != "0" ]; then
        exit
    fi

done

# Generate package manifest
# dotnet run PackagingTool.cs -- create-package-manifest linux $APP_NAME $VERSION
