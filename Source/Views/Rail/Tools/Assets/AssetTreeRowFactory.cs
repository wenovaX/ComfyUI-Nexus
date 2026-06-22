namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

internal static class AssetTreeRowFactory
{
	internal static Grid Create(Action<Grid, Label> wireRow)
	{
		var row = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Star },
				new ColumnDefinition { Width = GridLength.Auto },
			},
			HeightRequest = 28,
		};

		var chevron = new Label
		{
			TextColor = Color.FromArgb("#6d8399"),
			FontSize = 11,
			VerticalOptions = LayoutOptions.Center,
			WidthRequest = 14,
			HorizontalTextAlignment = TextAlignment.Center,
			InputTransparent = true,
		};

		var iconHost = new Grid
		{
			WidthRequest = 16,
			HeightRequest = 16,
			VerticalOptions = LayoutOptions.Center,
			Margin = new Thickness(4, 0, 8, 0),
			InputTransparent = true,
		};

		var label = new Label
		{
			FontSize = 12,
			VerticalOptions = LayoutOptions.Center,
			LineBreakMode = LineBreakMode.TailTruncation,
			InputTransparent = true,
		};

		row.Add(chevron, 0, 0);
		row.Add(iconHost, 1, 0);
		row.Add(label, 2, 0);

		var countLabel = new Label
		{
			FontSize = 10,
			TextColor = Color.FromArgb("#5f7892"),
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.End,
			Margin = new Thickness(8, 0, 0, 0),
			InputTransparent = true,
			IsVisible = false,
		};
		row.Add(countLabel, 3, 0);

		wireRow(row, chevron);
		return row;
	}

	internal static void Reset(Grid row)
	{
		row.BindingContext = null;
		row.Opacity = 1;
		row.TranslationX = 0;
		row.TranslationY = 0;
		row.BackgroundColor = Colors.Transparent;
		row.ClearValue(ToolTipProperties.TextProperty);
		FlyoutBase.SetContextFlyout(row, null);
	}
}
