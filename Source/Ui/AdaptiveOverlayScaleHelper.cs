namespace ComfyUI_Nexus.Ui;

internal sealed class AdaptiveOverlayScaleHelper(VisualElement viewport, VisualElement target, AdaptiveOverlayScaleOptions options)
{
	internal void Apply()
	{
		if (!CanMeasure() || viewport.Width <= 0 || viewport.Height <= 0)
		{
			return;
		}

		try
		{
			target.AnchorX = 0.5;
			target.AnchorY = 0.5;

			double availableWidth = Math.Max(1, viewport.Width * options.ViewportWidthRatio);
			double availableHeight = Math.Max(1, viewport.Height * options.ViewportHeightRatio);
			double widthConstraint = ResolveConstraint(target.WidthRequest, availableWidth);
			var measured = target.Measure(widthConstraint, double.PositiveInfinity);
			double contentWidth = ResolveContentSize(measured.Width, target.DesiredSize.Width, target.Width, target.WidthRequest);
			double contentHeight = ResolveContentSize(measured.Height, target.DesiredSize.Height, target.Height, target.HeightRequest);
			double thresholdScale = Math.Min(1, Math.Min(viewport.Width / options.ScaleThresholdWidth, viewport.Height / options.ScaleThresholdHeight));
			double contentScale = Math.Min(1, Math.Min(availableWidth / contentWidth, availableHeight / contentHeight));
			target.Scale = Math.Clamp(Math.Min(thresholdScale, contentScale), options.MinimumScale, 1);
		}
		catch (ObjectDisposedException)
		{
			// The app is shutting down and MAUI services may already be disposed.
		}
		catch (InvalidOperationException) when (!CanMeasure())
		{
			// Late layout callback after the overlay was detached.
		}
	}

	private bool CanMeasure()
	{
		return viewport.Handler != null &&
			target.Handler != null &&
			viewport.Window != null &&
			target.Window != null;
	}

	private static double ResolveConstraint(double requested, double available)
	{
		return requested > 1 ? requested : available;
	}

	private static double ResolveContentSize(double measured, double desired, double actual, double requested)
	{
		if (measured > 1)
		{
			return measured;
		}

		if (desired > 1)
		{
			return desired;
		}

		if (actual > 1)
		{
			return actual;
		}

		return requested > 1 ? requested : 1;
	}
}

internal readonly record struct AdaptiveOverlayScaleOptions(
	double ScaleThresholdWidth,
	double ScaleThresholdHeight,
	double ViewportWidthRatio = 0.94,
	double ViewportHeightRatio = 0.92,
	double MinimumScale = 0.72);
