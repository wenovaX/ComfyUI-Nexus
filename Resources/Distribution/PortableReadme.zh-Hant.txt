Nexus for ComfyUI
Windows 可攜版

快速開始

1. 將整個 ZIP 解壓縮到一個可寫入的本機資料夾。
2. 建議位置：D:\Nexus 或 C:\AI\Nexus。
3. 在此資料夾中執行 ComfyUI-Nexus.exe。
4. 依照引導連接現有的 ComfyUI 工作區，或建立由 Nexus 管理的工作區。

請保持此資料夾完整

解壓縮後，請勿移動、重新命名或刪除 App 或 LocalRuntime\Packages。
根目錄中的 ComfyUI-Nexus.exe 是啟動器。App 包含桌面應用程式，
LocalRuntime\Packages 包含首次設定所需的已驗證安裝套件。

請避免安裝到 Program Files、層級過深的資料夾或 OneDrive 等雲端同步資料夾。
在準備 Python 套件和 custom node 時，這些位置可能導致 Windows 權限或長路徑問題。

最低需求

- Windows 10 或 Windows 11，64 位元
- NVIDIA CUDA GPU，8 GB VRAM
- 16 GB 系統記憶體
- 30 GB 可用儲存空間，另加模型和生成資產所需空間
- Microsoft Edge WebView2 Runtime

SDXL workflow 建議規格

- NVIDIA GeForce RTX 3060 12 GB 或同等級顯示卡
- 32 GB 系統記憶體
- 有足夠空間存放模型、workflow 和生成資產的 SSD

執行階段資料

Nexus 會將 portable settings、installed runtime、logs、cache、backups 和 state
保存在此啟動器旁。若要移動安裝位置，請先關閉 Nexus，再移動整個資料夾。
若要移除可攜版安裝，請關閉 Nexus，並在備份要保留的 models、workflows、inputs、outputs
或 backups 後刪除整個資料夾。

Nexus for ComfyUI 會在您的電腦上本機執行 ComfyUI。
目前受管理的 runtime 支援 NVIDIA CUDA GPU
硬體需求會隨 model、custom node、image resolution 和 workflow 複雜度而變化。

支援與原始碼

https://github.com/wenovaX/ComfyUI-Nexus
