Nexus for ComfyUI
Windows 便携版

快速开始

1. 将整个 ZIP 解压到一个可写入的本地文件夹。
2. 建议位置：D:\Nexus 或 C:\AI\Nexus。
3. 在此文件夹中运行 ComfyUI-Nexus.exe。
4. 按照引导连接现有的 ComfyUI 工作区，或创建由 Nexus 管理的工作区。

请保持此文件夹完整

解压后，请勿移动、重命名或删除 App 或 LocalRuntime\Packages。
根目录中的 ComfyUI-Nexus.exe 是启动器。App 包含桌面应用，
LocalRuntime\Packages 包含首次设置所需的已验证安装包。

请避免安装到 Program Files、层级过深的文件夹或 OneDrive 等云同步文件夹。
在准备 Python 软件包和 custom node 时，这些位置可能导致 Windows 权限或长路径问题。

最低要求

- Windows 10 或 Windows 11，64 位
- NVIDIA CUDA GPU，8 GB VRAM
- 16 GB 系统内存
- 30 GB 可用存储空间，另加模型和生成资源所需空间
- Microsoft Edge WebView2 Runtime

SDXL workflow 建议配置

- NVIDIA GeForce RTX 3060 12 GB 或同等级显卡
- 32 GB 系统内存
- 有足够空间存放模型、workflow 和生成资源的 SSD

运行时数据

Nexus 会将 portable settings、installed runtime、logs、cache、backups 和 state
保存在此启动器旁。若要移动安装位置，请先关闭 Nexus，再移动整个文件夹。
若要删除便携版安装，请关闭 Nexus，并在备份要保留的 models、workflows、inputs、outputs
或 backups 后删除整个文件夹。

Nexus for ComfyUI 会在您的电脑上本地运行 ComfyUI。
当前受管 runtime 支持 NVIDIA CUDA GPU
硬件要求会随 model、custom node、image resolution和 workflow 复杂度而变化。

支持与源代码

https://github.com/wenovaX/ComfyUI-Nexus
