using ComfyUI_Nexus.Configuration;
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

	private bool _isInstantQueueButtonStop;
	private bool _isMainActionPulseRunning;
	private int _mainActionPulseVersion;
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
		RunModeRequested?.Invoke(_currentRunMode);
	}

	private void UpdateMainActionVisualState()
	{
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

	private async void StartMainActionPulse()
	{
		if (_isMainActionPulseRunning)
		{
			return;
		}

		_isMainActionPulseRunning = true;
		int version = ++_mainActionPulseVersion;

		MainActionPulseGlow.Opacity = 0;
		MainActionPulseGlow.Scale = MainActionPulseInitialScale;

		while (version == _mainActionPulseVersion)
		{
			await Task.WhenAll(
				MainActionPulseGlow.FadeToAsync(MainActionPulseHighOpacity, MainActionPulseRiseLength, Easing.CubicOut),
				MainActionPulseGlow.ScaleToAsync(MainActionPulseHighScale, MainActionPulseRiseLength, Easing.CubicOut));

			if (version != _mainActionPulseVersion) break;

			await Task.WhenAll(
				MainActionPulseGlow.FadeToAsync(MainActionPulseLowOpacity, MainActionPulseFallLength, Easing.CubicIn),
				MainActionPulseGlow.ScaleToAsync(MainActionPulseLowScale, MainActionPulseFallLength, Easing.CubicIn));
		}

		if (version == _mainActionPulseVersion)
		{
			_isMainActionPulseRunning = false;
		}
	}

	private void StopMainActionPulse()
	{
		if (!_isMainActionPulseRunning && MainActionPulseGlow.Opacity <= 0)
		{
			return;
		}

		_isMainActionPulseRunning = false;
		_mainActionPulseVersion++;
		_ = Task.WhenAll(
			MainActionPulseGlow.FadeToAsync(0, MainActionPulseStopLength, Easing.CubicIn),
			MainActionPulseGlow.ScaleToAsync(MainActionPulseInitialScale, MainActionPulseStopLength, Easing.CubicIn));
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
