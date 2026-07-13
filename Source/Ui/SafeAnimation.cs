using System.Runtime.InteropServices;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

internal static class SafeAnimation
{
	private const double DefaultSnapThreshold = 0.001;

	internal static Task FadeToAsync(VisualElement element, double opacity, uint length, Easing? easing = null, string source = "ANIMATION")
		=> RunAsync(() => element.FadeToAsync(opacity, length, easing), source);

	internal static Task<bool> TryFadeToAsync(VisualElement element, double opacity, uint length, Easing? easing = null, string source = "ANIMATION")
		=> RunAsync(() => element.FadeToAsync(opacity, length, easing), source);

	internal static Task ScaleToAsync(VisualElement element, double scale, uint length, Easing? easing = null, string source = "ANIMATION")
		=> XamlLifetimeDiagnostics.AreTransformAnimationsDisabled
			? ApplyTransformImmediately(element, () => element.Scale = scale, source)
			: RunAsync(() => element.ScaleToAsync(scale, length, easing), source);

	internal static Task ScaleXToAsync(VisualElement element, double scale, uint length, Easing? easing = null, string source = "ANIMATION")
		=> XamlLifetimeDiagnostics.AreTransformAnimationsDisabled
			? ApplyTransformImmediately(element, () => element.ScaleX = scale, source)
			: RunAsync(() => element.ScaleXToAsync(scale, length, easing), source);

	internal static Task ScaleYToAsync(VisualElement element, double scale, uint length, Easing? easing = null, string source = "ANIMATION")
		=> XamlLifetimeDiagnostics.AreTransformAnimationsDisabled
			? ApplyTransformImmediately(element, () => element.ScaleY = scale, source)
			: RunAsync(() => element.ScaleYToAsync(scale, length, easing), source);

	internal static Task TranslateToAsync(VisualElement element, double x, double y, uint length, Easing? easing = null, string source = "ANIMATION")
		=> XamlLifetimeDiagnostics.AreTransformAnimationsDisabled
			? ApplyTransformImmediately(element, () =>
			{
				element.TranslationX = x;
				element.TranslationY = y;
			}, source)
			: RunAsync(() => element.TranslateToAsync(x, y, length, easing), source);

	internal static Task RotateToAsync(VisualElement element, double rotation, uint length, Easing? easing = null, string source = "ANIMATION")
		=> XamlLifetimeDiagnostics.AreTransformAnimationsDisabled
			? ApplyTransformImmediately(element, () => element.Rotation = rotation, source)
			: RunAsync(() => element.RotateToAsync(rotation, length, easing), source);

	internal static Task<bool> FadeTranslateScaleToAsync(
		VisualElement element,
		string name,
		double opacity,
		double translationY,
		double scale,
		uint length,
		Easing? easing = null,
		string source = "ANIMATION")
	{
		AbortAnimation(element, name, source);
		if (XamlLifetimeDiagnostics.AreTransformAnimationsDisabled)
		{
			element.Opacity = opacity;
			element.TranslationY = translationY;
			element.Scale = scale;
			return Task.FromResult(true);
		}

		return TimelineAsync(
			element,
			name,
			16,
			length,
			easing: null,
			repeat: null,
			source,
			new TimelineSegment(0, 1, value => element.Opacity = value, element.Opacity, opacity, easing),
			new TimelineSegment(0, 1, value => element.TranslationY = value, element.TranslationY, translationY, easing),
			new TimelineSegment(0, 1, value => element.Scale = value, element.Scale, scale, easing));
	}

	internal static Task<bool> FadeTranslateScaleYToAsync(
		VisualElement element,
		string name,
		double opacity,
		double translationY,
		double scaleY,
		uint length,
		Easing? easing = null,
		string source = "ANIMATION")
	{
		AbortAnimation(element, name, source);
		if (XamlLifetimeDiagnostics.AreTransformAnimationsDisabled)
		{
			element.Opacity = opacity;
			element.TranslationY = translationY;
			element.ScaleY = scaleY;
			return Task.FromResult(true);
		}

		return TimelineAsync(
			element,
			name,
			16,
			length,
			easing: null,
			repeat: null,
			source,
			new TimelineSegment(0, 1, value => element.Opacity = value, element.Opacity, opacity, easing),
			new TimelineSegment(0, 1, value => element.TranslationY = value, element.TranslationY, translationY, easing),
			new TimelineSegment(0, 1, value => element.ScaleY = value, element.ScaleY, scaleY, easing));
	}

