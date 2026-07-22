namespace ComfyUI_Nexus.Setup.Services;

using System.IO;
using System.Threading.Tasks;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed class ModelResourceInstaller
{
	private const string CoreTag = "[Core]";

	private readonly Action<string> _log;
	private readonly Action<double, string> _progress;
	private readonly SetupSettingsService _settingsService;
	private readonly NexusComfyRuntimePaths _paths;

	internal ModelResourceInstaller(
		Action<string> log,
		Action<double, string> progress,
		SetupSettingsService settingsService,
		NexusComfyRuntimePaths paths)
	{
		_log = log;
		_progress = progress;
		_settingsService = settingsService;
		_paths = paths;
	}

	internal async Task<SetupStepResult> DownloadDefaultModelAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			var settings = _settingsService.Settings;
			string checkpointsDir = Path.Combine(_paths.ActiveModelsRootPath, "checkpoints");
			Directory.CreateDirectory(checkpointsDir);

			string fileName = settings.DefaultModelFileName;
			string targetPath = Path.Combine(checkpointsDir, fileName);

			_log($"{CoreTag} Validating base model: {fileName}");
			ReportStepProgress(0, "Checking integrity...");

			_log($"{CoreTag} Starting base model download: {fileName}");
			ReportStepProgress(0, "Preparing download...");

			await DownloadService.DownloadFileAsync(
				settings.DefaultModelUrl,
				targetPath,
				(p, read, total) =>
				{
					string readStr = FormatBytes(read);
					string totalStr = total.HasValue ? FormatBytes(total.Value) : "Unknown";
					_progress(p, $"Downloading... {p:P0} ({readStr} / {totalStr})");
				},
				Math.Max(1024, settings.DownloadBufferSize),
				cancellationToken);

			_log($"{CoreTag} Base model download completed.");
			return new SetupStepResult(true, "Base model downloaded successfully.", 1);
		}
		catch (Exception ex)
		{
			if (ex is OperationCanceledException)
			{
				_log($"{CoreTag} Base model download cancelled. The partial staging file is retained for a later resume.");
				throw;
			}

			return new SetupStepResult(false, $"Model download failed: {ex.Message}", 0);
		}
	}

	private static string FormatBytes(long bytes)
	{
		string[] sizes = { "B", "KB", "MB", "GB", "TB" };
		int order = 0;
		double len = bytes;
		while (len >= 1024 && order < sizes.Length - 1)
		{
			order++;
			len /= 1024;
		}

		return $"{len:0.##} {sizes[order]}";
	}

	private void ReportStepProgress(double progress, string message)
	{
		_progress(progress, message);
		_log(message);
	}
}
