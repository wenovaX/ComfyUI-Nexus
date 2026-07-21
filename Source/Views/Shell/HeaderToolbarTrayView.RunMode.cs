using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Controls.OptionDeck;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views;

public partial class HeaderToolbarTrayView
{
	private bool _isInstantQueueButtonStop;
	private bool _isMainActionPulseRunning;
	private readonly NexusMotionController _mainActionMotion;
	private readonly NexusAnimatedWebpClip _mainActionStopSignalClip;
	private NexusAnimatedWebpCacheLease? _mainActionStopSignalCacheLease;
	private Task<NexusAnimatedWebpCacheLease>? _mainActionStopSignalCacheAcquireTask;
	private string _currentRunMode = RunModeOptions.Default;
	private static readonly RunModeOptionDeckItem[] RunModeOptionItems = RunModeOptions.All
		.Select(mode => new RunModeOptionDeckItem(
			NormalizeRunMode(mode),
			mode,
			GetRunModeIcon(mode),
			GetRunModeTextColor(mode)))
		.ToArray();

	internal void DismissRunModeOptionsFromGlobalPointerRelease()
	{
		RunModeOptionDeck.DismissFromGlobalPointerRelease();
	}

	private void SetRunModeOptionsExpanded(bool isExpanded, bool animate)
	{
		RunModeOptionDeck.SetExpanded(isExpanded, animate);
	}

	private void ApplyRunMode(string mode)
	{
		_currentRunMode = NormalizeRunMode(mode);
		RunModeOptionDeck.SetOptions(RunModeOptionItems, _currentRunMode);
		UpdateMainActionVisualState();
	}

	private void OnRunModeOptionDeckSelectionChanged(string mode)
	{
		ApplyRunMode(mode);
		if (RunModeCommand?.CanExecute(_currentRunMode) != false)
		{
			RunModeCommand?.Execute(_currentRunMode);
		}
	}

	private void UpdateMainActionVisualState()
	{
		if (_isUnloaded)
		{
			return;
		}

		bool isInstantMode = string.Equals(_currentRunMode, RunModeOptions.Instant, StringComparison.OrdinalIgnoreCase);
		bool instantStop = isInstantMode && _isInstantQueueButtonStop;

		MainActionIcon.Source = instantStop
			? "mode_run_stop.png"
			: "play_nexus.png";
		MainActionIconHover.Source = instantStop
			? "mode_run_stop_hover.png"
			: "play_nexus_hover.png";

		MainActionHoverGlowCore.Color = Color.FromArgb(instantStop ? "#FF4D6D" : "#10D9FF");
		MainActionHoverGlowEdge.Color = Color.FromArgb(instantStop ? "#00FF4D6D" : "#0010D9FF");

		if (instantStop)
		{
			StartMainActionPulse();
		}
		else
		{
			StopMainActionPulse();
		}
	}

	private void StartMainActionPulse()
	{
		if (_isUnloaded || _isMainActionPulseRunning)
		{
			return;
		}

		_isMainActionPulseRunning = true;
		MainActionStopSignalSurface.Opacity = 1;
		_mainActionStopSignalClip.PlayLoop(CanRunMainActionStopSignal);
	}

	private void StopMainActionPulse()
	{
		if (!_isMainActionPulseRunning && MainActionStopSignalSurface.Opacity <= 0)
		{
			return;
		}

		_isMainActionPulseRunning = false;
		_mainActionStopSignalClip.Stop();
		MainActionStopSignalSurface.Opacity = 0;
	}

	private void ResetMainActionPulse()
	{
		_isMainActionPulseRunning = false;
		_mainActionStopSignalClip.Stop();
		MainActionStopSignalSurface.Opacity = 0;
	}

	private bool CanRunMainActionStopSignal()
		=> !_isUnloaded
			&& _isMainActionPulseRunning
			&& IsVisible
			&& Handler is not null
			&& MainActionStopSignalSurface.Handler is not null;

	private static string NormalizeRunMode(string mode)
	{
		return mode.Trim() switch
		{
			RunModeOptions.OnChange => RunModeOptions.OnChange,
			RunModeOptions.Instant => RunModeOptions.Instant,
			_ => RunModeOptions.Default,
		};
	}

	private static ImageSource GetRunModeIcon(string mode)
	{
		string iconName = NormalizeRunMode(mode) switch
		{
			RunModeOptions.OnChange => "mode_onchange.png",
			RunModeOptions.Instant => "mode_instant.png",
			_ => "mode_run.png",
		};

		return ImageSource.FromFile(iconName);
	}

	private static Color GetRunModeTextColor(string mode)
	{
		return NormalizeRunMode(mode) switch
		{
			RunModeOptions.Instant => Color.FromArgb("#ffdfe7"),
			_ => Color.FromArgb("#dff8ff"),
		};
	}
}
