using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Views;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

internal sealed class HeaderGpuStatusController
{
	private readonly object _stateLock = new();
	private readonly HeaderView _header;
	private readonly NexusMotionController _motion;
	private readonly NexusAnimatedWebpClip _gpuIdleEnergyCoreClip;
	private readonly NexusAnimatedWebpClip _gpuRunningEnergyCoreClip;
	private readonly NexusFrameGauge _gpuGauge;
	private readonly NexusFrameGauge _vramGauge;
	private readonly NexusFrameGauge _cpuGauge;
	private bool _hasGpuUsage;
	private bool _hasCpuUsage;
	private bool? _lastRunningState;

	internal HeaderGpuStatusController(VisualElement owner, HeaderView header)
	{
		ArgumentNullException.ThrowIfNull(owner);
		_header = header ?? throw new ArgumentNullException(nameof(header));
		_motion = new NexusMotionController("header-gpu", "HeaderGpuStatus", owner.Dispatcher);
		_gpuIdleEnergyCoreClip = new NexusAnimatedWebpClip(_motion, header.GpuIdleEnergyCoreImage, "HeaderGpu.IdleEnergyCore", NexusAnimatedWebpCacheCatalog.HeaderGpuIdle);
		_gpuRunningEnergyCoreClip = new NexusAnimatedWebpClip(_motion, header.GpuRunningEnergyCoreImage, "HeaderGpu.RunningEnergyCore", NexusAnimatedWebpCacheCatalog.HeaderGpuRunning);
		_gpuGauge = new NexusFrameGauge(_motion, header.GpuLoadFrameGaugeImage, "HeaderGpu.FrameGauge", NexusAnimatedWebpCacheCatalog.HeaderGpuFrameGauge);
		_vramGauge = new NexusFrameGauge(_motion, header.VramFrameGaugeImage, "HeaderGpu.VramFrameGauge", NexusAnimatedWebpCacheCatalog.HeaderVramFrameGauge);
		_cpuGauge = new NexusFrameGauge(_motion, header.CpuFrameGaugeImage, "HeaderGpu.CpuFrameGauge", NexusAnimatedWebpCacheCatalog.HeaderCpuFrameGauge);
	}

	internal async Task PrepareSurfacesAsync()
	{
		await Task.WhenAll(
			_gpuGauge.PrepareAsync(),
			_vramGauge.PrepareAsync(),
			_cpuGauge.PrepareAsync());
		RefreshGaugeSurfaces();
	}

	internal void UpdateGpuUsage(double loadPercent, double usedVramPercent, double reservedVramPercent)
	{
		double loadClamped = Math.Clamp(loadPercent, 0, 100);
		double usedVramClamped = Math.Clamp(usedVramPercent, 0, 100);
		double reservedVramClamped = Math.Clamp(Math.Max(reservedVramPercent, usedVramClamped), 0, 100);
		bool animate = !XamlLifetimeDiagnostics.AreTransformAnimationsDisabled;

		_hasGpuUsage = true;
		_gpuGauge.SetVisible(true);
		_vramGauge.SetVisible(true);
		_gpuGauge.SetTarget(loadClamped / 100d, animate);
		_vramGauge.SetTarget(reservedVramClamped / 100d, animate);
	}

	internal void UpdateExecutionState(bool isRunning)
	{
		lock (_stateLock)
		{
			if (_lastRunningState == isRunning)
			{
				return;
			}

			_lastRunningState = isRunning;
		}

		_gpuIdleEnergyCoreClip.Stop();
		_gpuRunningEnergyCoreClip.Stop();
		StartEnergyCoreLoop(isRunning);
	}

	internal void RestoreAfterSurfaceAttach()
	{
		bool isRunning;
		lock (_stateLock)
		{
			isRunning = _lastRunningState ?? false;
			_lastRunningState ??= false;
		}

		if (_header.Handler is null)
		{
			return;
		}

		_gpuIdleEnergyCoreClip.Stop();
		_gpuRunningEnergyCoreClip.Stop();
		StartEnergyCoreLoop(isRunning);
	}

	internal void UpdateCpuUsage(double cpuUsage)
	{
		_hasCpuUsage = true;
		_cpuGauge.SetVisible(true);
		_cpuGauge.SetTarget(PercentToScale(cpuUsage), !XamlLifetimeDiagnostics.AreTransformAnimationsDisabled);
	}

	internal void Stop()
	{
		_motion.StopAll();
		_gpuIdleEnergyCoreClip.Stop();
		_gpuRunningEnergyCoreClip.Stop();
		_gpuGauge.Reset();
		_vramGauge.Reset();
		_cpuGauge.Reset();
		_gpuGauge.SetVisible(false);
		_vramGauge.SetVisible(false);
		_cpuGauge.SetVisible(false);

		lock (_stateLock)
		{
			_hasGpuUsage = false;
			_hasCpuUsage = false;
			_lastRunningState = null;
		}

		HideEnergyCores();
	}

	private void RefreshGaugeSurfaces()
	{
		_gpuGauge.RefreshSurfaceAttachment();
		_vramGauge.RefreshSurfaceAttachment();
		_cpuGauge.RefreshSurfaceAttachment();

		if (_hasGpuUsage)
		{
			_gpuGauge.SetVisible(true);
			_vramGauge.SetVisible(true);
		}

		if (_hasCpuUsage)
		{
			_cpuGauge.SetVisible(true);
		}
	}

	private void StartEnergyCoreLoop(bool isRunning)
	{
		if (!XamlLifetimeDiagnostics.AreHeaderGpuIndicatorMotionsEnabled)
		{
			HideEnergyCores();
			return;
		}

		_header.ShowGpuEnergyCore(isRunning);
		if (isRunning)
		{
			_gpuRunningEnergyCoreClip.PlayLoop(() => CanRunEnergyCoreLoop(isRunning: true));
			return;
		}

		_gpuIdleEnergyCoreClip.PlayLoop(() => CanRunEnergyCoreLoop(isRunning: false));
	}

	private void HideEnergyCores()
		=> _header.HideGpuEnergyCores();

	private bool CanRunEnergyCoreLoop(bool isRunning)
	{
		lock (_stateLock)
		{
			return _lastRunningState == isRunning && _header.IsVisible && _header.Handler is not null;
		}
	}

	private static double PercentToScale(double usagePercent)
		=> Math.Clamp(usagePercent, 0, 100) / 100d;
}
