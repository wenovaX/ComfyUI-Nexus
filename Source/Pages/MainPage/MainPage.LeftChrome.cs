using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private const double RailResizeWidthStep = 4d;
	private const double RailResizeGhostVisibleThreshold = 1;
	private const double RailResizeGhostOpacity = 0.16;
	private const double RailResizePreviewLineOpacity = 0.9;
	private const double RailResizeHandleIdleOpacity = 0.88;
	private const double RailResizeGripHoverOpacity = 0.95;
	private const double RailResizeGripIdleOpacity = 0.72;
	private const uint ControlDeckShowLength = 150;
	private const uint ControlDeckHideLength = 110;
	private const uint RailOpenAnimationLength = 150;
	private static readonly Color RailResizeGripDragColor = Color.FromArgb("#66ebff");
	private static readonly Color RailResizeGripHoverColor = NexusColors.Accent;
	private static readonly Color RailResizeGripIdleColor = Color.FromArgb("#17324a");

	private async Task SetControlDeckVisibleAsync(bool isVisible)
	{
		if (_isControlDeckVisible == isVisible)
		{
			return;
		}

		_isControlDeckVisible = isVisible;
		if (_isSystemLoading)
		{
			ApplyLeftChromeVisibilityState();
			return;
		}

		if (isVisible)
		{
			ControlDeckColumn.Width = new GridLength(ShellLayoutOptions.ControlDeckExpandedWidth);
			ControlDeckControl.WidthRequest = ShellLayoutOptions.ControlDeckExpandedWidth;
			RefreshAvailableWidthAndTabs(ShellLayoutInvalidationReason.ControlDeckChanged);
			ControlDeckControl.PrepareToShow();
			await ControlDeckControl.AnimateShowAsync(ControlDeckShowLength, Easing.CubicOut);
		}
		else
		{
			await ControlDeckControl.AnimateHideAsync(ControlDeckHideLength, Easing.CubicIn);
			ControlDeckColumn.Width = new GridLength(0);
			ControlDeckControl.WidthRequest = 0;
			RefreshAvailableWidthAndTabs(ShellLayoutInvalidationReason.ControlDeckChanged);
			ControlDeckControl.CompleteHide();
		}

		ApplyLeftChromeVisibilityState();
		RefreshAvailableWidthAndTabs(ShellLayoutInvalidationReason.ControlDeckChanged);
	}

	private Task ToggleRailAsync()
		=> RailControl.RequestToggleAsync();

	private async Task<bool> ExpandRailAsync(CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}

		RailControl.AbortRevealAnimation();
		try
		{
			_isRailAnimating = true;
			_isFileRailExpanded = true;
			bool completed = await OpenRailAsync(cancellationToken);
			if (completed && !cancellationToken.IsCancellationRequested)
			{
				RefreshAvailableWidthAndTabs(ShellLayoutInvalidationReason.RailStateChanged);
				return true;
			}

			return false;
		}
		finally
		{
			_isRailAnimating = false;
		}
	}

	private void CollapseRailImmediately()
	{
		var perf = RailPerformanceDiagnostics.Start();
		RailPerformanceDiagnostics.Mark("RailCollapseStart", perf);
		RailControl.AbortRevealAnimation();
		_isRailAnimating = false;
		_isFileRailExpanded = false;
		HideModelThumbnailPreview();

		double collapsedWidth = ShellLayoutOptions.CollapsedRailWidth;
		ApplyRailWidth(collapsedWidth);
		UpdateMediaViewerOverlayLayout();
		RailControl.InputTransparent = false;
		RailControl.SyncVisualState();

		RailResizeHandleControl.IsHandleVisible = false;
		RailResizeHandleControl.HandleOpacity = 0;

		ResetRailResizePreview();
		ApplyLeftChromeVisibilityState();
		RefreshAvailableWidthAndTabs(ShellLayoutInvalidationReason.RailStateChanged);
		RailPerformanceDiagnostics.Mark("RailCollapseCompleted", perf);
	}

	private void ConfigureRailResizeCursor()
	{
		PlatformManager.Current.Cursor.AttachResizeHandleCursor(
			RailResizeHandleControl.HandleElement,
			pointerEntered: () =>
			{
				_isRailResizeHovering = true;
				UpdateRailResizeVisualState(true, false);
			},
			pointerExited: () =>
			{
				_isRailResizeHovering = false;
				if (!RailResizePreviewLine.IsVisible)
				{
					UpdateRailResizeVisualState(false, false);
				}
			},
			pointerPressed: () =>
			{
				UpdateRailResizeVisualState(true, true);
			});

		UpdateRailResizeVisualState(false, false);
	}

	private void ApplyRailState()
	{
		ApplyRailWidth(GetTargetRailWidth());
		RailControl.SyncVisualState();

		ResetRailResizePreview();
		ApplyLeftChromeVisibilityState();
	}

	private async Task<bool> OpenRailAsync(CancellationToken cancellationToken)
	{
		var perf = RailPerformanceDiagnostics.Start();
		try
		{
			double targetWidth = _expandedRailWidth;
			double startWidth = GetCurrentRailWidthOrDefault(ShellLayoutOptions.CollapsedRailWidth);
			RailPerformanceDiagnostics.Mark("RailRevealPreparing", perf, $"start={startWidth:0.##}, target={targetWidth:0.##}");

			RailControl.SyncVisualState();
			RailControl.Opacity = 1;
			if (cancellationToken.IsCancellationRequested)
			{
				return false;
			}

			ApplyRailWidth(targetWidth);
			UpdateMediaViewerOverlayLayout();
			RailPerformanceDiagnostics.Mark("RailRevealFinalLayoutCommitted", perf);
			RailControl.PrepareRevealAnimation(targetWidth);
			RailPerformanceDiagnostics.Mark("RailRevealAnimationStart", perf);
			bool completed = await RailControl.AnimateRevealAsync(RailOpenAnimationLength, Easing.CubicOut, cancellationToken);
			RailPerformanceDiagnostics.Mark("RailRevealAnimationEnd", perf, $"completed={completed}");
			if (!completed || cancellationToken.IsCancellationRequested)
			{
				return false;
			}

			RailControl.CompleteRevealAnimation();
			RailControl.Opacity = 1;
			RailResizeHandleControl.IsHandleVisible = true;

			return true;
		}
		finally
		{
			RailControl.InputTransparent = false;
		}
	}

	private void InitializeRailRoot()
	{
		RailControl.SetRootPath(ResolveRailRootPath());
	}

	private string ResolveRailRootPath()
	{
		string comfyPath = ComfyPathResolver.ResolveConfiguredComfyPath();
		if (!string.IsNullOrWhiteSpace(comfyPath) && Directory.Exists(comfyPath))
		{
			var outputRoot = _assetHubService
				.GetDefaultRoots(comfyPath)
				.FirstOrDefault(root => string.Equals(root.Label, "Output", StringComparison.OrdinalIgnoreCase));

			if (!string.IsNullOrWhiteSpace(outputRoot.Path))
			{
				return outputRoot.Path;
			}

			var firstKnownRoot = _assetHubService.GetDefaultRoots(comfyPath).FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(firstKnownRoot.Path))
			{
				return firstKnownRoot.Path;
			}
		}

		var projectRoot = TryFindProjectRoot();
		if (!string.IsNullOrWhiteSpace(projectRoot))
		{
			return projectRoot;
		}

		return AppContext.BaseDirectory;
	}

	private string? TryFindProjectRoot()
	{
		try
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				bool hasProjectFile = dir.GetFiles("*.csproj").Any();
				bool hasEditorConfig = File.Exists(Path.Combine(dir.FullName, ".editorconfig"));
				if (hasProjectFile || hasEditorConfig)
				{
					return dir.FullName;
				}

				dir = dir.Parent;
			}
		}
		catch
		{
		}

		return null;
	}

	private void ApplyLeftChromeVisibilityState()
	{
		bool showRail = !_isSystemLoading || _isSuccessSequenceActive;
		bool showResize = showRail && _isFileRailExpanded;
		bool showDeck = (!_isSystemLoading || _isSuccessSequenceActive) && _isControlDeckVisible;

		double railWidth = GetTargetRailWidth();

		ControlDeckColumn.Width = new GridLength(showDeck ? ShellLayoutOptions.ControlDeckExpandedWidth : 0);
		ControlDeckControl.WidthRequest = showDeck ? ShellLayoutOptions.ControlDeckExpandedWidth : 0;
		RailControl.WidthRequest = showRail ? railWidth : 0;
		RailControl.TranslationX = 0;

		RailControl.IsVisible = showRail;
		RailControl.Opacity = 1;
		UpdateMediaViewerOverlayLayout();

		ControlDeckControl.SetDisplayState(showDeck, 1);

		RailResizeHandleControl.IsHandleVisible = showResize;
		RailResizeHandleControl.TranslationX = GetRailResizeHandleX(railWidth);
		RailResizeHandleControl.HandleOpacity = showResize ? RailResizeHandleControl.HandleOpacity : 0;

		if (_isSystemLoading)
		{
			ResetRailResizePreview();
		}

	}

	private void OnRailResizePanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		if (!_isFileRailExpanded || _isRailAnimating)
		{
			return;
		}

		switch (e.StatusType)
		{
			case GestureStatus.Started:
				_railResizeStartWidth = GetCurrentRailWidthOrDefault(_expandedRailWidth);
				_pendingRailWidth = _railResizeStartWidth;
				double startX = GetRailResizeBoundaryX(_railResizeStartWidth);
				RailResizeGhostFill.IsVisible = true;
				RailResizeGhostFill.Opacity = RailResizeGhostOpacity;
				RailResizeGhostFill.WidthRequest = 0;
				RailResizeGhostFill.TranslationX = startX;
				RailResizePreviewLine.IsVisible = true;
				RailResizePreviewLine.Opacity = RailResizePreviewLineOpacity;
				RailResizePreviewLine.TranslationX = startX;
				UpdateRailResizeVisualState(true, true);
				break;

			case GestureStatus.Running:
				double resizedWidth = Math.Clamp(_railResizeStartWidth + e.TotalX, ShellLayoutOptions.MinExpandedRailWidth, ShellLayoutOptions.MaxExpandedRailWidth);
				resizedWidth = Math.Round(resizedWidth / RailResizeWidthStep) * RailResizeWidthStep;
				_pendingRailWidth = resizedWidth;
				double previewX = GetRailResizeBoundaryX(resizedWidth);
				double originX = GetRailResizeBoundaryX(_railResizeStartWidth);
				RailResizePreviewLine.TranslationX = previewX;
				RailResizeGhostFill.TranslationX = Math.Min(originX, previewX);
				RailResizeGhostFill.WidthRequest = Math.Abs(previewX - originX);
				RailResizeGhostFill.Opacity = RailResizeGhostFill.WidthRequest > RailResizeGhostVisibleThreshold ? RailResizeGhostOpacity : 0;
				break;

			case GestureStatus.Completed:
			case GestureStatus.Canceled:
				ResetRailResizePreview();
				UpdateRailResizeVisualState(_isRailResizeHovering, false);

				_expandedRailWidth = Math.Clamp(_pendingRailWidth, ShellLayoutOptions.MinExpandedRailWidth, ShellLayoutOptions.MaxExpandedRailWidth);
				ApplyRailWidth(_expandedRailWidth);
				UpdateMediaViewerOverlayLayout();
				RefreshAvailableWidthAndTabs(ShellLayoutInvalidationReason.RailResized);
				break;
		}
	}

	private void ResetRailResizePreview()
	{
		RailResizeGhostFill.IsVisible = false;
		RailResizeGhostFill.Opacity = 0;
		RailResizeGhostFill.WidthRequest = 0;
		RailResizeGhostFill.TranslationX = 0;
		RailResizePreviewLine.IsVisible = false;
		RailResizePreviewLine.Opacity = 0;
		RailResizePreviewLine.TranslationX = 0;
	}

	private void UpdateRailResizeVisualState(bool isHovering, bool isDragging)
	{
		RailResizeHandleControl.HandleOpacity = isHovering || isDragging ? 1 : RailResizeHandleIdleOpacity;
		RailResizeHandleControl.GripColor = isDragging
			? RailResizeGripDragColor
			: isHovering ? RailResizeGripHoverColor : RailResizeGripIdleColor;
		RailResizeHandleControl.GripOpacity = isDragging ? 1 : isHovering ? RailResizeGripHoverOpacity : RailResizeGripIdleOpacity;
	}

	private double GetRailResizeBoundaryX(double railWidth)
		=> railWidth;

	private double GetRailResizeHandleX(double railWidth)
		=> GetRailResizeBoundaryX(railWidth) - ShellLayoutOptions.RailResizeHandleOffset;

	private double GetTargetRailWidth()
		=> _isFileRailExpanded ? _expandedRailWidth : ShellLayoutOptions.CollapsedRailWidth;

	private double GetCurrentRailWidthOrDefault(double fallbackWidth)
		=> RailControl.WidthRequest > 0 ? RailControl.WidthRequest : fallbackWidth;

	private void ApplyRailWidth(double railWidth)
	{
		RailControl.WidthRequest = railWidth;
		RailControl.TranslationX = 0;
		RailResizeHandleControl.TranslationX = GetRailResizeHandleX(railWidth);
	}
}
