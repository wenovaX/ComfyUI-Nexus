namespace ComfyUI_Nexus.Setup.Models;

using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyUI_Nexus.Settings;

internal sealed record CustomNodeSetting(
	[property: JsonPropertyName("url")] string Url,
	[property: JsonPropertyName("folder")] string Folder);

internal sealed class SetupSettings
{
	[JsonPropertyName("root_path")]
	public string RootPath { get; set; } = string.Empty;

	[JsonPropertyName("last_active_port")]
	public int? LastActivePort { get; set; }

	[JsonPropertyName("last_launch_successful")]
	public bool LastLaunchSuccessful { get; set; }

	[JsonPropertyName("active_server_launch_settings")]
	public ServerLaunchSettingsSnapshot? ActiveServerLaunchSettings { get; set; }

	[JsonPropertyName("language_code")]
	public string LanguageCode { get; set; } = string.Empty;

	[JsonPropertyName("install_mode")]
	public string InstallMode { get; set; } = SetupInstallModes.LocalRuntime;

	[JsonPropertyName("comfy_path")]
	public string ComfyPath { get; set; } = string.Empty;

	[JsonPropertyName("gpu_id")]
	public string GpuId { get; set; } = "0";

	[JsonPropertyName("comfy_repo_url")]
	public string ComfyRepoUrl { get; set; } = "https://github.com/comfyanonymous/ComfyUI.git";

	[JsonPropertyName("manager_repo_url")]
	public string ManagerRepoUrl { get; set; } = "https://github.com/ltdrdata/ComfyUI-Manager.git";

	[JsonPropertyName("hud_repo_url")]
	public string HudRepoUrl { get; set; } = "https://github.com/wenovaX/ComfyUI-HUD.git";

	[JsonPropertyName("default_model_url")]
	public string DefaultModelUrl { get; set; } = "https://huggingface.co/Comfy-Org/stable-diffusion-v1-5-archive/resolve/main/v1-5-pruned-emaonly-fp16.safetensors?download=true";

	[JsonPropertyName("pytorch_index_url")]
	public string PyTorchIndexUrl { get; set; } = "https://download.pytorch.org/whl/cu130";

	[JsonPropertyName("git_source")]
	public string GitSource { get; set; } = "builtin"; // "system" or "builtin"

	[JsonPropertyName("git_path")]
	public string GitPath { get; set; } = string.Empty;

	[JsonPropertyName("python_source")]
	public string PythonSource { get; set; } = "builtin"; // "system" or "builtin"

	[JsonPropertyName("python_path")]
	public string PythonPath { get; set; } = string.Empty;

	[JsonPropertyName("pip_cache_mode")]
	public string PipCacheMode { get; set; } = PipCacheModes.NexusDefault;

	[JsonPropertyName("pip_cache_path")]
	public string PipCachePath { get; set; } = string.Empty;

	[JsonPropertyName("server_python_mode")]
	public string ServerPythonMode { get; set; } = PythonExecutionModes.Venv;

	[JsonPropertyName("pending_venv_delete")]
	public bool PendingVenvDelete { get; set; }

	[JsonPropertyName("pending_runtime_purge")]
	public bool PendingRuntimePurge { get; set; }

	[JsonPropertyName("runtime_purge_in_progress")]
	public bool RuntimePurgeInProgress { get; set; }

	[JsonPropertyName("pending_boot_tasks")]
	public List<PendingBootTask> PendingBootTasks { get; set; } = new();

	[JsonPropertyName("portable_only")]
	public bool PortableOnly { get; set; } = true;

	[JsonPropertyName("listen_address")]
	public string ListenAddress { get; set; } = "127.0.0.1";

	[JsonPropertyName("server_port")]
	public int ServerPort { get; set; } = 8188;

	[JsonPropertyName("server_startup_timeout_seconds")]
	public int ServerStartupTimeoutSeconds { get; set; } = 60;

	[JsonPropertyName("port_probe_interval_ms")]
	public int PortProbeIntervalMilliseconds { get; set; } = 500;

	[JsonPropertyName("server_log_file")]
	public string ServerLogFile { get; set; } = "Logs/comfy-server-latest.log";

	[JsonPropertyName("server_log_tail_interval_ms")]
	public int ServerLogTailIntervalMilliseconds { get; set; } = 250;

	[JsonPropertyName("purge_retry_count")]
	public int PurgeRetryCount { get; set; } = 3;

	[JsonPropertyName("purge_retry_delay_ms")]
	public int PurgeRetryDelayMilliseconds { get; set; } = 500;

	[JsonPropertyName("download_buffer_size")]
	public int DownloadBufferSize { get; set; } = 8192;

	[JsonPropertyName("default_model_file_name")]
	public string DefaultModelFileName { get; set; } = "v1-5-pruned-emaonly-fp16.safetensors";

	[JsonPropertyName("model_library_roots")]
	public List<string> ModelLibraryRoots { get; set; } = new();

	[JsonPropertyName("runtime_backup_path")]
	public string RuntimeBackupPath { get; set; } = string.Empty;

	[JsonPropertyName("runtime_backup_format")]
	public string RuntimeBackupFormat { get; set; } = RuntimeBackupFormats.Folder;

	[JsonPropertyName("primary_model_root")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? LegacyPrimaryModelRoot { get; set; }

	[JsonPropertyName("additional_model_roots")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? LegacyAdditionalModelRoots { get; set; }

	[JsonPropertyName("torch_packages")]
	public string TorchPackages { get; set; } = "torch torchvision torchaudio";

	[JsonPropertyName("essential_nodes")]
	public List<CustomNodeSetting> EssentialNodes { get; set; } = new()
	{
		new("https://github.com/cubiq/ComfyUI_IPAdapter_plus.git", "ComfyUI_IPAdapter_plus"),
		new("https://github.com/Fannovel16/comfyui_controlnet_aux.git", "comfyui_controlnet_aux"),
		new("https://github.com/Kosinkadink/ComfyUI-AnimateDiff-Evolved.git", "ComfyUI-AnimateDiff-Evolved"),
		new("https://github.com/ltdrdata/ComfyUI-Impact-Pack.git", "ComfyUI-Impact-Pack"),
		new("https://github.com/AlekPet/ComfyUI_Custom_Nodes_AlekPet.git", "ComfyUI_Custom_Nodes_AlekPet"),
		new("https://github.com/cubiq/ComfyUI_FaceAnalysis.git", "ComfyUI_FaceAnalysis"),
		new("https://github.com/ltdrdata/ComfyUI-Impact-Subpack.git", "ComfyUI-Impact-Subpack"),
		new("https://github.com/nkchocoai/ComfyUI-PromptUtilities.git", "ComfyUI-PromptUtilities"),
		new("https://github.com/wTechArtist/ComfyUI-CustomNodes.git", "ComfyUI-CustomNodes")
	};

	public static SetupSettings Load(string path)
	{
		if (!File.Exists(path)) return new SetupSettings();
		try
		{
			string json = File.ReadAllText(path);
			return JsonSerializer.Deserialize<SetupSettings>(json) ?? new SetupSettings();
		}
		catch { return new SetupSettings(); }
	}

	public void Save(string path)
	{
		_ = TrySave(path);
	}

	public bool TrySave(string path)
	{
		try
		{
			string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(path, json);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
