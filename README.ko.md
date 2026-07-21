# Nexus for ComfyUI

**작업에만 집중하세요.**<br>
**ComfyUI 주변의 설정, 런타임, 파일 관리는 Nexus가 맡습니다.**

Nexus for ComfyUI는 ComfyUI를 위한 네이티브 데스크톱 보조 도구입니다.<br>
ComfyUI는 WebView2 안에서 workflow engine 역할을 그대로 유지하고,<br>
Nexus는 실행 준비, 서버 제어, workflow 관리, asset 탐색, 복구를 위한 Windows shell을 제공합니다.

<p align="left">
  <img src="docs/images/nexus-model-thumbnails.png" alt="ComfyUI Nexus 모델 썸네일 미리보기" width="100%">
</p>

언어: [English](README.md) | [한국어](README.ko.md)

프로젝트 및 브랜딩 기준: [PROJECTINFO.ko.md](docs/PROJECTINFO.ko.md)

릴리즈 변경 내역: [CHANGELOG.ko.md](docs/CHANGELOG.ko.md)

## 설치

일반 사용자는 GitHub Release에 올라온 ZIP을 내려받아 폴더째 압축을 풀면 됩니다.

1. `D:\Nexus`처럼 쓰기 가능한 폴더를 하나 만듭니다.
2. Release ZIP을 그 폴더 안에 압축 해제합니다.
3. `ComfyUI-Nexus.exe`를 실행하고 화면의 설정 안내를 따라가면 됩니다.

설치 경로는 `D:\Nexus`, `C:\AI\Nexus`처럼 짧게 두는 것을 권장합니다.<br>
너무 깊은 폴더, 동기화되는 클라우드 폴더, 보호된 위치에서는
Python package 설치 중 Windows 경로 길이 또는 권한 문제가 생길 수 있습니다.

별도의 installer는 필요하지 않습니다.<br>
ZIP 안의 파일은 함께 유지해야 합니다.<br>
루트 실행 파일은 작은 launcher이고, 실제 MAUI 앱 파일은 `App` 폴더 안에 둡니다.<br>
루트 옆의 `App`과 `LocalRuntime\Packages`를 사용해 Python, Git, Nexus bridge,
검증된 ComfyUI source fallback을 installer 없이 준비합니다.<br>

설정·설치된 런타임·캐시·상태·백업·로그 파일은 루트 launcher와 같은 위치에 생성됩니다.

```text
D:\Nexus\
  ComfyUI-Nexus.exe
  App\
    ComfyUI-Nexus.exe
  nexus_settings.json
  LocalRuntime\
    Packages\
      runtime-package-spec.json
      ComfyUI\
        ComfyUI-v0.27.0-source.zip
  Backups\
```

Nexus가 관리하는 영구 데이터는 AppData가 아니라 이 portable 폴더 안에 유지됩니다.<br>
쓰기 가능한 위치에서 실행하고, 설치 위치를 옮기거나 백업할 때는 폴더 전체를 함께 이동하세요.

## Nexus가 더하는 기능

### 설정 및 런타임

- 기존 ComfyUI 설치본에 연결하거나, Nexus 관리형 로컬 런타임을 준비합니다.
- 관리형 런타임을 준비할 때 원격 최신 ComfyUI source와 Nexus에 포함된 검증된
  ComfyUI source fallback 중 선택할 수 있습니다.
- Python, Git, ComfyUI, 필수 패키지, virtual environment 상태를 확인합니다.
- 서버 부팅 전에 외부 모델 라이브러리를 ComfyUI의 `extra_model_paths.yaml`과 맞춰 둡니다.
- 현재 PC 상태에 따라 setup, maintenance, server boot, process reattach, direct loading 중 알맞은 시작 경로를 선택합니다.
- 무거운 복구 및 유지보수 작업을 통제된 다음 부팅 과정으로 예약합니다.

### 서버 제어

- GPU, host, port, Python mode, launch option을 설정합니다.
- Nexus가 실행한 ComfyUI 서버를 시작하고, 재시도하고, 복구하거나 다시 연결합니다.
- 재시작 또는 서버 종료 전에는 shell service, server process, local listener 종료를 확인합니다.
- 앱 안에서 pooled live boot log, 설치 진행률, server readiness를 확인합니다.
- 네이티브 command deck에서 queue count, run mode, 실행, 중단, queue 상태를 제어합니다.

