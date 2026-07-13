using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views.Controls.Buttons;

public partial class RailIconButton : ContentView
{
	private static readonly Color TransparentBackgroundColor = Color.FromRgba(0, 0, 0, 0.01);

	public static readonly BindableProperty IconSourceProperty =
		BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(RailIconButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty ActiveIconSourceProperty =
		BindableProperty.Create(nameof(ActiveIconSource), typeof(ImageSource), typeof(RailIconButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty TooltipTextProperty =
		BindableProperty.Create(nameof(TooltipText), typeof(string), typeof(RailIconButton), string.Empty, propertyChanged: OnTooltipTextChanged);

	public static readonly BindableProperty IdleOpacityProperty =
		BindableProperty.Create(nameof(IdleOpacity), typeof(double), typeof(RailIconButton), 1.0, propertyChanged: OnIdleOpacityChanged);

	public static readonly BindableProperty IsSelectedProperty =
		BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(RailIconButton), false, propertyChanged: OnIsSelectedChanged);

	public static readonly BindableProperty CornerRadiusProperty =
		BindableProperty.Create(nameof(CornerRadius), typeof(CornerRadius), typeof(RailIconButton), new CornerRadius(9));

	public event EventHandler? Clicked;

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

	public string TooltipText
	{
		get => (string)GetValue(TooltipTextProperty);
		set => SetValue(TooltipTextProperty, value);
	}

	public double IdleOpacity
	{
		get => (double)GetValue(IdleOpacityProperty);
		set => SetValue(IdleOpacityProperty, value);
	}

	public bool IsSelected
	{
		get => (bool)GetValue(IsSelectedProperty);
		set => SetValue(IsSelectedProperty, value);
	}

	public CornerRadius CornerRadius
	{
		get => (CornerRadius)GetValue(CornerRadiusProperty);
		set => SetValue(CornerRadiusProperty, value);
	}

	public RailIconButton()
	{
		InitializeComponent();

		var tap = new TapGestureRecognizer();
		tap.Tapped += (s, e) => OnTapped();
		GestureRecognizers.Add(tap);

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) => OnPointerEntered();
		pointer.PointerExited += (s, e) => OnPointerExited();
		GestureRecognizers.Add(pointer);

		ApplyTooltip();
		Opacity = IdleOpacity;
		ApplySelectionState(animate: false);
	}

	private async void OnTapped()
	{
		Clicked?.Invoke(this, EventArgs.Empty);

		await GlowSurface.FadeToAsync(0.7, 70, Easing.CubicOut);
		await GlowSurface.FadeToAsync(IsSelected ? 0.16 : 0, 120, Easing.CubicOut);
	}

	private async void OnPointerEntered()
	{
		ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		await Task.WhenAll(
			this.FadeToAsync(1.0, 130, Easing.CubicOut),
			GlowSurface.FadeToAsync(0.62, 130, Easing.CubicOut));
	}

	private async void OnPointerExited()
	{
		ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		await Task.WhenAll(
			this.FadeToAsync(IdleOpacity, 130, Easing.CubicIn),
			GlowSurface.FadeToAsync(0, 130, Easing.CubicIn));
	}

	private static void OnIdleOpacityChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailIconButton button)
		{
			button.Opacity = (double)newValue;
		}
	}

	private static void OnTooltipTextChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailIconButton button)
		{
			button.ApplyTooltip();
		}
	}

	private static void OnIconSourceChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailIconButton button)
		{
			button.ApplyIconSource();
		}
	}

	private static void OnIsSelectedChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailIconButton button)
		{
			button.ApplySelectionState(animate: true);
		}
	}

	private void ApplyTooltip()
	{
		RailButtonVisuals.ApplyTooltip(this, TooltipText);
	}

	private void ApplySelectionState(bool animate)
	{
		ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		ApplyIconSource();

		double targetGlowOpacity = 0;

		if (!animate)
		{
			GlowSurface.Opacity = targetGlowOpacity;
			return;
		}

		_ = GlowSurface.FadeToAsync(targetGlowOpacity, 130, Easing.CubicOut);
	}

	private void ApplyIconSource()
	{
		RailButtonVisuals.ApplyIconPair(NormalIconImage, ActiveIconImage, IsSelected, ActiveIconSource);
	}
}
