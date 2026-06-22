using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views;

public partial class HeaderView : ContentView
{
	private const double MiniUsageTrackWidth = 50;
	private const double GaugeWidthSnapThreshold = 0.5;
	private const double LogoHoverGlowScale = 1.2;
	private const double LogoHiddenGlowScale = 0.5;
	private const double LogoHoverRotation = 90;
	private const int LogoSecretClickTarget = 5;
	private const int GaugeAnimationRate = 16;
	private const uint GaugeAnimationLength = 250;
	private const uint LogoHoverGlowLength = 250;
	private const uint LogoHoverRotationLength = 300;
	private const uint LogoExitGlowLength = 200;
	private const uint LogoExitRotationLength = 250;
	private const string CpuMiniBarAnimationName = "CpuMiniBarAnim";
	private const string GpuMiniBarAnimationName = "GpuMiniBarAnim";
	private static readonly Color GpuExecutingColor = Color.FromArgb("#ff4444");
	private static readonly Color GpuIdleColor = Color.FromArgb("#22d3ee");
	private static readonly Color GpuTotalVramTextColor = Color.FromArgb("#8fb9d8");
	private static readonly Color UsageHighBarColor = Color.FromArgb("#ff7a8f");
	private static readonly Color UsageMediumBarColor = Color.FromArgb("#ffca6f");
	private static readonly Color UsageNormalBarColor = Color.FromArgb("#8cf1ff");
	private static readonly Color UsageHighTextColor = Color.FromArgb("#ffd4db");
	private static readonly Color UsageMediumTextColor = Color.FromArgb("#ffe6b0");
	private static readonly Color UsageNormalTextColor = NexusColors.TextPrimary;
	private int _logoSecretClickCount;
	private bool _isLogoSecretArmed;
#if WINDOWS
	private Microsoft.UI.Xaml.UIElement? _logoSecretPointerElement;
#endif
	internal event EventHandler? LogoClicked;
	internal event EventHandler? LogoFiveClicked;
	internal event EventHandler? MainActionRequested;
	internal event EventHandler? StopActionRequested;
	internal event Action<string>? RunModeRequested;
	internal event EventHandler? ViewQueueRequested;
	internal event EventHandler? TogglePropertiesRequested;
	internal event EventHandler? ShowManagerRequested;
	internal event EventHandler? ShowFavoritesRequested;
	internal event EventHandler? UnloadModelsRequested;
	internal event EventHandler? FreeCacheRequested;
	internal event EventHandler? ShareRequested;
	internal event EventHandler? EnterAppModeRequested;
	internal event EventHandler? WorkflowActionsRequested;

	public HeaderView()
	{
		InitializeComponent();
		WireToolbarTrayEvents();
		WireLogoSecretPointerHandler();
	}

	private void WireToolbarTrayEvents()
	{
		ToolbarTrayControl.MainActionRequested += (_, e) => MainActionRequested?.Invoke(this, e);
		ToolbarTrayControl.StopActionRequested += (_, e) => StopActionRequested?.Invoke(this, e);
		ToolbarTrayControl.RunModeRequested += mode => RunModeRequested?.Invoke(mode);
		ToolbarTrayControl.ViewQueueRequested += (_, e) => ViewQueueRequested?.Invoke(this, e);
		ToolbarTrayControl.TogglePropertiesRequested += (_, e) => TogglePropertiesRequested?.Invoke(this, e);
		ToolbarTrayControl.ShowManagerRequested += (_, e) => ShowManagerRequested?.Invoke(this, e);
		ToolbarTrayControl.ShowFavoritesRequested += (_, e) => ShowFavoritesRequested?.Invoke(this, e);
		ToolbarTrayControl.UnloadModelsRequested += (_, e) => UnloadModelsRequested?.Invoke(this, e);
		ToolbarTrayControl.FreeCacheRequested += (_, e) => FreeCacheRequested?.Invoke(this, e);
		ToolbarTrayControl.ShareRequested += (_, e) => ShareRequested?.Invoke(this, e);
		ToolbarTrayControl.EnterAppModeRequested += (_, e) => EnterAppModeRequested?.Invoke(this, e);
		ToolbarTrayControl.WorkflowActionsRequested += (_, e) => WorkflowActionsRequested?.Invoke(this, e);
	}

	private void OnLogoTapped(object? sender, TappedEventArgs e)
	{
		if (_isLogoSecretArmed)
		{
			_isLogoSecretArmed = false;
			_logoSecretClickCount = 0;
			LogoFiveClicked?.Invoke(this, EventArgs.Empty);
			return;
		}

		LogoClicked?.Invoke(this, EventArgs.Empty);
	}