### Workflow 및 Asset 도구

- 네이티브 rail에서 workflows, models, input, output 파일을 탐색합니다.
- canvas를 떠나지 않고 모델 및 이미지 썸네일을 바로 미리 볼 수 있습니다.
- root별 안전 정책에 따라 검색, bookmark, rename, move, copy, duplicate, delete, folder 정리를 수행합니다.
- 외부 파일 변경이 발생하면 ComfyUI workflow index를 자동으로 동기화합니다.
- ComfyUI workflows 폴더 밖의 파일을 포함해, 검증된 workflow JSON을 현재 graph에 바로 Insert할 수 있습니다.
- 파일 rename과 이동 과정에서도 workflow tab과 실제 경로를 추적합니다.

### 작업 공간

- canvas를 떠나지 않고 node, model, visual reference를 탐색합니다.
- 지원하는 asset을 Nexus bridge feedback과 함께 ComfyUI로 drag할 수 있습니다.
- 네이티브 viewer에서 image/video 목록 이동, 재생, 확대, 삭제를 처리합니다.
- 관리형 HUD와 독립 Nexus bridge로 네이티브 UI와 ComfyUI web 상태를 연결합니다.

<p align="left">
  <img src="docs/images/nexus-media-assets.png" alt="ComfyUI Nexus 미디어 에셋" width="100%">
</p>

### 데스크톱 경험

- 네이티브 splash, setup, boot, settings, help, dialog, blocker, rail UI.
- 영어 fallback 기반의 영어, 한국어, 중국어 간체, 중국어 번체 shell 리소스.
- 예측 가능한 상태 동기화를 위한 file watcher, 명시적 bridge action, bridge 자동 복구,
  diagnostic log.
- watcher, media, GPU, bridge 작업은 owner별 최신/순차 작업 lane으로 조정해
  stale 결과가 현재 desktop state를 덮어쓰지 않게 합니다.
- loading은 해제 장치입니다. server, bridge, shell service, 실제 UI surface가 준비된 뒤에만
  작업 공간을 보여 줍니다.

## 요구 사항

- Windows 10 이상
- WebView2 Runtime
- NVIDIA CUDA GPU. 현재 Nexus 런타임 설정은 NVIDIA CUDA 그래픽카드만 테스트 및 지원합니다.
- ComfyUI, Python, Git, model, 생성 asset을 저장할 충분한 디스크 공간
- 소스 빌드 시 .NET 10 SDK 및 .NET MAUI workload

## 빌드

빌드는 크게 두 가지 방식이 있습니다.

### Release용 portable 패키지 빌드

GitHub Release에 올릴 형태와 같은 결과물이 필요하면 포함된 빌드 스크립트를 사용합니다.<br>
빌드할 package mode를 명시적으로 선택합니다.

```bat
dev-build-as-binary.bat Release folder archive
```

`folder` mode는 아래와 같은 패키지를 만듭니다.

```text
ComfyUI-Nexus.exe
App/
LocalRuntime/
```

`archive` 또는 `zip`을 넣으면 패키징 후 portable ZIP을 만듭니다.<br>
빠른 로컬 테스트에서 release 폴더만 필요하다면 이 옵션을 생략합니다.

스크립트는 반복 release build 사이의 binary 변동을 줄이기 위해 `bin`, `obj`를 유지합니다.<br>
SDK 산출물까지 완전히 지운 뒤 빌드해야 한다면 아래처럼 실행합니다.

```bat
dev-build-as-binary.bat Release folder clean
```

Windows 로컬 Authenticode 서명은 `--cert`로 사용할 수 있습니다.<br>
인증서는 Windows `CurrentUser\My` 또는 `LocalMachine\My` 저장소에 있어야 합니다.

```bat
dev-build-as-binary.bat Release folder --cert develop
```

빌드 스크립트는 clean, restore, publish 전에 인증서가 실제로 서명 가능한지 먼저 확인합니다.<br>
Release 준비용 clean build는 아래처럼 실행할 수 있습니다.

```bat
dev-build-as-binary.bat Release folder clean archive --cert Release
```

서명되지 않은 Windows desktop build는 보안 설정이 강한 환경에서
Windows Defender, Smart App Control, reputation 기반 검사에 걸릴 수 있습니다.<br>
특히 clean build 직후에 민감하게 반응할 수 있습니다.<br>
Release 준비 중 이런 경고가 보이면 같은 mode로 `clean` 없이 한 번 더 빌드하고,
두 번째 산출물을 배포 대상으로 사용하세요.

