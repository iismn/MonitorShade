#!/bin/zsh
set -euo pipefail
cd "${0:A:h}"

app="${1:-../dist/Monitor Shade.app}"
build_dir="$(mktemp -d "${TMPDIR:-/tmp/}MonitorShade-build.XXXXXX")"
trap 'rm -rf -- "$build_dir"' EXIT
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$build_dir/cache"
sdk="$(xcrun --sdk macosx --show-sdk-path)"
architectures=(${=ARCHS:-arm64 x86_64})
objects=()
sources=(MonitorShade.swift OLEDProtection.swift ContentChangeMonitor.swift MacIntegration.swift MacConsole.swift MacVerification.swift)
frameworks=(-framework AppKit -framework SwiftUI -framework IOKit -framework CoreGraphics -framework ServiceManagement
            -framework ApplicationServices -framework Carbon -framework ScreenCaptureKit -framework CoreMedia -framework CoreVideo)
for arch in "${architectures[@]}"; do
    target="$arch-apple-macosx14.0"
    xcrun clang -target "$target" -isysroot "$sdk" -fobjc-arc -O2 -c Hardware.m -o "$build_dir/Hardware-$arch.o"
    xcrun swiftc -swift-version 5 -O -parse-as-library -sdk "$sdk" -target "$target" \
        -module-cache-path "$build_dir/cache" -import-objc-header Hardware.h "${sources[@]}" \
        "$build_dir/Hardware-$arch.o" "${frameworks[@]}" -o "$build_dir/MonitorShade-$arch"
    objects+=("$build_dir/MonitorShade-$arch")
done
if (( ${#objects[@]} == 1 )); then
    cp "${objects[1]}" "$app/Contents/MacOS/MonitorShade"
else
    xcrun lipo -create "${objects[@]}" -output "$app/Contents/MacOS/MonitorShade"
fi
cp Info.plist "$app/Contents/Info.plist"
cp AppIcon.icns ThirdPartyLicenses.txt "$app/Contents/Resources/"
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"
"$app/Contents/MacOS/MonitorShade" --self-test
printf 'Built: %s\n' "$app"
