#!/bin/bash
set -euo pipefail

VERSION=$(grep -oPm1 '(?<=<Version>)[^<]+' HdrProfileSwitcher.csproj)
PUBLISH_DIR="bin/Release/net10.0-windows/win-x64/publish"
RELEASE_DIR="release/HdrProfileSwitcher-${VERSION}"

cd "$(dirname "$0")"

echo "Building v${VERSION}..."
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=false

echo "Packaging..."
rm -rf "release"
mkdir -p "${RELEASE_DIR}"
cp "${PUBLISH_DIR}/HdrProfileSwitcher.exe" "${RELEASE_DIR}/"
cp README.md "${RELEASE_DIR}/"
cp config.example.json "${RELEASE_DIR}/"

cd release
zip -r "HdrProfileSwitcher-${VERSION}.zip" "HdrProfileSwitcher-${VERSION}/"
echo "Package ready: release/HdrProfileSwitcher-${VERSION}.zip"