	internal void SetExecutionState(bool isRunning)
	{
		ToolbarTrayControl.SetExecutionState(isRunning);
		MainThread.BeginInvokeOnMainThread(() =>
		{
			GpuRunningIndicator.BackgroundColor = isRunning ? GpuExecutingColor : GpuIdleColor;
		});
	}

	internal bool IsExecutingState()
	{
		return ToolbarTrayControl.IsExecutingState();
	}

	internal void SetInstantQueueButtonStop(bool isStopButton)
	{
		ToolbarTrayControl.SetInstantQueueButtonStop(isStopButton);
	}

	internal void SetQueueCount(int count)
	{
		ToolbarTrayControl.SetQueueCount(count);
	}

	internal int GetQueueCount()
	{
		return ToolbarTrayControl.GetQueueCount();
	}

	internal void SetRunMode(string mode)
	{
		ToolbarTrayControl.SetRunMode(mode);
	}

	internal void SetViewQueueActive(bool isActive)
	{
		ToolbarTrayControl.SetViewQueueActive(isActive);
	}

	internal bool IsViewQueueActive()
	{
		return ToolbarTrayControl.IsViewQueueActive();
	}

	internal void DismissRunModeOptionsFromGlobalPointerRelease()
	{
		ToolbarTrayControl.DismissRunModeOptionsFromGlobalPointerRelease();
	}

	internal void InvalidateShellLayout()
	{
		ToolbarTrayControl.InvalidateShellLayout();
	}

	internal void ClearWorkflowSummary()
	{
		ActiveWorkflowNameLabel.Text = "No workflow selected";
		ActiveWorkflowBadgesStack.IsVisible = false;
		ActiveSavedStateLabel.Text = string.Empty;
		ActiveModifiedStateBadge.IsVisible = false;
	}

	internal void UpdateWorkflowSummary(string name, bool hasFile, bool modified, bool bookmarked)
	{
		ActiveWorkflowNameLabel.Text = name;
		ActiveWorkflowBadgesStack.IsVisible = true;

		ActiveSavedStateLabel.Text = hasFile ? "SAVED" : "UNSAVED";
		ActiveSavedStateLabel.TextColor = hasFile
			? Color.FromArgb("#7fe7ff")
			: NexusColors.AccentText;
		ActiveSavedStateBadge.BackgroundColor = hasFile
			? Color.FromArgb("#0f1d2c")
			: Color.FromArgb("#15202b");
		ActiveSavedStateBadge.StrokeThickness = 0;
		ActiveSavedStateBadge.Opacity = bookmarked ? 1 : 0.92;
		ActiveModifiedStateBadge.IsVisible = modified;
	}

	internal void SetWorkflowStatusBarState(bool isVisible, bool inputTransparent, double opacity = 1)
	{
		WorkflowStatusBarBorder.IsVisible = isVisible;
		WorkflowStatusBarBorder.InputTransparent = inputTransparent;
		WorkflowStatusBarBorder.Opacity = opacity;
	}

	internal void SetLoadingHaloState(double opacity, double scale)
	{
		LoadingHalo.Opacity = opacity;
		LoadingHalo.Scale = scale;
	}

	internal void SetLoadingHaloOpacity(double opacity)
	{
		LoadingHalo.Opacity = opacity;
	}

	internal void SetLoadingHaloScale(double scale)
	{
		LoadingHalo.Scale = scale;
	}

	internal Task FadeLoadingHaloAsync(double opacity, uint length)
	{
		return LoadingHalo.FadeToAsync(opacity, length);
	}

	internal void SetLoadingLogoRotation(double rotation)
	{
		LogoImage.Rotation = rotation;
	}

	internal void SetRightPaneOpacity(double opacity)
	{
		HeaderRightGrid.Opacity = opacity;
	}

	internal void SetLogoInputTransparent(bool inputTransparent)
	{
		LogoHostBorder.InputTransparent = inputTransparent;
	}

	internal double GetRootHeight()
	{
		return NativeHeaderGrid.Height;
	}

	internal void SetTabRowHeights(double height)
	{
		TabRowsGrid.HeightRequest = height;
		TabRowsGrid.Padding = Thickness.Zero;
		PrimaryTabsGrid.HeightRequest = height;
	}

	internal void SetTabRowsOpacity(double opacity)
	{
		PrimaryTabsGrid.Opacity = opacity;
	}

	internal void ResetTabSurface()
	{
		PrimaryTabsGrid.Children.Clear();
		PrimaryTabsGrid.ColumnDefinitions.Clear();
	}

	internal void RemoveTabSurfaceChild(View child)
	{
		PrimaryTabsGrid.Children.Remove(child);
	}

	internal void AddTabColumn(GridLength width)
	{
		PrimaryTabsGrid.ColumnDefinitions.Add(new ColumnDefinition(width));
	}

