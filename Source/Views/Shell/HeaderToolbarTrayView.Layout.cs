namespace ComfyUI_Nexus.Views;

public partial class HeaderToolbarTrayView
{
	private void OnToolbarLayoutInvalidated(object? sender, EventArgs e)
	{
		if (_isUnloaded)
		{
			return;
		}

		QueueCommandDeckPlacementUpdate();
	}

	private void QueueCommandDeckPlacementUpdate()
	{
		if (_isUnloaded || _isCommandDeckPlacementUpdateQueued)
		{
			return;
		}

		_isCommandDeckPlacementUpdateQueued = true;
		Dispatcher.Dispatch(() =>
		{
			if (_isUnloaded)
			{
				_isCommandDeckPlacementUpdateQueued = false;
				return;
			}

			_isCommandDeckPlacementUpdateQueued = false;
			UpdateCommandDeckVerticalPlacement();
			Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(CommandDeckLayoutRetryDelayMs), UpdateCommandDeckVerticalPlacement);
		});
	}

	private void UpdateCommandDeckVerticalPlacement()
	{
		if (_isUnloaded)
		{
			return;
		}

		double toolbarWidth = WorkflowToolBarStack.Width > 0 ? WorkflowToolBarStack.Width : Width;
		if (toolbarWidth <= 0)
		{
			return;
		}

		double centerWidth = GetAvailableCommandDeckWidth(toolbarWidth);
		bool hasEnoughCenterSpace = centerWidth >= CommandDeckWidth + CommandDeckComfortPadding;
		CommandDeckHost.TranslationY = hasEnoughCenterSpace ? CommandDeckRaisedY : CommandDeckLoweredY;
		WorkflowToolBarStack.HeightRequest = hasEnoughCenterSpace ? ToolbarNormalHeight : ToolbarLoweredHeight;
	}

	private double GetAvailableCommandDeckWidth(double toolbarWidth)
	{
		double measuredSideWidth =
			GetMeasuredWidth(ManagerActionsGroup) +
			GetMeasuredWidth(PropertiesButton) +
			ToolbarMeasuredSidePadding;

		double sideWidth = measuredSideWidth > ToolbarSideColumnsMinMeasuredWidth
			? Math.Max(ToolbarSideColumnsFallbackWidth, measuredSideWidth * 2)
			: ToolbarSideColumnsFallbackWidth;

		return toolbarWidth - sideWidth;
	}

	private static double GetMeasuredWidth(VisualElement element)
		=> element.IsVisible && element.Width > 0 ? element.Width : 0;
}
