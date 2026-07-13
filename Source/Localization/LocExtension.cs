namespace ComfyUI_Nexus.Localization;

[ContentProperty(nameof(Key))]
public sealed class LocExtension : IMarkupExtension<string>
{
	public string Key { get; set; } = string.Empty;

	public string ProvideValue(IServiceProvider serviceProvider)
		=> LocalizationManager.Text(Key);

	object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
		=> ProvideValue(serviceProvider);
}