	internal void AddTabSurfaceChild(View child, int column)
	{
		Grid.SetColumn(child, column);
		PrimaryTabsGrid.Children.Add(child);
	}

	internal void InvalidateTabSurface()
	{
		PrimaryTabsGrid.InvalidateMeasure();
		TabRowsGrid.InvalidateMeasure();
		Dispatcher.Dispatch(() =>
		{
			PrimaryTabsGrid.InvalidateMeasure();
			TabRowsGrid.InvalidateMeasure();
		});
	}

	internal void SetGpuVisibility(bool isVisible)
	{
		SystemStatusStack.IsVisible = isVisible;
	}

	internal void UpdateGpuSummary(
		string modelName,
		double activePercent,
		double allocatedGiB,
		double cachedGiB,
		double totalGiB,
		Color accent,
		Color accentSoft)
	{
		GpuStatusStack.IsVisible = true;
		GpuModelLabel.Text = modelName.ToUpperInvariant();
		GpuUtilizationLabel.Text = $"{Math.Round(activePercent):0}%";
		GpuActiveVramLabel.Text = $"Active {allocatedGiB:0.0}";
		GpuCachedVramLabel.Text = $"Cache {cachedGiB:0.0}";
		GpuTotalVramLabel.Text = $"Total {totalGiB:0.0}";
		GpuUtilizationLabel.TextColor = accent;
		GpuModelLabel.TextColor = accentSoft;
		GpuActiveVramLabel.TextColor = Colors.White;
		GpuCachedVramLabel.TextColor = accentSoft;
		GpuTotalVramLabel.TextColor = GpuTotalVramTextColor;
	}

	internal void UpdateSystemUsageSummary(double cpuPercent, double gpuPercent, Color gpuAccent)
	{
		SystemStatusStack.IsVisible = true;
		CpuUsageValueLabel.Text = $"{Math.Round(cpuPercent):0}%";
		GpuMiniUsageValueLabel.Text = $"{Math.Round(gpuPercent):0}%";

		double targetCpuWidth = (Math.Clamp(cpuPercent, 0, 100) / 100.0) * MiniUsageTrackWidth;
		double targetGpuWidth = (Math.Clamp(gpuPercent, 0, 100) / 100.0) * MiniUsageTrackWidth;

		// Animate gauge bar widths for smooth transitions
		AnimateGauge(CpuUsageBarFill, targetCpuWidth, CpuMiniBarAnimationName);
		AnimateGauge(GpuMiniUsageBarFill, targetGpuWidth, GpuMiniBarAnimationName);

		CpuUsageValueLabel.TextColor = GetUsageTextColor(cpuPercent);
		GpuMiniUsageValueLabel.TextColor = gpuAccent;
		CpuUsageBarFill.BackgroundColor = GetUsageBarColor(cpuPercent);
		GpuMiniUsageBarFill.BackgroundColor = gpuAccent;
	}

	private void AnimateGauge(VisualElement element, double targetWidth, string animName)
	{
		element.AbortAnimation(animName);

		if (Math.Abs(element.WidthRequest - targetWidth) < GaugeWidthSnapThreshold)
		{
			element.WidthRequest = targetWidth;
			return;
		}

		var anim = new Animation(v => element.WidthRequest = v, element.WidthRequest, targetWidth);
		anim.Commit(this, animName, GaugeAnimationRate, GaugeAnimationLength, Easing.CubicOut);
	}

	internal void SetGpuBarBackground(Brush brush)
	{
		GpuUtilBarFill.Background = brush;
	}

	internal double GetGpuCacheBarWidth()
	{
		return GpuCacheBarFill.WidthRequest;
	}

	internal double GetGpuBarWidth()
	{
		return GpuUtilBarFill.WidthRequest;
	}

	internal void SetGpuCacheBarWidth(double width)
	{
		GpuCacheBarFill.WidthRequest = width;
	}

	internal void SetGpuBarWidth(double width)
	{
		GpuUtilBarFill.WidthRequest = width;
	}

	internal void SetGpuBarOpacity(double opacity)
	{
		GpuUtilBarFill.Opacity = opacity;
		GpuCacheBarFill.Opacity = 0.22 + (opacity * 0.2);
	}

	internal void ApplyGpuIndicatorPalette(
		Color core,
		Color blob,
		Color shell,
		Color shadowBrush,
		float shadowOpacity,
		float shadowRadius)
	{
		GpuRunningIndicator.BackgroundColor = core;
		GpuRunningBlob.BackgroundColor = blob;
		GpuRunningIndicatorShell.BackgroundColor = shell;
		if (GpuRunningIndicatorShell.Shadow is Shadow shadow)
		{
			shadow.Brush = shadowBrush;
			shadow.Opacity = shadowOpacity;
			shadow.Radius = shadowRadius;
		}
	}