	internal static void CancelAnimations(VisualElement element, string source = "ANIMATION")
		=> Run(() => element.CancelAnimations(), source);

	internal static void CancelAnimations(string source, params VisualElement?[] elements)
	{
		foreach (VisualElement? element in elements)
		{
			if (element is not null)
			{
				CancelAnimations(element, source);
			}
		}
	}

	internal static void AbortAnimation(IAnimatable animatable, string animationName, string source = "ANIMATION")
	{
		XamlLifetimeDiagnostics.RemoveAnimation(animatable, animationName);
		Run(() => animatable.AbortAnimation(animationName), source);
	}

	internal static bool Tween(
		IAnimatable owner,
		string name,
		Action<double> apply,
		double from,
		double to,
		uint rate = 16,
		uint length = 250,
		Easing? easing = null,
		Action<double, bool>? finished = null,
		Func<bool>? repeat = null,
		string source = "ANIMATION")
	{
		var animation = new Animation(value => ApplySafely(owner, name, apply, value, source), from, to, easing);
		return Commit(animation, owner, name, rate, length, easing: null, finished, repeat, source);
	}

	internal static Task<bool> TweenAsync(
		IAnimatable owner,
		string name,
		Action<double> apply,
		double from,
		double to,
		uint rate = 16,
		uint length = 250,
		Easing? easing = null,
		Func<bool>? repeat = null,
		string source = "ANIMATION")
	{
		var animation = new Animation(value => ApplySafely(owner, name, apply, value, source), from, to, easing);
		return CommitAsync(animation, owner, name, rate, length, easing: null, repeat: repeat, source: source);
	}

	internal static bool TweenTo(
		IAnimatable owner,
		string name,
		Func<double> getCurrent,
		Action<double> apply,
		double target,
		uint rate = 16,
		uint length = 250,
		Easing? easing = null,
		double snapThreshold = DefaultSnapThreshold,
		string source = "ANIMATION")
	{
		double current;
		try
		{
			current = getCurrent();
		}
		catch (Exception ex) when (IsShutdownSafe(ex))
		{
			NexusLog.Trace($"[{source}] Animation read skipped during shutdown: {ex.Message}");
			return false;
		}

		if (Math.Abs(current - target) <= snapThreshold)
		{
			return Run(() => apply(target), source);
		}

		AbortAnimation(owner, name, source);
		return Tween(owner, name, apply, current, target, rate, length, easing, source: source);
	}

	internal static Task<bool> TweenToAsync(
		IAnimatable owner,
		string name,
		Func<double> getCurrent,
		Action<double> apply,
		double target,
		uint rate = 16,
		uint length = 250,
		Easing? easing = null,
		double snapThreshold = DefaultSnapThreshold,
		string source = "ANIMATION")
	{
		double current;
		try
		{
			current = getCurrent();
		}
		catch (Exception ex) when (IsShutdownSafe(ex))
		{
			NexusLog.Trace($"[{source}] Animation read skipped during shutdown: {ex.Message}");
			return Task.FromResult(false);
		}

		if (Math.Abs(current - target) <= snapThreshold)
		{
			return Task.FromResult(Run(() => apply(target), source));
		}

		AbortAnimation(owner, name, source);
		return TweenAsync(owner, name, apply, current, target, rate, length, easing, source: source);
	}

	internal static Animation Composite(params TimelineSegment[] segments)
	{
		var animation = new Animation();
		foreach (var segment in segments)
		{
			animation.Add(segment.Begin, segment.End, new Animation(segment.Apply, segment.From, segment.To, segment.Easing));
		}

		return animation;
	}

	internal static bool Timeline(
		IAnimatable owner,
		string name,
		uint rate,
		uint length,
		Easing? easing = null,
		Func<bool>? repeat = null,
		string source = "ANIMATION",
		params TimelineSegment[] segments)
	{
		var animation = CompositeSafely(owner, name, source, segments);
		return Commit(animation, owner, name, rate, length, easing, repeat: repeat, source: source);
	}

