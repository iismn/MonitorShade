# macOS 1.18.3 validation

2026-10-23 (Asia/Seoul).

Built on the GitHub-hosted macOS 26 runner with Xcode 26.6. The packaged app also ran on macOS 27.0.1 using the `xcode-27` runner:
[run 39906295080](https://github.com/iismn/MonitorShade/actions/runs/39906295080).

The app source for this package is commit `6a0992491416dd55fd6c20068b015d9b4e33f54a`. Later README and screenshot commits do not change the executable sources.

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
| Sidebar and detail share one backdrop | Morning/evening RGB difference ≤ 0.0001 on macOS 26 and 27 |
| Sidebar follows the time palette | Passed on macOS 26 and 27 |
| Sync bar and monitor cards avoid separate dark opaque panels | Passed in normal-transparency fixtures on macOS 26 and 27 |
| ZIP contains executable mode and both architectures | Passed |
| ZIP excludes personal profiles and diagnostics | Passed |

The UI tests use preview monitors. The hosted runners initially had Reduce Transparency enabled; CI explicitly disables it and Increase Contrast for normal-appearance captures. The app still respects these settings on the user's Mac. The initial opaque captures and the corrected transparent captures were inspected; `environment.txt` records the active state. They do not modify hardware, register global shortcuts or load user display profiles. Screenshots are of the test app's own window on the CI runner.

The README images are the app's native macOS 27.0.1 window captures, with preview display data. The sidebar has no separate dark panel. Its empty background matches the detail background. The dot readout retains its 20% smaller frame with brighter dots and glow. Monitor cards use transparent tint layers over the live window backdrop. The sync bar and percentage control use native clear Liquid Glass. Morning and evening brightness cards were visually checked for aligned edges, readable controls and the dot readout.

The inherited `Hardware.m` bridge is unchanged from the supplied Mac app. Its source SHA-256 is `16aacdc87500c2e1621723a6384606ae8c578a078f0d4467757332e1458ab25f`.

Package SHA-256:

```text
54bbbad9635b9c52de7dc914e7f35fbfbf8361370ce49def9a04db7fab3384b5  MonitorShade-macOS.zip
```

Physical monitor DDC/CI writes, cable reconnection, rotation, login startup, permission approval, screen-content capture and third-party window movement were not exercised on the user's Mac in this run. The supplied app's earlier hardware validation is reference evidence; it is not a substitute for testing this integration on that hardware. The app is not Apple notarized.
