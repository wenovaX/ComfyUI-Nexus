# 변경 이력

언어: [English](CHANGELOG.md) | [한국어](CHANGELOG.ko.md)

사용자에게 보이는 주요 변경 사항을 기록합니다.

## [1.0.0.2] - 2026-07-21

### 안정성

- Windows alpha-mask 실패와 불필요한 compositor 부하를 피하기 위해 interactive shell의 MAUI native shadow 사용을 제거했습니다.
- server start, stop, restart, recovery, exit를 server lifecycle coordinator로 통합했습니다.
- owner별 operation lane과 lifecycle generation 검증을 추가해 늦게 도착한 UI, watcher, media, GPU, bridge 결과를 안전하게 버립니다.
- asset watcher를 현재 열려 있는 rail tool과 root에만 연결했습니다. 닫히거나 숨겨진 도구는 더 이상 UI scan이나 patch를 수행하지 않습니다.

### Loading 및 시각 연출

- loading을 실제 해제 장치로 정리했습니다. server, bridge, shell service, 표시될 UI surface가 모두 준비된 뒤에만 workspace를 보여 줍니다.
- setup, loading, server boot, header, command deck, gauge에 cache 기반 WebP frame playback을 추가했습니다.
- 주요 사용자 흐름의 반복 transform 효과를 lifecycle을 소유하는 visual controller로 교체했습니다.
- server boot에 idle, booting, success, failed visual state와 retry feedback을 추가했습니다.

### 런타임 및 도구

- Python ABI를 인식하는 Dlib wheel과 FaceAnalysis model download를 포함해 managed custom-node dependency plan을 추가했습니다.
- local server probe를 listener-first HTTP readiness 검사로 통일했습니다.
- server-only diagnostic 및 recovery action을 위한 독립 Control Deck을 추가했습니다.
- portable release packaging, 선택적 local signing, archive output, 대화형 version update를 개선했습니다.

### 진단

- 집중적인 문제 분석을 위한 lifecycle, concurrency, browser host, motion, crash capture snapshot을 추가했습니다.
- 정상 animation cache, frame gauge, startup prewarm 메시지는 trace level로 낮췄습니다.

### 현재 제한 사항

- Nexus는 현재 Windows 전용이며 managed runtime 경로는 NVIDIA CUDA GPU를 기준으로 지원합니다.
- workflow engine은 WebView2 안의 ComfyUI가 계속 담당하고, Nexus는 그 주변의 desktop shell과 lifecycle을 담당합니다.
