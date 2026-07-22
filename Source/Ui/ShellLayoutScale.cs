namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Provides horizontal-only proportional scaling relative to a design-time reference width.
/// Vertical dimensions are intentionally left untouched.
/// </summary>
internal sealed class NexusShellLayoutScaleService : IDisposable
{
	/// <summary>Design-time reference width at which all layout constants look "perfect".</summary>
	private const double ReferenceWidth = 1280;

	/// <summary>Minimum allowed horizontal scale factor.</summary>
	private const double MinScale = 0.75;

	/// <summary>Maximum allowed horizontal scale factor.</summary>
	private const double MaxScale = 1.3;

	private double _scale = 1.0;
	private double _lastWidth;

	/// <summary>Current horizontal scale factor (1.0 == reference width).</summary>
	internal double Scale => _scale;

	/// <summary>Raised on the caller's thread when the scale factor actually changes.</summary>
	internal event Action? ScaleChanged;

	/// <summary>
	/// Recalculates the scale factor from the current window width.
	/// Only raises <see cref="ScaleChanged"/> when the factor actually differs.
	/// </summary>
	internal void Update(double windowWidth)
	{
		if (Math.Abs(_lastWidth - windowWidth) < 0.5)
			return;

		_lastWidth = windowWidth;
		double raw = windowWidth / ReferenceWidth;
		double newScale = Math.Clamp(raw, MinScale, MaxScale);

		if (Math.Abs(_scale - newScale) < 0.001)
			return;

		_scale = newScale;
		ScaleChanged?.Invoke();
	}

	/// <summary>Scales a design-time horizontal value by the current factor.</summary>
	internal double H(double designValue)
		=> Math.Round(designValue * _scale);

	/// <summary>Scales a design-time horizontal value, guaranteeing a minimum result.</summary>
	internal double H(double designValue, double minimum)
		=> Math.Max(minimum, Math.Round(designValue * _scale));

	public void Dispose()
	{
		ScaleChanged = null;
	}
}