	internal void SetGpuIndicatorVisualState(
		double coreOpacity,
		double coreScale,
		double blobOpacity,
		double blobScale,
		double shellScale)
	{
		GpuRunningIndicator.Opacity = coreOpacity;
		GpuRunningIndicator.Scale = coreScale;
		GpuRunningBlob.Opacity = blobOpacity;
		GpuRunningBlob.Scale = blobScale;
		GpuRunningIndicatorShell.Scale = shellScale;
	}

	internal void SetGpuIndicatorCoreScale(double scale)
	{
		GpuRunningIndicator.Scale = scale;
	}

	internal void SetGpuIndicatorBlobScale(double scale)
	{
		GpuRunningBlob.Scale = scale;
	}

	internal void SetGpuIndicatorBlobOpacity(double opacity)
	{
		GpuRunningBlob.Opacity = opacity;
	}

	internal void SetGpuIndicatorShellScale(double scale)
	{
		GpuRunningIndicatorShell.Scale = scale;
	}

	internal double GetCpuUsageBarWidth()
	{
		return CpuUsageBarFill.WidthRequest;
	}

	internal void SetCpuUsageBarWidth(double width)
	{
		CpuUsageBarFill.WidthRequest = width;
	}

	internal double GetGpuMiniUsageBarWidth()
	{
		return GpuMiniUsageBarFill.WidthRequest;
	}

	internal void SetGpuMiniUsageBarWidth(double width)
	{
		GpuMiniUsageBarFill.WidthRequest = width;
	}

	internal double GetMiniUsageTrackWidth()
	{
		return MiniUsageTrackWidth;
	}

	private static Color GetUsageBarColor(double usagePercent)
	{
		if (usagePercent >= 85)
		{
			return UsageHighBarColor;
		}

		if (usagePercent >= 55)
		{
			return UsageMediumBarColor;
		}

		return UsageNormalBarColor;
	}

	private static Color GetUsageTextColor(double usagePercent)
	{
		if (usagePercent >= 85)
		{
			return UsageHighTextColor;
		}

		if (usagePercent >= 55)
		{
			return UsageMediumTextColor;
		}

		return UsageNormalTextColor;
	}

	private void OnLogoPointerEntered(object? sender, PointerEventArgs e)
	{
		_ = LogoGlow.FadeToAsync(1, LogoHoverGlowLength, Easing.CubicOut);
		_ = LogoGlow.ScaleToAsync(LogoHoverGlowScale, LogoHoverGlowLength, Easing.CubicOut);
		_ = LogoImage.RotateToAsync(LogoHoverRotation, LogoHoverRotationLength, Easing.CubicOut);
	}

	private void RegisterLogoSecretRightClick()
	{
		if (_isLogoSecretArmed)
		{
			return;
		}

		_logoSecretClickCount++;
		if (_logoSecretClickCount < LogoSecretClickTarget)
		{
			return;
		}

		_isLogoSecretArmed = true;
	}

#if WINDOWS
	private void WireLogoSecretPointerHandler()
	{
		LogoHostBorder.HandlerChanged += OnLogoHostBorderHandlerChanged;
	}

	private void OnLogoHostBorderHandlerChanged(object? sender, EventArgs e)
	{
		if (_logoSecretPointerElement is not null)
		{
			_logoSecretPointerElement.PointerPressed -= OnLogoNativePointerPressed;
			_logoSecretPointerElement = null;
		}

		if (LogoHostBorder.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
		{
			_logoSecretPointerElement = element;
			_logoSecretPointerElement.PointerPressed += OnLogoNativePointerPressed;
		}
	}

	private void OnLogoNativePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Microsoft.UI.Xaml.UIElement element)
		{
			return;
		}

		var pointerPoint = e.GetCurrentPoint(element);
		if (!pointerPoint.Properties.IsRightButtonPressed)
		{
			return;
		}

		RegisterLogoSecretRightClick();
		e.Handled = true;
	}
#else
	private void WireLogoSecretPointerHandler()
	{
	}
#endif

	private void OnLogoPointerExited(object? sender, PointerEventArgs e)
	{
		_ = LogoGlow.FadeToAsync(0, LogoExitGlowLength, Easing.CubicIn);
		_ = LogoGlow.ScaleToAsync(LogoHiddenGlowScale, LogoExitGlowLength, Easing.CubicIn);
		_ = LogoImage.RotateToAsync(0, LogoExitRotationLength, Easing.CubicIn);
	}

	internal void SetPropertiesActive(bool isActive)
	{
		ToolbarTrayControl.SetPropertiesActive(isActive);
	}

	internal void SetManagerActionsVisible(bool isVisible)
	{
		ToolbarTrayControl.SetManagerActionsVisible(isVisible);
	}
}
