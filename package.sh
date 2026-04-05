#!/bin/bash
set -e
VERSION="4.1.0"
PUBLISH_DIR="bin/Release/net10.0-windows/win-x64/publish"
RELEASE_DIR="release/HdrProfileSwitcher-${VERSION}"

cd "$(dirname "$0")"

echo "Building v${VERSION}..."
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=false

echo "Packaging..."
rm -rf "release"
mkdir -p "${RELEASE_DIR}"
cp "${PUBLISH_DIR}/HdrProfileSwitcher.exe" "${RELEASE_DIR}/"
cp config.json "${RELEASE_DIR}/"
cp README.md "${RELEASE_DIR}/"

cd release
zip -r "HdrProfileSwitcher-${VERSION}.zip" "HdrProfileSwitcher-${VERSION}/"
echo "Package ready: release/HdrProfileSwitcher-${VERSION}.zip"
