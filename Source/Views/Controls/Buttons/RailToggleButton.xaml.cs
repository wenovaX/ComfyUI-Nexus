using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views.Controls.Buttons;

public partial class RailToggleButton : ContentView
{
	private static readonly Color TransparentBackgroundColor = Color.FromRgba(0, 0, 0, 0.01);

	public static readonly BindableProperty IconSourceProperty =
		BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(RailToggleButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty ActiveIconSourceProperty =
		BindableProperty.Create(nameof(ActiveIconSource), typeof(ImageSource), typeof(RailToggleButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty TooltipTextProperty =
		BindableProperty.Create(nameof(TooltipText), typeof(string), typeof(RailToggleButton), string.Empty, propertyChanged: OnTooltipTextChanged);

	public static readonly BindableProperty IsSelectedProperty =
		BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(RailToggleButton), false, propertyChanged: OnIsSelectedChanged);

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

	public bool IsSelected
	{
		get => (bool)GetValue(IsSelectedProperty);
		set => SetValue(IsSelectedProperty, value);
	}

	public RailToggleButton()
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
		UpdateVisualState(false);
	}

	private async void OnTapped()
	{
		Clicked?.Invoke(this, EventArgs.Empty);

		await GlowSurface.FadeToAsync(0.7, 70, Easing.CubicOut);
		await GlowSurface.FadeToAsync(IsSelected ? 0.16 : 0, 120, Easing.CubicOut);
	}

	private async void OnPointerEntered()
	{
		if (!IsSelected)
		{
			ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		}

		await GlowSurface.FadeToAsync(0.62, 130, Easing.CubicOut);
	}

	private async void OnPointerExited()
	{
		if (!IsSelected)
		{
			ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		}

		await GlowSurface.FadeToAsync(0, 130, Easing.CubicIn);
	}

	private static void OnIsSelectedChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToggleButton button)
		{
			button.UpdateVisualState(true);
		}
	}

	private void UpdateVisualState(bool animate)
	{
		double targetGlowOpacity = 0;
		ApplyIconSource();

		if (animate)
		{
			_ = GlowSurface.FadeToAsync(targetGlowOpacity, 180, Easing.CubicOut);
			ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		}
		else
		{
			GlowSurface.Opacity = targetGlowOpacity;
			ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		}
	}

	private static void OnIconSourceChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToggleButton button)
		{
			button.ApplyIconSource();
		}
	}

	private static void OnTooltipTextChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToggleButton button)
		{
			button.ApplyTooltip();
		}
	}

	private void ApplyTooltip()
	{
		RailButtonVisuals.ApplyTooltip(this, TooltipText);
	}

	private void ApplyIconSource()
	{
		RailButtonVisuals.ApplyIconPair(NormalIconImage, ActiveIconImage, IsSelected, ActiveIconSource);
	}
}
