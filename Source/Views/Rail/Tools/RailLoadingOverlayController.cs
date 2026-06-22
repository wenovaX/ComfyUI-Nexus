using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Views.Rail.Tools;

internal sealed class RailLoadingOverlayController
{
	private const int MinimumVisibleMilliseconds = 350;

	private readonly VisualElement _overlay;
	private CancellationTokenSource? _visibilityCts;
	private int _version;
	private DateTime _shownAtUtc = DateTime.MinValue;

	internal RailLoadingOverlayController(VisualElement overlay)
	{
		_overlay = overlay;
	}

	internal async Task ShowAsync()
	{
		Show();

		await Task.Yield();
	}

	internal void Show()
	{
		_visibilityCts?.Cancel();
		_visibilityCts = new CancellationTokenSource();
		_shownAtUtc = DateTime.UtcNow;
		Interlocked.Increment(ref _version);
		_overlay.Opacity = 1;
		_overlay.IsVisible = true;
	}

	internal async Task HideAsync()
	{
		int version = _version;
		CancellationToken cancellationToken = _visibilityCts?.Token ?? CancellationToken.None;
		TimeSpan elapsed = DateTime.UtcNow - _shownAtUtc;
		int remainingMilliseconds = MinimumVisibleMilliseconds - (int)elapsed.TotalMilliseconds;
		if (remainingMilliseconds > 0)
		{
			try
			{
				await Task.Delay(remainingMilliseconds, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}

		if (version != _version || !_overlay.IsVisible)
		{
			return;
		}

		try
		{
			if (version == _version)
			{
				_overlay.IsVisible = false;
				_overlay.Opacity = 1;
			}
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"Rail loading overlay hide skipped: {ex.Message}");
		}
	}
}
