# MonitorShade for macOS

Native SwiftUI/AppKit app. macOS 14 or later. Apple silicon and Intel executable.

## Build

Install Xcode 26 or later, or its command-line tools and macOS 26 SDK. From the repository root:

```sh
zsh macos/build.command
```

The script builds arm64 and x86_64, combines them with `lipo`, creates `dist/Monitor Shade.app`, applies an ad-hoc signature and runs the hardware-free self-tests. It does not install packages or change display settings. Set `ARCHS=arm64` to build only Apple silicon.

```sh
ARCHS=arm64 zsh macos/build.command
"dist/Monitor Shade.app/Contents/MacOS/MonitorShade" --self-test
"dist/Monitor Shade.app/Contents/MacOS/MonitorShade" --ui-test "$PWD/dist/ui"
```

The UI check uses two preview monitors and renders the settings pages. It does not register global shortcuts, request screen recording, change brightness or load the user's monitor profiles.

## Saved display setups

`MonitorShade.swift`, `Hardware.h`, `Hardware.m`, the icon and third-party notices originate from the user's working Monitor Shade 1.7 app. Hardware identity matching, DDC/CI transport, built-in brightness, rotation, profile validation, migration and display-configuration rollback are retained.

The bundle identifier remains `haus.grey.DockMonitor`. Profiles remain in:

```text
~/Library/Application Support/DockMonitor/profiles.json
```

The old single `profile.json` migrates to the profile library without deleting the original. Each write keeps `profiles.previous.json`; profile deletion creates a separate JSON backup. New window shortcuts and appearance preferences use the existing app's UserDefaults domain. No user profiles, monitor serial numbers, diagnostics or backups from the supplied `outputs` directory are included in this repository.

A setup stores monitor identities, positions, primary display, logical and pixel resolution, refresh rate, rotation, mirroring, brightness and protection settings. A reconnect restores only a fully matched display set. Resolution and refresh rate must exist in the current connection's advertised modes. Failures restore the previous arrangement when possible.

## Screen protection

Existing profiles retain input-idle mode. Mouse activity affects its display; keyboard activity affects the last clicked display. Screen-content mode is optional and requires Screen Recording permission. ScreenCaptureKit samples at up to two frames per second and compares a reduced grayscale frame in memory. This is change detection, not an exact pixel comparison.

With wallpaper exclusion, the capture filter includes normal application windows and excludes MonitorShade, desktop wallpaper and empty background areas around smaller windows. Without exclusion, the full display is sampled while MonitorShade's own windows are excluded. Protected content and unusual window layers may not appear in the capture.

Capture errors stop automatic blanking for the affected display. Sleep, session changes and display reconfiguration clear overlays and restart idle intervals. Settings pause automatic blanking while visible. Built-in and mirrored screens are excluded from OLED protection. Manual blanking wakes on mouse entry, with an exit/re-entry required when the pointer was already inside that display.

## Window control

Carbon registers only the configured key combinations. Window movement uses Accessibility APIs for the frontmost app's focused window. It requires Accessibility permission. Window frames use the display's visible work area, excluding the Dock and menu bar. A repeated shortcut cycles only if the same window and previous placement remain unchanged. Full-screen Spaces and windows that reject resizing are reported without forcing a display-mode change.

## Hardware support

The inherited external DDC transport uses Apple silicon's IOAVService/DCP path. An Intel build includes the UI, profiles and window tools; external hardware brightness may be unavailable there. Some adapters, DisplayLink connections and monitor/HDR modes also block DDC/CI. Rotation, display metadata and built-in brightness use dynamically loaded macOS APIs inherited from the supplied app; macOS updates can affect them.

The app does not need Edge, WebView2, .NET or a browser runtime. Its panels use NSVisualEffectView for live blur. The distributed app is ad-hoc signed and is not Apple notarized.

## Source map

| File | Purpose |
| --- | --- |
| `MonitorShade.swift` | Monitor identity, saved setups, restoration, brightness and menu-bar lifecycle |
| `Hardware.m` | DDC/CI, built-in brightness and rotation bridge |
| `OLEDProtection.swift` | Per-display idle timers and black panels |
| `ContentChangeMonitor.swift` | Optional ScreenCaptureKit change detection and wallpaper exclusion |
| `MacIntegration.swift` | Global shortcuts, window geometry, Accessibility and preferences |
| `MacConsole.swift` | Sidebar, brightness cards, display setups, settings and time palettes |
| `MacVerification.swift` | Hardware-free regression checks and preview rendering |

`.github/workflows/macos.yml` builds on a Mac runner, checks the universal binary and signature, runs self-tests, renders the UI and packages a ZIP. Hardware-specific operations require validation on the user's Mac and monitors.

## Interface

The sidebar and detail view share one full-window desktop backdrop and time palette. Navigation has no separate opaque panel. Search, hover states, keyboard navigation and the sidebar visibility control are retained. Brightness sync and the linked brightness controls use native clear Liquid Glass on macOS 26 or later. Content cards use translucent tint layers over the shared native desktop blur, with a soft highlight along their edges. They do not add a separate dark material. Reduce Transparency uses an opaque system surface for readability. The linked card blends green daytime colors into orange, pink and pale gray in the evening. Its dot readout is 20% smaller than 1.18.1, with larger white dots and two soft glow layers. The native window backdrop stays live when the window moves.

The design follows Apple's [Materials guidance](https://developer.apple.com/design/human-interface-guidelines/materials) and [macOS 27 design updates](https://developer.apple.com/videos/play/wwdc2026/289/), with the soft gradients and dot readout from the supplied [visual reference](https://www.instagram.com/p/DeKDxK1MhzI/). System Reduce Transparency and Increase Contrast settings affect the materials. Page transitions honor Reduce Motion.