	internal static Task<bool> TimelineAsync(
		IAnimatable owner,
		string name,
		uint rate,
		uint length,
		Easing? easing = null,
		Func<bool>? repeat = null,
		string source = "ANIMATION",
		params TimelineSegment[] segments)
	{
		var animation = CompositeSafely(owner, name, source, segments);
		return CommitAsync(animation, owner, name, rate, length, easing, repeat: repeat, source: source);
	}

	internal static bool Commit(
		Animation animation,
		IAnimatable owner,
		string name,
		uint rate = 16,
		uint length = 250,
		Easing? easing = null,
		Action<double, bool>? finished = null,
		Func<bool>? repeat = null,
		string source = "ANIMATION")
	{
		Action<double, bool>? safeFinished = finished is null
			? null
			: (value, wasCancelled) => Run(() => finished(value, wasCancelled), source);
		Func<bool>? safeRepeat = repeat is null
			? null
			: () => RepeatSafely(repeat, source);

		Action<double, bool> trackedFinished = (value, wasCancelled) =>
		{
			XamlLifetimeDiagnostics.RemoveAnimation(owner, name);
			safeFinished?.Invoke(value, wasCancelled);
		};
		XamlLifetimeDiagnostics.RegisterAnimation(owner, name);
		bool committed = Run(
			() => animation.Commit(owner, name, rate, length, easing, trackedFinished, safeRepeat),
			source);
		if (!committed)
		{
			XamlLifetimeDiagnostics.RemoveAnimation(owner, name);
		}

		return committed;
	}

	internal static Task<bool> CommitAsync(
		Animation animation,
		IAnimatable owner,
		string name,
		uint rate = 16,
		uint length = 250,
		Easing? easing = null,
		Func<bool>? repeat = null,
		string source = "ANIMATION")
	{
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool committed = Commit(
			animation,
			owner,
			name,
			rate,
			length,
			easing,
			(_, wasCancelled) => completion.TrySetResult(!wasCancelled),
			repeat,
			source);

		if (!committed)
		{
			completion.TrySetResult(false);
		}

		return completion.Task;
	}

	private static Animation CompositeSafely(
		IAnimatable owner,
		string name,
		string source,
		params TimelineSegment[] segments)
	{
		var animation = new Animation();
		foreach (var segment in segments)
		{
			animation.Add(
				segment.Begin,
				segment.End,
				new Animation(
					value => ApplySafely(owner, name, segment.Apply, value, source),
					segment.From,
					segment.To,
					segment.Easing));
		}

		return animation;
	}

	private static void ApplySafely(
		IAnimatable owner,
		string name,
		Action<double> apply,
		double value,
		string source)
	{
		try
		{
			apply(value);
		}
		catch (Exception ex) when (IsShutdownSafe(ex))
		{
			NexusLog.Trace($"[{source}] Animation callback skipped during shutdown: {ex.Message}");
			AbortAnimation(owner, name, source);
		}
	}

	private static bool RepeatSafely(Func<bool> repeat, string source)
	{
		try
		{
			return repeat();
		}
		catch (Exception ex) when (IsShutdownSafe(ex))
		{
			NexusLog.Trace($"[{source}] Animation repeat stopped during shutdown: {ex.Message}");
			return false;
		}
	}

	private static async Task<bool> RunAsync(Func<Task> action, string source)
	{
		try
		{
			await action();
			return true;
		}
		catch (Exception ex) when (IsShutdownSafe(ex))
		{
			NexusLog.Trace($"[{source}] Animation skipped during shutdown: {ex.Message}");
			return false;
		}
	}

	private static Task ApplyTransformImmediately(VisualElement element, Action apply, string source)
	{
		CancelAnimations(element, source);
		return Task.FromResult(Run(apply, source));
	}

	private static bool Run(Action action, string source)
	{
		try
		{
			action();
			return true;
		}
		catch (Exception ex) when (IsShutdownSafe(ex))
		{
			NexusLog.Trace($"[{source}] Animation update skipped during shutdown: {ex.Message}");
			return false;
		}
	}

	private static bool IsShutdownSafe(Exception ex)
		=> ex is ObjectDisposedException
			or InvalidOperationException
			or COMException
			or TaskCanceledException
			or OperationCanceledException;

	internal readonly record struct TimelineSegment(
		double Begin,
		double End,
		Action<double> Apply,
		double From,
		double To,
		Easing? Easing = null);
}
