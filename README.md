<div align="center">

# MonitorShade

### 밝기, 화면 보호, 창 배치를 한곳에서.

모니터 밝기는 함께 맞추고, 멈춘 화면은 검게 가리고.<br>
작업 창은 단축키 한 번으로 원하는 자리에 놓으세요.

<a href="https://github.com/iismn/MonitorShade/releases/download/v1.17/MonitorShade.exe"><img src="docs/images/download-windows.svg" alt="Windows용 MonitorShade 1.17 다운로드" width="260" height="56"></a>

**1.17 · Windows 10 / 11 · x64**<br>
[릴리즈 노트](https://github.com/iismn/MonitorShade/releases/tag/v1.17) · [사용 가이드](docs/user-guide.md) · [문제 보고](https://github.com/iismn/MonitorShade/issues)

</div>

<br>

![MonitorShade 1.17 — 밝기 동기화 카드와 모니터별 밝기 조절 화면](docs/images/brightness-morning.png)

<p align="center"><sub>MonitorShade 1.17 설정 UI · 09:00 아침 색상</sub></p>

<table>
<tr>
<td width="33%" valign="top">
<h3>밝기는 함께</h3>
<p>각 모니터의 밝기를 조절하거나, 선택한 모니터를 같은 값으로 맞춥니다.</p>
</td>
<td width="33%" valign="top">
<h3>화면 보호는 따로</h3>
<p>변화가 없는 화면만 검게 가립니다. 다른 화면에서는 작업을 계속합니다.</p>
</td>
<td width="33%" valign="top">
<h3>창 배치는 빠르게</h3>
<p>방향 배치, 중앙 정렬, 크기 변경을 Windows 키 조합으로 전환합니다.</p>
</td>
</tr>
</table>

<br>

## 모니터 두 대. 밝기는 한 번에.

모니터 버튼을 누르지 않고 **DDC/CI**로 밝기를 조절합니다. 슬라이더를 움직이거나, **오른쪽 Alt + F2 / F3**을 누르세요.

**함께 조절**을 켜면 선택한 모니터에 같은 밝기를 적용합니다. 서로 다른 밝기에서도 한 번에 같은 값으로 맞춰집니다.

| 현재 밝기 | 조작 | 적용 후 |
| :---: | :---: | :---: |
| 40% · 60% | 5% 높이기 | **65% · 65%** |
| 40% · 60% | 5% 낮추기 | **35% · 35%** |

모니터별 조절도 가능합니다. 밝기 단축키와 변경 간격은 **단축키**에서 바꿀 수 있습니다.

<br>

## 멈춘 화면만, 잠시 검게.

![동작 예시 — 왼쪽 모니터는 검은 오버레이로 가리고, 오른쪽 모니터의 작업은 계속합니다](docs/images/screen-protection.svg)

선택한 화면의 내용이 설정한 시간 동안 바뀌지 않으면 검은 오버레이를 띄웁니다. **마우스가 들어오거나 화면 내용이 바뀌면 자동으로 돌아옵니다.**

- **모니터마다 독립 감시** — 여러 모니터를 선택하고 대기 시간을 초 또는 분으로 지정합니다.
- **움직이는 배경 제외** — Lively 배경화면과 앱 창 밖에 보이는 배경 영역을 감시에서 제외할 수 있습니다.
- **연결되면 다시 감시** — 저장한 모니터가 돌아오면 감시를 재개합니다. 직접 일시정지한 상태는 유지합니다.
- **바로 가리기** — 지정한 단축키로 한쪽 모니터에 검은 화면을 띄웁니다. 마우스가 들어오면 해제합니다.

<sub>위 그림은 동작 설명입니다. 검은 오버레이는 모니터의 전원이나 입력 신호를 끄지 않습니다.</sub>

<br>

## 작업 창을 원하는 크기와 위치로.

![방향 배치와 50%·70%·최대화 순환을 설정하는 창 배치 화면](docs/images/window-layout.png)

| 단축키 | 누를 때마다 바뀌는 배치 |
| --- | --- |
| **Win + Backspace** | 중앙 50% → 70% → 최대화 |
| **Win + 방향키** | 해당 방향의 ½ → ⅔ → 최대화 |
| **Win + Delete** | 중앙 16:9 창 → 최대화 |

50%와 70%는 작업 표시줄을 제외한 영역의 **가로·세로 각각**에 적용합니다. 중앙 16:9 창의 기본 너비는 65%이며, 설정에서 변경할 수 있습니다.

<br>

## 아침의 살구색에서, 저녁의 올리브색으로.

![MonitorShade의 20시 저녁 색상 — 올리브색 화면과 초록빛 밝기 카드](docs/images/brightness-evening.png)

PC의 시간에 따라 화면 색상이 바뀝니다. 아침은 흰색과 살구색, 낮은 세이지, 저녁은 올리브, 밤은 슬레이트 계열로 이어집니다. 원하는 시간대의 색상으로 고정할 수도 있습니다.

**창 뒤 배경 블러**, 반투명 카드, 마우스를 올리면 은은하게 밝아지는 슬라이더를 함께 사용합니다. 마우스를 올리는 효과는 모니터의 실제 밝기를 바꾸지 않습니다.

<sub>스크린샷은 실제 설정 UI에 테스트용 모니터 정보를 넣어 촬영했습니다. 이미지에는 고정 배경색을 사용했으며, Windows의 실시간 배경 블러는 포함하지 않았습니다.</sub>

<br>

## 내려받고, 설정하고, 트레이로.

1. **[MonitorShade.exe 다운로드](https://github.com/iismn/MonitorShade/releases/download/v1.17/MonitorShade.exe)** 후 쓰기 가능한 폴더에 저장하세요.
2. EXE를 실행하세요. 기본적으로 창 없이 트레이에서 시작합니다.
3. **Ctrl + Alt + F11** 또는 트레이 아이콘 두 번 클릭으로 설정을 여세요.
4. **화면 보호**에서 모니터와 대기 시간을 선택한 다음 **감시 시작**을 누르세요.

**일반 → 로그인 시 자동 실행**을 켜면 다음 로그인부터 저장된 설정으로 시작합니다. 설정 검색은 **Ctrl + F**로 열 수 있습니다.

### 실행 조건

Windows 10 2004 이상 또는 Windows 11의 **x64 환경**, **.NET Framework 4.8**, **Microsoft Edge WebView2 Runtime**이 필요합니다. WebView2가 없으면 [Microsoft 공식 배포처](https://developer.microsoft.com/microsoft-edge/webview2/)에서 Evergreen Runtime을 설치하세요.

밝기 조절에는 모니터의 DDC/CI 지원이 필요합니다. 창 뒤 배경 블러는 Windows 11 22H2 이상에서 Windows 투명도 효과를 켰을 때 적용됩니다.

<details>
<summary><strong>업데이트와 설정 보관</strong></summary>

1. 기존 프로그램의 트레이 메뉴에서 **종료**를 선택하세요.
2. 기존 `MonitorShade.exe`를 새 파일로 교체하세요.
3. 새 EXE를 실행하세요.

설정은 EXE와 같은 폴더의 `MonitorShade.settings.xml`에 저장됩니다. 이 파일을 유지하면 기존 설정을 사용합니다. EXE 경로를 변경했다면 로그인 시 자동 실행을 다시 설정하세요.

</details>

<details>
<summary><strong>사용 전 알아둘 사항</strong></summary>

- 검은 오버레이는 모니터 전원을 끄지 않습니다. 화면 공유에는 오버레이 뒤의 내용이 보일 수 있습니다.
- 화면 비교는 메모리에서 처리합니다. 감시 중인 화면 내용을 저장하거나 전송하지 않습니다.
- DRM 영상, 독점 전체 화면, 보안 화면에서는 변화 감지가 제한될 수 있습니다.
- DDC/CI 쓰기는 표준 밝기 명령 `0x10`에 한정합니다. HDR이나 일부 모니터 화면 모드에서는 밝기 조절이 제한될 수 있습니다.
- 관리자 권한 앱이나 최소 크기 제한이 있는 창에는 요청한 배치를 적용하지 못할 수 있습니다.
- 배경 블러를 지원하지 않는 환경에서는 단색 배경을 사용합니다.

</details>

<br>

---

<div align="center">

### MonitorShade 1.17

[**Windows용 EXE 다운로드 →**](https://github.com/iismn/MonitorShade/releases/download/v1.17/MonitorShade.exe)

[전체 사용법과 단축키](docs/user-guide.md) · [빌드 및 기술 가이드](docs/technical-guide.md) · [문제 보고 · 기능 제안](https://github.com/iismn/MonitorShade/issues)

[글꼴 라이선스](source/fonts/README.md) · [WebView2 고지](source/licenses)

</div>
