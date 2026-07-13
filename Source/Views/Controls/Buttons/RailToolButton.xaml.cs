using System;
using System.Threading;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views.Controls.Buttons;

public partial class RailToolButton : ContentView
{
	private static readonly Color TransparentBackgroundColor = Color.FromRgba(0, 0, 0, 0.01);

	public static readonly BindableProperty IconSourceProperty =
		BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(RailToolButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty ActiveIconSourceProperty =
		BindableProperty.Create(nameof(ActiveIconSource), typeof(ImageSource), typeof(RailToolButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty IsSelectedProperty =
		BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(RailToolButton), false, propertyChanged: OnIsSelectedChanged);

	public static readonly BindableProperty TooltipTextProperty =
		BindableProperty.Create(nameof(TooltipText), typeof(string), typeof(RailToolButton), string.Empty, propertyChanged: OnTooltipTextChanged);

	public static readonly BindableProperty AccentColorProperty =
		BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(RailToolButton), NexusColors.Accent, propertyChanged: OnAccentColorChanged);

	public event EventHandler? Clicked;

	private int _tapAnimationVersion;
	private bool _isPointerOver;

	public ImageSource? IconSource
	{
		get => (ImageSource?)GetValue(IconSourceProperty);
		set => SetValue(IconSourceProperty, value);
	}

	public ImageSource? ActiveIconSource
	{
		get => (ImageSource?)GetValue(ActiveIconSourceProperty);
		set => SetValue(ActiveIconSourceProperty, value);
	}

	public bool IsSelected
	{
		get => (bool)GetValue(IsSelectedProperty);
		set => SetValue(IsSelectedProperty, value);
	}

	public string TooltipText
	{
		get => (string)GetValue(TooltipTextProperty);
		set => SetValue(TooltipTextProperty, value);
	}

	public Color AccentColor
	{
		get => (Color)GetValue(AccentColorProperty);
		set => SetValue(AccentColorProperty, value);
	}

	public RailToolButton()
	{
		InitializeComponent();

		var tap = new TapGestureRecognizer();
		tap.Tapped += (s, e) => OnTapped();
		GestureRecognizers.Add(tap);

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) => OnPointerEntered();
		pointer.PointerExited += (s, e) => OnPointerExited();
		GestureRecognizers.Add(pointer);

		ApplyAccentVisual();
		ApplyTooltip();
		ApplyInitialStates();
	}

	private async void OnTapped()
	{
		int animationVersion = Interlocked.Increment(ref _tapAnimationVersion);
		Clicked?.Invoke(this, EventArgs.Empty);

		GlowSurface.AbortAnimation("FadeTo");

		await GlowSurface.FadeToAsync(0.7, 70, Easing.CubicOut);
		await GlowSurface.FadeToAsync(_isPointerOver ? 0.62 : 0, 120, Easing.CubicOut);

		if (animationVersion == _tapAnimationVersion)
		{
			ApplyGlowState(true);
		}
	}

	private void OnPointerEntered()
	{
		_isPointerOver = true;
		ApplyGlowState(true);
	}

	private void OnPointerExited()
	{
		_isPointerOver = false;
		ApplyGlowState(true);
	}

	private static void OnIsSelectedChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToolButton button)
		{
			button.ApplySelectionState();
		}
	}

	private static void OnIconSourceChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToolButton button)
		{
			button.ApplyIconSource();
		}
	}

	private static void OnTooltipTextChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToolButton button)
		{
			button.ApplyTooltip();
		}
	}

	private static void OnAccentColorChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToolButton button)
		{
			button.ApplyAccentVisual();
		}
	}

	private void ApplySelectionState(bool animate = true)
	{
		// Reset hover on selection change to prevent stuck states during tool switches
		_isPointerOver = false;
		ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		ApplyIconSource();
		ApplyGlowState(animate);

		ApplyActiveEdgeLineState();
	}

	private void ApplyGlowState(bool animate = true)
	{
		double targetSurfaceOpacity = _isPointerOver ? 0.62 : 0;

		if (animate)
		{
			_ = GlowSurface.FadeToAsync(targetSurfaceOpacity, 130, Easing.CubicOut);
		}
		else
		{
			GlowSurface.Opacity = targetSurfaceOpacity;
		}
	}

	private void ApplyActiveEdgeLineState()
	{
		bool showLine = IsSelected;
		ActiveEdgeLine.Opacity = showLine ? 1 : 0;
		ActiveEdgeLine.HeightRequest = showLine ? 24 : 0;
	}

	private void ApplyAccentVisual()
	{
		GlowSurface.BackgroundColor = AccentColor.WithAlpha(0.5f);
		NormalIconImage.ClearValue(ShadowProperty);
		ActiveIconImage.ClearValue(ShadowProperty);
		ActiveEdgeLine.BackgroundColor = AccentColor;
	}

	private void ApplyIconSource()
	{
		RailButtonVisuals.ApplyIconPair(NormalIconImage, ActiveIconImage, IsSelected, ActiveIconSource);
	}

	private void ApplyTooltip()
	{
		RailButtonVisuals.ApplyTooltip(this, TooltipText);
	}

	private void ApplyInitialStates()
	{
		ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		ApplyIconSource();
		ApplySelectionState(animate: false);
	}
}
