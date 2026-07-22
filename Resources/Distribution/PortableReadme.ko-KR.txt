Nexus for ComfyUI
Windows 포터블 릴리즈

빠른 시작

1. ZIP 전체를 쓰기 가능한 로컬 폴더 하나에 압축 해제합니다.
2. 권장 위치: D:\Nexus 또는 C:\AI\Nexus.
3. 이 폴더의 ComfyUI-Nexus.exe를 실행합니다.
4. 안내에 따라 기존 ComfyUI 작업 환경을 연결하거나 Nexus 관리형 환경을 준비합니다.

폴더를 함께 유지하세요

압축 해제 후 App 또는 LocalRuntime\Packages의 이름을 바꾸거나 이동하거나 삭제하지 마세요.
루트의 ComfyUI-Nexus.exe는 launcher이고, App에는 데스크톱 앱이 있으며,
LocalRuntime\Packages에는 최초 설정에 사용하는 검증된 설치 패키지가 들어 있습니다.

Program Files, 너무 깊은 폴더, OneDrive 같은 동기화 폴더는 피하세요.
이 위치에서는 Python 패키지나 custom node 설치 중 Windows 권한 또는 긴 경로 문제가 생길 수 있습니다.

최소 사양

- Windows 10 또는 Windows 11, 64-bit
- VRAM 8 GB 이상의 NVIDIA CUDA GPU
- 시스템 메모리 16 GB
- 사용 가능 저장 공간 30 GB 이상 및 모델/생성 결과물 저장 공간
- Microsoft Edge WebView2 Runtime

SDXL workflow 권장 사양

- NVIDIA GeForce RTX 3060 12 GB 또는 동급 이상
- 시스템 메모리 32 GB
- 모델, workflow, 생성 결과물을 위한 충분한 SSD 공간

런타임 데이터

Nexus는 portable settings, installed runtime, logs, cache, backups, state를
이 launcher 옆에 저장합니다. 설치 위치를 옮길 때는 Nexus를 종료한 뒤 폴더 전체를 이동하세요.
포터블 설치를 제거할 때는 Nexus를 종료하고, 보관할 models, workflows, inputs, outputs,
backups를 백업한 뒤 폴더 전체를 삭제하세요.

Nexus for ComfyUI는 ComfyUI를 PC에서 로컬로 실행합니다.
현재 관리형 runtime은 NVIDIA CUDA GPU를 지원합니다.
필요한 사양은 model, custom node, image resolution, workflow 복잡도에 따라 달라질 수 있습니다.

지원 및 소스

https://github.com/wenovaX/ComfyUI-Nexus
