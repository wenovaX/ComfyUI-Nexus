using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using System.Text.Json;

namespace ComfyUI_Nexus.Views.Overlays;

public partial class MediaViewerOverlayView : ContentView
{
	private const double ZoomedImageScale = 2;
	private const double KeyboardPanStep = 42;
	private const double TipStackBreakpoint = 920;
	private const double TipScaleBreakpoint = 700;
	private const double MinimumTipScale = 0.84;
	private const double DeleteDisabledOpacity = 0.45;
	private const double TipWideLayoutSideMargin = 102;
	private const double TipLayoutScaleEpsilon = 0.01;
	private const double PanClampDivisor = 2;
	private const uint ViewerShowFadeLength = 120;
	private const uint ViewerHideFadeLength = 100;
	private const uint ConfirmShowFadeLength = 110;
	private const uint ConfirmHideFadeLength = 90;
	private const string ViewerFadeAnimationName = "MediaViewerFade";
	private const string DefaultCssCursor = "default";
	private const string BlankVideoHtml = "<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;background:#02060c\"></body></html>";
	private static readonly Color TransparentButtonNormalColor = NexusColors.Transparent;
	private static readonly Color TransparentButtonHoverColor = Color.FromArgb("#1F31D8FF");
	private static readonly Color DefaultButtonNormalColor = Color.FromArgb("#121a25");
	private static readonly Color DefaultButtonHoverColor = NexusColors.SurfaceRaised;
	private static readonly Color ConfirmCancelNormalColor = Color.FromArgb("#151E2B");
	private static readonly Color ConfirmCancelHoverColor = Color.FromArgb("#1E2A3A");
	private static readonly Color ConfirmAcceptNormalColor = Color.FromArgb("#33202A");
	private static readonly Color ConfirmAcceptHoverColor = Color.FromArgb("#4A2637");
	private static readonly Color DeleteHoverBackgroundColor = Color.FromArgb("#800020");
	private static readonly Color DeleteHoverStrokeColor = Color.FromArgb("#FFFF9AAC");
	private static readonly Color DeleteHoverTextColor = Color.FromArgb("#FFF");
	private static readonly Color DeleteNormalBackgroundColor = Color.FromArgb("#21192327");
	private static readonly Color DeleteNormalStrokeColor = Color.FromArgb("#5aef6d9a");
	private static readonly Color DeleteNormalTextColor = NexusColors.DeleteText;

	private readonly List<MediaViewerItem> _items = [];
	private Func<MediaViewerItem, Task<bool>>? _deleteHandler;
	private Action? _hideCallback;
	private int _index;
	private bool _isZoomed;
	private bool _isTransitioning;
	private bool _isDeleteInProgress;
	private bool _isPointerOverImage;
	private bool _isImagePanActive;
	private NexusCursorShape? _activeCursorShape;
	private bool _isTipLayoutUpdateQueued;
	private bool? _isViewerTipLayoutStacked;
	private double _viewerTipLayoutScale = 1;
	private TaskCompletionSource<bool>? _confirmCompletion;
	private Point _panStart;
	private string? _videoHtmlPath;

	public MediaViewerOverlayView()
	{
		InitializeComponent();
		WireHoverState(ViewerPreviousButton, TransparentButtonNormalColor, TransparentButtonHoverColor);
		WireHoverState(ViewerNextButton, TransparentButtonNormalColor, TransparentButtonHoverColor);
		WireHoverState(ViewerCloseButton);
		WireDeleteHoverState();
		WireHoverState(ViewerConfirmCancelButton, ConfirmCancelNormalColor, ConfirmCancelHoverColor);
		WireHoverState(ViewerConfirmAcceptButton, ConfirmAcceptNormalColor, ConfirmAcceptHoverColor);

		var pan = new PanGestureRecognizer();
		pan.PanUpdated += OnImagePanUpdated;
		ViewerImageInteractionLayer.GestureRecognizers.Add(pan);
		var imagePointer = new PointerGestureRecognizer();
		imagePointer.PointerEntered += OnImagePointerEntered;
		imagePointer.PointerMoved += OnImagePointerMoved;
		imagePointer.PointerExited += OnImagePointerExited;
		ViewerImageInteractionLayer.GestureRecognizers.Add(imagePointer);
		ViewerTipBar.SizeChanged += OnViewerTipBarSizeChanged;
	}

	public bool IsOpen => IsVisible && !InputTransparent;

