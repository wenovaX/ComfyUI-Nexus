Nexus for ComfyUI
Portable Windows Release

Localized guides: README.ko-KR.txt, README.zh-Hans.txt, README.zh-Hant.txt

Quick start

1. Extract the entire ZIP into one writable local folder.
2. Recommended locations: D:\Nexus or C:\AI\Nexus.
3. Run ComfyUI-Nexus.exe from this folder.
4. Follow the guided setup to connect an existing ComfyUI workspace or create a Nexus-managed one.

Keep this folder together

Do not move, rename, or delete App or LocalRuntime\Packages after extraction.
The root ComfyUI-Nexus.exe is the launcher. App contains the desktop application,
and LocalRuntime\Packages contains the verified setup packages used on first run.

Avoid installing under Program Files, a deeply nested folder, or a cloud-synced folder
such as OneDrive. These locations can cause Windows permission or long-path issues
while Python packages and custom nodes are being prepared.

Minimum requirements

- Windows 10 or Windows 11, 64-bit
- NVIDIA CUDA GPU with 8 GB VRAM
- 16 GB system memory
- 30 GB free storage, plus space for models and generated assets
- Microsoft Edge WebView2 Runtime

Recommended for SDXL workflows

- NVIDIA GeForce RTX 3060 12 GB or equivalent
- 32 GB system memory
- SSD storage with room for models, workflows, and generated assets

Runtime data

Nexus keeps its portable settings, installed runtime, logs, cache, backups, and state
beside this launcher. To move the installation, close Nexus and move the entire folder.
To remove a portable installation, close Nexus and delete the entire folder after backing up
any models, workflows, inputs, outputs, or backups you want to keep.

Nexus for ComfyUI runs ComfyUI locally.
The managed runtime currently supports NVIDIA CUDA GPUs.
Hardware requirements vary with model, custom node, image resolution, and workflow complexity.

Support and source

https://github.com/wenovaX/ComfyUI-Nexus
