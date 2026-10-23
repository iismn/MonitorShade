# macOS 1.18.0 validation

2026-10-23 (Asia/Seoul).

Built and checked on a GitHub-hosted macOS 26 runner with Xcode 26.6:
[run 39901966820](https://github.com/iismn/MonitorShade/actions/runs/39901966820).

The app source for this package is commit `aa9af4c54e02a1f0c14215ab1108f5127c39b483`. Later README and screenshot commits do not change the executable sources.

| Check | Result |
| --- | --- |
| arm64 and x86_64 compilation | Passed |
| Universal Mach-O slices (`lipo -verify_arch`) | Passed |
| Ad-hoc signature (`codesign --verify --deep --strict`) | Passed |
| Bundle plist and macOS 14 deployment target | Passed |
| Existing profile migration, identity matching, main-display, brightness, DDC parsing and independent idle-timer suite | Passed |
| New geometry, shortcuts, content comparison, profile-target reconnection and time-palette checks | 133 passed |
| Native settings-window renders | 7 pages × morning/evening; 14 passed |
| Sidebar visibility in composited window captures | Passed |
| ZIP contains executable mode and both architectures | Passed |
| ZIP excludes personal profiles and diagnostics | Passed |

The UI tests use preview monitors. They do not modify hardware, register global shortcuts or load user display profiles. Screenshots are of the test app's own window on the CI runner.

The inherited `Hardware.m` bridge is unchanged from the supplied Mac app. Its source SHA-256 is `16aacdc87500c2e1621723a6384606ae8c578a078f0d4467757332e1458ab25f`.

Package SHA-256:

```text
c4a54ee04336f020ba668c82bba9d0d54b6dd36f7854d51bf11e744f70beb5b4  MonitorShade-macOS.zip
```

Physical monitor DDC/CI writes, cable reconnection, rotation, login startup, permission approval, screen-content capture and third-party window movement were not exercised on the user's Mac in this run. The supplied app's earlier hardware validation is reference evidence; it is not a substitute for testing this integration on that hardware. The app is not Apple notarized.