```bat
dev-build-as-binary.bat Release folder archive
```

보안 경고를 피하려고 package에 불필요한 파일을 추가하지 마세요.<br>
Release ZIP은 launcher, `App`, `LocalRuntime`만 담는 형태를 유지하는 것이 좋습니다.

이전 compact single-file 앱 형태가 필요하면 아래처럼 실행합니다.

```bat
dev-build-as-binary.bat Release single archive
```

스크립트는 self-contained Windows build를 만들고, release용 파일을 아래 위치에 생성합니다.

```text
build/Release_<timestamp>/
  ComfyUI-Nexus.exe
  App/
    ComfyUI-Nexus.exe
  LocalRuntime/
    Packages/
  ComfyUI-Nexus-v<version>-win-x64-portable-folder-<timestamp>.zip
```

ZIP 파일이 GitHub Release에 올릴 산출물입니다.<br>
ZIP 안에는 루트 launcher, self-contained app 폴더,
`runtime-package-spec.json`, 첫 실행에 필요한 setup package가 들어 있습니다.<br>
[설치](#설치)에 적힌 것처럼 폴더째 압축을 풀고 실행하면 됩니다.

### 버전 변경

릴리즈 전에 아래 대화형 도구로 버전을 올립니다.

```bash
./dev-set-version
```

현재 버전을 보여 주고, 세 자리 또는 네 자리 숫자 버전을 검증한 뒤,
변경 전후와 확인 입력을 거쳐 `Directory.Build.props`와 Windows manifest를 함께 갱신합니다.<br>
Git Bash를 사용하지 않는 경우에는 명령 프롬프트에서 `dev-set-version.bat`를 실행하면 됩니다.

### Visual Studio 또는 직접 .NET 빌드

개발과 디버깅 중에는 Visual Studio에서 직접 빌드하거나 일반 `dotnet build`를 사용할 수 있습니다.

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0
```

이 방식은 `bin`, `obj` 아래에 일반 빌드 산출물을 만듭니다.<br>
반복 개발에는 편하지만, Release에 올리는 정리된 portable ZIP 형태는 아닙니다.

## 현재 상태

setup, startup, server, workflow, asset, media, HUD, bridge의 주요 흐름은 구현되어 있습니다.<br>
현재는 실제 사용 환경에서의 튜닝, 패키징, edge case 정리에 집중하고 있습니다.

## 크래시 진단

Nexus가 예기치 않게 종료되면 먼저 `LocalRuntime/Logs/nexus-latest.log`를 확인하세요.<br>
콘솔의 Nexus 로그 동작은 최신 Nexus 앱 로그를 기본 텍스트 앱으로 열어야 합니다.<br>
시간 정보가 붙은 session log는 `nexus-runtime-<timestamp>-p<pid>.log`로, ComfyUI 서버 로그는 `comfy-server-<timestamp>-*.log`로 보관됩니다.<br>
서버 로그는 Python, custom node, ComfyUI readiness 문제를 볼 때 유용하고,<br>
Nexus runtime 로그는 native UI, setup, restart, recovery 동작의 기준 로그입니다.<br>
최근 session 로그가 유지되므로 AppData에 의존하지 않고 크래시와 재시작 흐름을 비교할 수 있습니다.<br>
`[UI_TRACE]`는 최근의 주요 surface 및 lifecycle 전이를 기록하며, native 종료 뒤에도 이전 session marker에 이 trace가 보존됩니다.<br>
`[CONCURRENCY]` snapshot에는 lifecycle 또는 operation fault를 분석할 때 필요한 owner 작업, worker queue, UI post 상태가 기록됩니다.

Nexus 로그에 managed exception이 남지 않고 앱이 종료되었다면<br>
`C:\Users\<user>\AppData\Local\CrashDumps`의 Windows dump와 Event Viewer의
`Application Error`, `Windows Error Reporting` 항목도 함께 확인하세요.

## 라이선스

ComfyUI-Nexus는 [MIT License](LICENSE)로 배포됩니다.

## 개발

아키텍처, 책임, 런타임 구조, UI 안정성, 빌드 검증은 개발 문서에 정리되어 있습니다:<br>
[English](docs/DEVELOPERS.md) | [한국어](docs/DEVELOPERS.ko.md).
