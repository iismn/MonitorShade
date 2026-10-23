# macOS 1.18.1 validation

2026-10-23 (Asia/Seoul).

Built on the GitHub-hosted macOS 26 runner with Xcode 26.6. The packaged app also ran on macOS 27.0.1 using the `xcode-27` runner:
[run 39903626836](https://github.com/iismn/MonitorShade/actions/runs/39903626836).

The app source for this package is commit `bbbf60494c2779445c86b4e0f2b28bba319d0276`. Later README and screenshot commits do not change the executable sources.

| Check | Result |
| --- | --- |
| arm64 and x86_64 compilation | Passed |
| Universal Mach-O slices (`lipo -verify_arch`) | Passed |
| Ad-hoc signature (`codesign --verify --deep --strict`) | Passed |
| Bundle plist and macOS 14 deployment target | Passed |
| Existing profile migration, identity matching, main-display, brightness, DDC parsing and independent idle-timer suite | Passed |
| New geometry, shortcuts, content comparison, profile-target reconnection and time-palette checks | 133 passed |
| Native settings-window renders | 7 pages × morning/evening × macOS 26/27; 28 passed |
| Sidebar visibility in composited window captures | Passed |
| ZIP contains executable mode and both architectures | Passed |
| ZIP excludes personal profiles and diagnostics | Passed |

The UI tests use preview monitors. They do not modify hardware, register global shortcuts or load user display profiles. Screenshots are of the test app's own window on the CI runner.

The README images are the app's native macOS 27.0.1 window captures, with preview display data. The duplicate sidebar heading is absent. Morning and evening brightness cards were visually checked for aligned edges, readable controls and the dot readout.

The inherited `Hardware.m` bridge is unchanged from the supplied Mac app. Its source SHA-256 is `16aacdc87500c2e1621723a6384606ae8c578a078f0d4467757332e1458ab25f`.

Package SHA-256:

```text
f6da4ecf6447d680f05d333233dfb1e56d0503b49ca82cbae77cd8acdb9a3bd8  MonitorShade-macOS.zip
```

Physical monitor DDC/CI writes, cable reconnection, rotation, login startup, permission approval, screen-content capture and third-party window movement were not exercised on the user's Mac in this run. The supplied app's earlier hardware validation is reference evidence; it is not a substitute for testing this integration on that hardware. The app is not Apple notarized.