	public async Task ShowAsync(
		IEnumerable<MediaViewerItem> items,
		int startIndex,
		bool deleteEnabled,
		Func<MediaViewerItem, Task<bool>>? deleteHandler,
		Action? hideCallback = null)
	{
		var nextItems = items.Where(item => (item.IsImage || item.IsVideo) && File.Exists(item.FullPath)).ToList();
		if (nextItems.Count == 0)
		{
			return;
		}

		if (_isTransitioning)
		{
			return;
		}

		try
		{
			using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.Show");
			if (IsOpen)
			{
				if (IsCurrentFile(nextItems, startIndex))
				{
					return;
				}

				await UpdateOpenViewerAsync(nextItems, startIndex, deleteEnabled, deleteHandler, hideCallback);
				return;
			}

			_isTransitioning = true;
			SafeAnimation.AbortAnimation(this, ViewerFadeAnimationName, "MediaViewer.Show");
			_items.Clear();
			_items.AddRange(nextItems);

			_index = Math.Clamp(startIndex, 0, _items.Count - 1);
			_deleteHandler = deleteHandler;
			_hideCallback = hideCallback;
			ViewerDeleteButton.IsVisible = deleteEnabled && deleteHandler != null;
			SetDeleteUiEnabled(deleteEnabled && deleteHandler != null);
			InputTransparent = false;
			IsVisible = true;
			await ShowCurrentAsync();
			Opacity = 0;
			await SafeAnimation.FadeToAsync(this, 1, ViewerShowFadeLength, Easing.CubicOut, "MediaViewer.Show");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[MEDIA_VIEWER] Failed to show viewer");
		}
		finally
		{
			_isTransitioning = false;
		}
	}

	private bool IsCurrentFile(IReadOnlyList<MediaViewerItem> items, int startIndex)
	{
		if (_items.Count == 0 || _index < 0 || _index >= _items.Count || startIndex < 0 || startIndex >= items.Count)
		{
			return false;
		}

		return string.Equals(_items[_index].FullPath, items[startIndex].FullPath, StringComparison.OrdinalIgnoreCase);
	}

	private async Task UpdateOpenViewerAsync(
		IReadOnlyList<MediaViewerItem> items,
		int startIndex,
		bool deleteEnabled,
		Func<MediaViewerItem, Task<bool>>? deleteHandler,
		Action? hideCallback)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.UpdateOpen");
		if (_confirmCompletion != null)
		{
			ResolveConfirmDialog(false);
		}

		string? selectedPath = startIndex >= 0 && startIndex < items.Count
			? items[startIndex].FullPath
			: null;

