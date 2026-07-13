using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Controls.OptionDeck;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views;

public partial class HeaderToolbarTrayView
{
	private const double MainActionPulseInitialScale = 0.82;
	private const double MainActionPulseHighOpacity = 0.42;
	private const double MainActionPulseHighScale = 1.14;
	private const double MainActionPulseLowOpacity = 0.06;
	private const double MainActionPulseLowScale = 0.92;
	private const uint MainActionPulseRiseLength = 420;
	private const uint MainActionPulseFallLength = 520;
	private const uint MainActionPulseStopLength = 160;
	private const string MainActionPulseAnimationName = "HeaderToolbar.MainActionPulse";

	private bool _isInstantQueueButtonStop;
	private bool _isMainActionPulseRunning;
	private int _mainActionPulseVersion;
	private readonly NexusMotionController _mainActionMotion;
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

		if (XamlLifetimeDiagnostics.AreTransformAnimationsDisabled)
		{
			ResetMainActionPulse();
			return;
		}

		int version = ++_mainActionPulseVersion;

		MainActionPulseGlow.Opacity = 0;
		MainActionPulseGlow.Scale = MainActionPulseInitialScale;
		_mainActionMotion.StartTimeline(
			MainActionPulseAnimationName,
			this,
			16,
			MainActionPulseRiseLength + MainActionPulseFallLength,
			Easing.Linear,
			() => !_isUnloaded && _isMainActionPulseRunning && version == _mainActionPulseVersion,
			ResetMainActionPulse,
			new SafeAnimation.TimelineSegment(0, (double)MainActionPulseRiseLength / (MainActionPulseRiseLength + MainActionPulseFallLength), value => MainActionPulseGlow.Opacity = value, 0, MainActionPulseHighOpacity, Easing.CubicOut),
			new SafeAnimation.TimelineSegment((double)MainActionPulseRiseLength / (MainActionPulseRiseLength + MainActionPulseFallLength), 1, value => MainActionPulseGlow.Opacity = value, MainActionPulseHighOpacity, MainActionPulseLowOpacity, Easing.CubicIn),
			new SafeAnimation.TimelineSegment(0, (double)MainActionPulseRiseLength / (MainActionPulseRiseLength + MainActionPulseFallLength), value => MainActionPulseGlow.Scale = value, MainActionPulseInitialScale, MainActionPulseHighScale, Easing.CubicOut),
			new SafeAnimation.TimelineSegment((double)MainActionPulseRiseLength / (MainActionPulseRiseLength + MainActionPulseFallLength), 1, value => MainActionPulseGlow.Scale = value, MainActionPulseHighScale, MainActionPulseLowScale, Easing.CubicIn));
		_isMainActionPulseRunning = true;
	}

	private void StopMainActionPulse()
	{
		if (!_isMainActionPulseRunning && MainActionPulseGlow.Opacity <= 0)
		{
			return;
		}

		_isMainActionPulseRunning = false;
		_mainActionPulseVersion++;
		_mainActionMotion.Stop(MainActionPulseAnimationName);
		SafeAnimation.Timeline(
			this,
			MainActionPulseAnimationName,
			16,
			MainActionPulseStopLength,
			Easing.CubicIn,
			null,
			"HeaderToolbar.MainActionPulse",
			new SafeAnimation.TimelineSegment(0, 1, value => MainActionPulseGlow.Opacity = value, MainActionPulseGlow.Opacity, 0, Easing.CubicIn),
			new SafeAnimation.TimelineSegment(0, 1, value => MainActionPulseGlow.Scale = value, MainActionPulseGlow.Scale, MainActionPulseInitialScale, Easing.CubicIn));
	}

	private void ResetMainActionPulse()
	{
		_isMainActionPulseRunning = false;
		MainActionPulseGlow.Opacity = 0;
		MainActionPulseGlow.Scale = MainActionPulseInitialScale;
	}

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