		_items.Clear();
		_items.AddRange(items);
		_index = !string.IsNullOrWhiteSpace(selectedPath)
			? Math.Max(0, _items.FindIndex(item => string.Equals(item.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase)))
			: Math.Clamp(startIndex, 0, _items.Count - 1);
		_deleteHandler = deleteHandler;
		_hideCallback = hideCallback;
		ViewerDeleteButton.IsVisible = deleteEnabled && deleteHandler != null;
		SetDeleteUiEnabled(deleteEnabled && deleteHandler != null);
		ResetZoom();
		await ShowCurrentAsync();
	}

	public async Task HideAsync()
	{
		if (!IsVisible || _isTransitioning)
		{
			return;
		}

		try
		{
			using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.Hide");
			_isTransitioning = true;
			SafeAnimation.AbortAnimation(this, ViewerFadeAnimationName, "MediaViewer.Hide");
			InputTransparent = true;
			await StopVideoPlaybackAsync();
			await SafeAnimation.FadeToAsync(this, 0, ViewerHideFadeLength, Easing.CubicIn, "MediaViewer.Hide");
			ViewerImage.Source = null;
			ViewerVideo.Source = null;
			CleanupVideoHtmlFile();
			ResetZoom();
			ResetImageCursor();
			IsVisible = false;
			_hideCallback?.Invoke();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[MEDIA_VIEWER] Failed to hide viewer");
		}
		finally
		{
			_isTransitioning = false;
		}
	}

	public async Task<bool> TryHandleShortcutAsync(NexusKey key, bool ctrl, bool shift, bool alt)
	{
		if (!IsOpen || ctrl || shift || alt)
		{
			return false;
		}

		try
		{
			using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation($"MediaViewer.Shortcut.{key}");
			if (_confirmCompletion != null)
			{
				if (key == NexusKey.Enter)
				{
					ResolveConfirmDialog(true);
				}
				else if (key == NexusKey.Escape)
				{
					ResolveConfirmDialog(false);
				}

				return true;
			}

			switch (key)
			{
				case NexusKey.Escape:
					await HideAsync();
					return true;
				case NexusKey.Right:
					if (_isZoomed)
					{
						PanZoomedImage(-KeyboardPanStep, 0);
						return true;
					}
					Navigate(1);
					return true;
				case NexusKey.Left:
					if (_isZoomed)
					{
						PanZoomedImage(KeyboardPanStep, 0);
						return true;
					}
					Navigate(-1);
					return true;
				case NexusKey.Up:
					if (_isZoomed)
					{
						PanZoomedImage(0, KeyboardPanStep);
						return true;
					}
					return true;
				case NexusKey.Down:
					if (_isZoomed)
					{
						PanZoomedImage(0, -KeyboardPanStep);
						return true;
					}
					return true;
				case NexusKey.A:
				case NexusKey.D:
					return true;
				case NexusKey.Space:
				case NexusKey.Enter:
					if (IsCurrentItemVideo())
					{
						await ToggleVideoPlaybackAsync();
						return true;
					}

					ToggleZoom();
					return true;
				case NexusKey.Delete:
					await DeleteCurrentAsync();
					return true;
				default:
					return false;
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[MEDIA_VIEWER] Shortcut handling failed");
			return true;
		}
	}

	private async Task ShowCurrentAsync()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.ShowCurrent");

		if (_items.Count == 0)
		{
			await HideAsync();
			return;
		}

		PruneMissingItems();
		if (_items.Count == 0)
		{
			await HideAsync();
			return;
		}

		_index = Math.Clamp(_index, 0, _items.Count - 1);
		var item = _items[_index];
		bool hideSwap = _isZoomed;
		if (hideSwap)
		{
			await StopVideoPlaybackAsync();
			ViewerImage.Opacity = 0;
			ViewerImage.Source = null;
			ViewerVideo.Source = null;
			CleanupVideoHtmlFile();
			await Task.Yield();
		}
		else if (ViewerVideo.IsVisible)
		{
			await StopVideoPlaybackAsync();
		}

		ResetZoom();
		if (item.IsVideo)
		{
			ViewerImage.Source = null;
			ViewerImage.IsVisible = false;
			ViewerImageInteractionLayer.IsVisible = false;
			ViewerVideo.Source = BuildVideoSource(item.FullPath);
			ViewerVideo.IsVisible = true;
			UpdateVideoHintBar();
		}
		else
		{
			ViewerVideo.Source = null;
			CleanupVideoHtmlFile();
			ViewerVideo.IsVisible = false;
			ViewerImage.Source = ImageSource.FromFile(item.FullPath);
			ViewerImage.IsVisible = true;
			ViewerImage.Opacity = 1;
			ViewerImageInteractionLayer.IsVisible = true;
			UpdateHintBar();
		}

		ViewerTitleLabel.Text = item.Name;
		ViewerMetaLabel.Text = item.FullPath;
		ViewerCounterLabel.Text = $"{_index + 1} / {_items.Count}";
	}

	private UrlWebViewSource BuildVideoSource(string fullPath)
	{
		CleanupVideoHtmlFile();
		string tempRoot = ComfyInstallService.GetLocalRuntimePath("Cache/MediaViewer");
		Directory.CreateDirectory(tempRoot);
		_videoHtmlPath = Path.Combine(tempRoot, $"video-{Guid.NewGuid():N}.html");
		File.WriteAllText(_videoHtmlPath, BuildVideoHtml(fullPath));
		return new UrlWebViewSource { Url = new Uri(_videoHtmlPath).AbsoluteUri };
	}

	private static string BuildVideoHtml(string fullPath)
	{
		string source = new Uri(fullPath).AbsoluteUri;
		string mimeType = Path.GetExtension(fullPath).Equals(".mkv", StringComparison.OrdinalIgnoreCase)
			? "video/x-matroska"
			: "video/mp4";
		string sourceJson = JsonSerializer.Serialize(source);
		string mimeTypeJson = JsonSerializer.Serialize(mimeType);
		return $$"""
			<!doctype html>
			<html>
			<head>
				<meta charset="utf-8">
				<style>
					html, body {
						width: 100%;
						height: 100%;
						margin: 0;
						overflow: hidden;
						background: #02060c;
					}
					video {
						width: 100%;
						height: 100%;
						object-fit: contain;
						background: #02060c;
						cursor: pointer;
					}
					.play-toggle {
						position: fixed;
						left: 50%;
						top: 50%;
						width: 92px;
						height: 92px;
						transform: translate(-50%, -50%);
						border: 1px solid rgba(141, 231, 255, 0.74);
						border-radius: 999px;
						background: rgba(6, 16, 28, 0.72);
						box-shadow: 0 0 32px rgba(49, 216, 255, 0.26), inset 0 0 18px rgba(49, 216, 255, 0.12);
						color: #dff8ff;
						font-size: 38px;
						line-height: 1;
						display: flex;
						align-items: center;
						justify-content: center;
						cursor: pointer;
						transition: opacity 120ms ease, transform 120ms ease, background 120ms ease;
						z-index: 10;
					}
					.play-toggle:hover {
						background: rgba(12, 35, 54, 0.86);
						transform: translate(-50%, -50%) scale(1.04);
					}
					.play-toggle.hidden {
						opacity: 0;
						pointer-events: none;
					}
					.control-bar {
						position: fixed;
						left: 102px;
						right: 102px;
						bottom: 16px;
						min-height: 42px;
						display: grid;
						grid-template-columns: auto minmax(120px, 1fr) auto minmax(82px, 126px);
						align-items: center;
						gap: 12px;
						padding: 9px 12px;
						border: 1px solid rgba(88, 211, 255, 0.28);
						border-radius: 16px;
						background: linear-gradient(90deg, rgba(8, 20, 31, 0.88), rgba(10, 14, 24, 0.78));
						box-shadow: 0 0 22px rgba(49, 216, 255, 0.12), inset 0 0 18px rgba(49, 216, 255, 0.06);
						color: #dff8ff;
						font: 12px/1.2 system-ui, sans-serif;
						z-index: 9;
						opacity: 1;
						transform: translateY(0);
						transition: opacity 180ms ease, transform 180ms ease;
					}
					.control-bar.hidden {
						opacity: 0;
						transform: translateY(12px);
						pointer-events: none;
					}
					.control-button {
						width: 30px;
						height: 26px;
						border: 1px solid rgba(141, 231, 255, 0.46);
						border-radius: 10px;
						background: rgba(19, 48, 68, 0.8);
						color: #dff8ff;
						font-size: 13px;
						line-height: 1;
						cursor: pointer;
					}
					.time-label {
						color: #a9c4d7;
						font-variant-numeric: tabular-nums;
						white-space: nowrap;
					}
					.progress-range,
					.volume-range {
						width: 100%;
						height: 4px;
						margin: 0;
						accent-color: #31D8FF;
						cursor: pointer;
					}
					.volume-wrap {
						display: grid;
						grid-template-columns: auto 1fr;
						align-items: center;
						gap: 8px;
						min-width: 82px;
					}
					.volume-icon {
						color: #8de7ff;
						font-size: 13px;
						line-height: 1;
					}
					.video-error {
						position: fixed;
						left: 50%;
						bottom: 74px;
						max-width: min(720px, calc(100vw - 80px));
						transform: translateX(-50%);
						padding: 10px 14px;
						border: 1px solid rgba(255, 139, 168, 0.6);
						border-radius: 12px;
						background: rgba(28, 9, 18, 0.86);
						color: #ffd6df;
						font: 12px/1.45 system-ui, sans-serif;
						text-align: center;
						display: none;
						z-index: 11;
					}
					@media (max-width: 920px) {
						.control-bar {
							left: 14px;
							right: 14px;
						}
					}
					@media (max-width: 700px) {
						.control-bar {
							grid-template-columns: auto 1fr auto;
							gap: 8px;
							padding: 8px 10px;
						}
						.volume-wrap {
							display: none;
						}
					}
				</style>
			</head>
			<body>
				<video id="video" playsinline preload="metadata"></video>
				<button id="playToggle" class="play-toggle" type="button" aria-label="Play">▶</button>
				<div class="control-bar" aria-label="Video controls">
					<button id="barPlay" class="control-button" type="button" aria-label="Play">▶</button>
					<input id="progress" class="progress-range" type="range" min="0" max="1000" value="0" aria-label="Video progress">
					<div id="timeLabel" class="time-label">0:00 / 0:00</div>
					<div class="volume-wrap">
						<div id="volumeIcon" class="volume-icon">VOL</div>
						<input id="volume" class="volume-range" type="range" min="0" max="1" step="0.01" value="1" aria-label="Volume">
					</div>
				</div>
				<div id="videoError" class="video-error"></div>
				<script>
					const video = document.getElementById('video');
					const playToggle = document.getElementById('playToggle');
					const barPlay = document.getElementById('barPlay');
					const progress = document.getElementById('progress');
					const timeLabel = document.getElementById('timeLabel');
					const volume = document.getElementById('volume');
					const volumeIcon = document.getElementById('volumeIcon');
					const errorBox = document.getElementById('videoError');
					const controlBar = document.querySelector('.control-bar');
					const sourceUri = {{sourceJson}};
					const mimeType = {{mimeTypeJson}};
					const controlHideDelayMs = 2600;
					let isSeeking = false;
					let hideControlsTimer = 0;

					function showError(message) {
						errorBox.textContent = message;
						errorBox.style.display = 'block';
						playToggle.classList.remove('hidden');
					}

					function describeError() {
						const code = video.error ? video.error.code : 0;
						const labels = {
							1: 'loading aborted',
							2: 'network/local file access failed',
							3: 'decode failed or codec unsupported',
							4: 'format or codec unsupported'
						};
						return `Video could not be loaded (${labels[code] || 'unknown error'}, code ${code}).`;
					}

					function formatTime(seconds) {
						if (!Number.isFinite(seconds) || seconds < 0) {
							return '0:00';
						}
						const total = Math.floor(seconds);
						const minutes = Math.floor(total / 60);
						const rest = String(total % 60).padStart(2, '0');
						return `${minutes}:${rest}`;
					}

					function updateTime() {
						const duration = Number.isFinite(video.duration) && video.duration > 0 ? video.duration : 0;
						if (!isSeeking) {
							progress.value = duration > 0 ? Math.round((video.currentTime / duration) * 1000) : 0;
						}
						timeLabel.textContent = `${formatTime(video.currentTime)} / ${formatTime(duration)}`;
					}

					function updateVolume() {
						volume.value = video.volume;
						volumeIcon.textContent = video.muted || video.volume <= 0 ? 'MUTE' : 'VOL';
					}

					function clearControlTimer() {
						if (hideControlsTimer) {
							window.clearTimeout(hideControlsTimer);
							hideControlsTimer = 0;
						}
					}

					function showControls() {
						controlBar.classList.remove('hidden');
						clearControlTimer();
						if (!video.paused && !video.ended) {
							hideControlsTimer = window.setTimeout(() => {
								if (!video.paused && !video.ended && !isSeeking) {
									controlBar.classList.add('hidden');
								}
							}, controlHideDelayMs);
						}
					}

					function keepControlsVisible() {
						clearControlTimer();
						controlBar.classList.remove('hidden');
					}

					function syncButton() {
						const isPaused = video.paused || video.ended;
						playToggle.textContent = isPaused ? '▶' : 'Ⅱ';
						barPlay.textContent = isPaused ? '▶' : 'Ⅱ';
						playToggle.classList.toggle('hidden', !isPaused);
						if (isPaused) {
							keepControlsVisible();
						} else {
							showControls();
						}
					}

					async function togglePlayback() {
						if (video.paused || video.ended) {
							try { await video.play(); } catch (_) {}
						} else {
							video.pause();
						}
						syncButton();
					}

					window.nexusTogglePlayback = togglePlayback;
					playToggle.addEventListener('click', togglePlayback);
					barPlay.addEventListener('click', togglePlayback);
					video.addEventListener('click', togglePlayback);
					document.addEventListener('pointermove', showControls);
					document.addEventListener('pointerdown', showControls);
					controlBar.addEventListener('pointerenter', keepControlsVisible);
					controlBar.addEventListener('pointermove', keepControlsVisible);
					controlBar.addEventListener('pointerleave', showControls);
					video.addEventListener('play', syncButton);
					video.addEventListener('pause', syncButton);
					video.addEventListener('ended', syncButton);
					video.addEventListener('timeupdate', updateTime);
					video.addEventListener('durationchange', updateTime);
					video.addEventListener('volumechange', updateVolume);
					video.addEventListener('error', () => showError(describeError()));
					video.addEventListener('loadedmetadata', () => {
						errorBox.style.display = 'none';
						syncButton();
						updateTime();
						updateVolume();
					});
					progress.addEventListener('input', () => {
						isSeeking = true;
						keepControlsVisible();
						const duration = Number.isFinite(video.duration) && video.duration > 0 ? video.duration : 0;
						timeLabel.textContent = `${formatTime((Number(progress.value) / 1000) * duration)} / ${formatTime(duration)}`;
					});
					progress.addEventListener('change', () => {
						const duration = Number.isFinite(video.duration) && video.duration > 0 ? video.duration : 0;
						if (duration > 0) {
							video.currentTime = (Number(progress.value) / 1000) * duration;
						}
						isSeeking = false;
						updateTime();
						showControls();
					});
					volume.addEventListener('input', () => {
						keepControlsVisible();
						video.muted = false;
						video.volume = Number(volume.value);
						updateVolume();
					});
					const source = document.createElement('source');
					source.src = sourceUri;
					source.type = mimeType;
					video.appendChild(source);
					video.load();
					syncButton();
					updateTime();
					updateVolume();
				</script>
			</body>
			</html>
			""";
	}

	private void CleanupVideoHtmlFile()
	{
		if (string.IsNullOrWhiteSpace(_videoHtmlPath))
		{
			return;
		}

		try
		{
			if (File.Exists(_videoHtmlPath))
			{
				File.Delete(_videoHtmlPath);
			}
		}
		catch
		{
		}
		finally
		{
			_videoHtmlPath = null;
		}
	}

	private async Task ToggleVideoPlaybackAsync()
	{
		if (!ViewerVideo.IsVisible)
		{
			return;
		}

		try
		{
			await ViewerVideo.EvaluateJavaScriptAsync("window.nexusTogglePlayback && window.nexusTogglePlayback();");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Media viewer video toggle failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private async Task StopVideoPlaybackAsync()
	{
		if (!ViewerVideo.IsVisible && ViewerVideo.Source == null)
		{
			return;
		}

		try
		{
			await ViewerVideo.EvaluateJavaScriptAsync("""
				(() => {
					const video = document.getElementById('video');
					if (!video) return;
					try { video.pause(); } catch (_) {}
					try { video.removeAttribute('src'); } catch (_) {}
					try { while (video.firstChild) video.removeChild(video.firstChild); } catch (_) {}
					try { video.load(); } catch (_) {}
				})()
				""");
		}
		catch
		{
		}

		ViewerVideo.Source = new HtmlWebViewSource { Html = BlankVideoHtml };
		await Task.Delay(80);
	}

	private void PruneMissingItems()
	{
		for (int index = _items.Count - 1; index >= 0; index--)
		{
			if (!File.Exists(_items[index].FullPath))
			{
				_items.RemoveAt(index);
			}
		}
	}

	private void Navigate(int direction)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.Navigate");

		if (_items.Count <= 0)
		{
			return;
		}

		_index = (_index + direction + _items.Count) % _items.Count;
		FireAndForget(ShowCurrentAsync(), "[MEDIA_VIEWER] Failed to navigate media");
	}

	private async void OnDeleteTapped(object? sender, TappedEventArgs e)
		=> await RunEventAsync(DeleteCurrentAsync, "[MEDIA_VIEWER] Delete tap failed");

	private async Task DeleteCurrentAsync()
	{
		if (_deleteHandler == null || _items.Count == 0 || _isTransitioning || _isDeleteInProgress)
		{
			return;
		}

		try
		{
			using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.Delete");
			_isDeleteInProgress = true;
			SetDeleteUiEnabled(false);

			var item = _items[_index];
			bool confirm = await ShowConfirmDialogAsync(
				LocalizationManager.Text("views.overlays.media_viewer.delete_media_title"),
				LocalizationManager.Format("views.overlays.media_viewer.delete_media_message", item.Name));
			if (!confirm)
			{
				return;
			}

			bool deleted = false;
			try
			{
				deleted = await _deleteHandler(item);
			}
			catch (Exception ex)
			{
				NexusLog.Warning($"Media viewer delete failed: {ex.GetType().Name} - {ex.Message}");
			}

			if (!deleted)
			{
				return;
			}

			_items.RemoveAt(_index);
			if (_items.Count == 0)
			{
				await HideAsync();
				return;
			}

			_index = Math.Min(_index, _items.Count - 1);
			await ShowCurrentAsync();
		}
		finally
		{
			_isDeleteInProgress = false;
			if (IsVisible)
			{
				SetDeleteUiEnabled(true);
			}
		}
	}

	private async Task<bool> ShowConfirmDialogAsync(string title, string message)
	{
		if (_confirmCompletion != null)
		{
			return false;
		}

		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.Confirm.Show");
		_confirmCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		ViewerConfirmTitleLabel.Text = title;
		ViewerConfirmMessageLabel.Text = message;
		ViewerConfirmLayer.IsVisible = true;
		ViewerConfirmLayer.InputTransparent = false;
		ViewerConfirmLayer.Opacity = 0;
		await SafeAnimation.FadeToAsync(ViewerConfirmLayer, 1, ConfirmShowFadeLength, Easing.CubicOut, "MediaViewer.Confirm.Show");

		bool result = await _confirmCompletion.Task;
		await HideConfirmDialogAsync();
		return result;
	}

	private async Task HideConfirmDialogAsync()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.Confirm.Hide");
		await SafeAnimation.FadeToAsync(ViewerConfirmLayer, 0, ConfirmHideFadeLength, Easing.CubicIn, "MediaViewer.Confirm.Hide");
		ViewerConfirmLayer.InputTransparent = true;
		ViewerConfirmLayer.IsVisible = false;
		_confirmCompletion = null;
	}

	private void ResolveConfirmDialog(bool result)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.Confirm.Resolve");
		_confirmCompletion?.TrySetResult(result);
	}

	private void OnConfirmAcceptTapped(object? sender, TappedEventArgs e) => ResolveConfirmDialog(true);

	private void OnConfirmCancelTapped(object? sender, TappedEventArgs e) => ResolveConfirmDialog(false);

	private void SetDeleteUiEnabled(bool isEnabled)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.SetDeleteUiEnabled");
		ViewerDeleteButton.InputTransparent = !isEnabled;
		ViewerDeleteButton.Opacity = isEnabled ? 1 : DeleteDisabledOpacity;
	}

	private void ToggleZoom()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.ToggleZoom");
		if (IsCurrentItemVideo())
		{
			return;
		}

		_isZoomed = !_isZoomed;
		ViewerImage.Scale = _isZoomed ? ZoomedImageScale : 1;
		UpdateHintBar();
		UpdateImageCursor();
		if (!_isZoomed)
		{
			ViewerImage.TranslationX = 0;
			ViewerImage.TranslationY = 0;
		}
		else
		{
			ClampImagePan();
		}
	}

	private void ResetZoom()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.ResetZoom");
		_isZoomed = false;
		ViewerImage.Scale = 1;
		ViewerImage.TranslationX = 0;
		ViewerImage.TranslationY = 0;
		_isImagePanActive = false;
		UpdateHintBar();
		UpdateImageCursor();
	}

	private void PanZoomedImage(double x, double y)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.PanByKeyboard");
		if (!_isZoomed || IsCurrentItemVideo())
		{
			return;
		}

		ViewerImage.TranslationX += x;
		ViewerImage.TranslationY += y;
		ClampImagePan();
	}

	private void ClampImagePan()
	{
		if (!_isZoomed)
		{
			return;
		}

		double maxX = Math.Max(0, ViewerImageClip.Width * (ViewerImage.Scale - 1) / PanClampDivisor);
		double maxY = Math.Max(0, ViewerImageClip.Height * (ViewerImage.Scale - 1) / PanClampDivisor);
		ViewerImage.TranslationX = Math.Clamp(ViewerImage.TranslationX, -maxX, maxX);
		ViewerImage.TranslationY = Math.Clamp(ViewerImage.TranslationY, -maxY, maxY);
	}

	private void UpdateHintBar()
	{
		if (IsCurrentItemVideo())
		{
			UpdateVideoHintBar();
			return;
		}

		ShowImageHintLayout();
		if (_isZoomed)
		{
			ViewerPointerClickActionLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.zoom");
			ViewerPointerDragKeyLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.drag_key");
			ViewerPointerDragKeyHost.IsVisible = false;
			ViewerPointerDragActionLabel.IsVisible = false;
			ViewerPointerDragActionLabel.Text = string.Empty;
			ViewerHintPreviousKeyLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.arrow_keys");
			ViewerHintPreviousActionLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.move_image");
			ViewerHintNextKeyHost.IsVisible = false;
			ViewerHintNextKeyLabel.Text = string.Empty;
			ViewerHintNextActionLabel.IsVisible = false;
			ViewerHintNextActionLabel.Text = string.Empty;
			ViewerHintZoomKeyHost.IsVisible = true;
			ViewerHintZoomKeyLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.space_enter_keys");
			ViewerHintZoomActionLabel.IsVisible = true;
			ViewerHintZoomActionLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.zoom_out");
			return;
		}

		ViewerPointerClickActionLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.zoom");
		ViewerPointerDragKeyLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.drag_key");
		ViewerPointerDragKeyHost.IsVisible = true;
		ViewerPointerDragActionLabel.IsVisible = true;
		ViewerPointerDragActionLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.pan_while_zoomed");
		ViewerHintPreviousKeyLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.left_right_keys");
		ViewerHintPreviousActionLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.previous_next");
		ViewerHintNextKeyHost.IsVisible = false;
		ViewerHintNextKeyLabel.Text = string.Empty;
		ViewerHintNextActionLabel.IsVisible = false;
		ViewerHintNextActionLabel.Text = string.Empty;
		ViewerHintZoomKeyHost.IsVisible = true;
		ViewerHintZoomKeyLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.space_enter_keys");
		ViewerHintZoomActionLabel.IsVisible = true;
		ViewerHintZoomActionLabel.Text = LocalizationManager.Text("views.overlays.media_viewer.zoom_in");
	}

	private void UpdateVideoHintBar()
	{
		ViewerPointerTipGroup.IsVisible = false;
		ViewerKeyboardTipGroup.IsVisible = false;
		ViewerVideoTipLayout.IsVisible = true;
	}

	private void ShowImageHintLayout()
	{
		ViewerVideoTipLayout.IsVisible = false;
		ViewerPointerTipGroup.IsVisible = true;
		ViewerKeyboardTipGroup.IsVisible = true;
	}

	private bool IsCurrentItemVideo()
		=> _items.Count > 0 && _items[Math.Clamp(_index, 0, _items.Count - 1)].IsVideo;

	private void OnViewerTipBarSizeChanged(object? sender, EventArgs e)
	{
		if (_isTipLayoutUpdateQueued)
		{
			return;
		}

		_isTipLayoutUpdateQueued = true;
		Dispatcher.Dispatch(() =>
		{
			_isTipLayoutUpdateQueued = false;
			ApplyViewerTipLayout();
		});
	}

	private void ApplyViewerTipLayout()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.TipLayout");

		double width = ViewerTipBar.Width;
		if (width <= 0)
		{
			return;
		}

		bool shouldStack = width < TipStackBreakpoint;
		double targetScale = shouldStack && width < TipScaleBreakpoint
			? Math.Clamp(width / TipScaleBreakpoint, MinimumTipScale, 1.0)
			: 1.0;

		if (_isViewerTipLayoutStacked != shouldStack)
		{
			ViewerTipLayoutGrid.ColumnDefinitions[1].Width = shouldStack ? new GridLength(0) : GridLength.Star;
			ViewerKeyboardTipGroup.SetValue(Grid.RowProperty, shouldStack ? 1 : 0);
			ViewerKeyboardTipGroup.SetValue(Grid.ColumnProperty, shouldStack ? 0 : 1);
			ViewerTipLayoutGrid.Margin = shouldStack ? new Thickness(0) : new Thickness(TipWideLayoutSideMargin, 0);
			ViewerPointerTipStack.HorizontalOptions = shouldStack ? LayoutOptions.Center : LayoutOptions.Start;
			ViewerKeyboardTipStack.HorizontalOptions = shouldStack ? LayoutOptions.Center : LayoutOptions.End;
			_isViewerTipLayoutStacked = shouldStack;
		}

		if (Math.Abs(_viewerTipLayoutScale - targetScale) > TipLayoutScaleEpsilon)
		{
			ViewerTipLayoutGrid.Scale = targetScale;
			_viewerTipLayoutScale = targetScale;
		}
	}

	private void OnImagePanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("MediaViewer.Pan");

		if (!_isZoomed)
		{
			return;
		}

		switch (e.StatusType)
		{
			case GestureStatus.Started:
				_isImagePanActive = true;
				_panStart = new Point(ViewerImage.TranslationX, ViewerImage.TranslationY);
				UpdateImageCursor();
				break;
			case GestureStatus.Running:
				ViewerImage.TranslationX = _panStart.X + e.TotalX;
				ViewerImage.TranslationY = _panStart.Y + e.TotalY;
				ClampImagePan();
				UpdateImageCursor();
				break;
			case GestureStatus.Completed:
			case GestureStatus.Canceled:
				_isImagePanActive = false;
				UpdateImageCursor();
				break;
		}
	}

	private void OnImagePointerEntered(object? sender, PointerEventArgs e)
	{
		_isPointerOverImage = true;
		UpdateImageCursor();
	}

	private void OnImagePointerMoved(object? sender, PointerEventArgs e)
	{
		_isPointerOverImage = true;
		UpdateImageCursor();
	}

	private void OnImagePointerExited(object? sender, PointerEventArgs e)
	{
		_isPointerOverImage = false;
		_isImagePanActive = false;
		ResetImageCursor();
	}

	private void UpdateImageCursor()
	{
		if (!_isPointerOverImage)
		{
			return;
		}

		var shape = GetImageCursorShape();
		if (_activeCursorShape == shape)
		{
			return;
		}

		NexusAppManager.Instance.Platform.Cursor.SetCursor(ViewerImageInteractionLayer, shape);
		_activeCursorShape = shape;
	}

	private void ResetImageCursor()
	{
		_activeCursorShape = null;
		NexusAppManager.Instance.Platform.Cursor.SetCssCursor(ViewerImageInteractionLayer, DefaultCssCursor);
	}

	private NexusCursorShape GetImageCursorShape()
		=> _isZoomed
			? (_isImagePanActive ? NexusCursorShape.Grabbing : NexusCursorShape.Hand)
			: NexusCursorShape.ZoomIn;

	private void OnPreviousTapped(object? sender, TappedEventArgs e) => Navigate(-1);

	private void OnNextTapped(object? sender, TappedEventArgs e) => Navigate(1);

	private void OnImageTapped(object? sender, TappedEventArgs e) => ToggleZoom();

	private async void OnCloseTapped(object? sender, TappedEventArgs e)
		=> await RunEventAsync(HideAsync, "[MEDIA_VIEWER] Close tap failed");

	private async void OnBackdropTapped(object? sender, TappedEventArgs e)
		=> await RunEventAsync(HideAsync, "[MEDIA_VIEWER] Backdrop tap failed");

	private static async Task RunEventAsync(Func<Task> action, string context)
	{
		try
		{
			await action();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, context);
		}
	}

	private static async void FireAndForget(Task task, string context)
	{
		try
		{
			await task;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, context);
		}
	}

	private static void WireHoverState(Border target)
		=> WireHoverState(target, DefaultButtonNormalColor, DefaultButtonHoverColor);

	private static void WireHoverState(Border target, Color normalColor, Color hoverColor)
	{
		target.BackgroundColor = normalColor;
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) => target.BackgroundColor = hoverColor;
		pointer.PointerExited += (_, _) => target.BackgroundColor = normalColor;
		target.GestureRecognizers.Add(pointer);
	}

	private void WireDeleteHoverState()
	{
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) =>
		{
			ApplyDeleteHoverState(isHovered: true);
		};
		pointer.PointerExited += (_, _) =>
		{
			ApplyDeleteHoverState(isHovered: false);
		};
		ViewerDeleteButton.GestureRecognizers.Add(pointer);
	}

	private void ApplyDeleteHoverState(bool isHovered)
	{
		ViewerDeleteButton.BackgroundColor = isHovered ? DeleteHoverBackgroundColor : DeleteNormalBackgroundColor;
		ViewerDeleteButton.Stroke = isHovered ? DeleteHoverStrokeColor : DeleteNormalStrokeColor;
		ViewerDeleteLabel.TextColor = isHovered ? DeleteHoverTextColor : DeleteNormalTextColor;
	}
}
