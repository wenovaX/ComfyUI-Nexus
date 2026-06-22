namespace ComfyUI_Nexus.Help;

using System.Text.Json.Serialization;

// JSON-bound properties stay public for System.Text.Json; the models themselves remain assembly-local.
internal sealed class HelpContent
{
	internal string DisplayTitle { get; set; } = string.Empty;
	internal string DisplaySubtitle { get; set; } = string.Empty;

	[JsonPropertyName("title")]
	public LocalizedHelpText Title { get; init; } = new();

	[JsonPropertyName("subtitle")]
	public LocalizedHelpText Subtitle { get; init; } = new();

	[JsonPropertyName("sections")]
	public IReadOnlyList<HelpSection> Sections { get; init; } = [];
}

internal sealed class HelpSection
{
	internal string DisplayTitle { get; set; } = string.Empty;
	internal string DisplayDescription { get; set; } = string.Empty;

	[JsonPropertyName("title")]
	public LocalizedHelpText Title { get; init; } = new();

	[JsonPropertyName("description")]
	public LocalizedHelpText Description { get; init; } = new();

	[JsonPropertyName("items")]
	public IReadOnlyList<HelpItem> Items { get; init; } = [];
}

internal sealed class HelpItem
{
	internal string DisplayTitle { get; set; } = string.Empty;
	internal string DisplayHint { get; set; } = string.Empty;

	[JsonPropertyName("title")]
	public LocalizedHelpText Title { get; init; } = new();

	[JsonPropertyName("body")]
	public string Body { get; set; } = string.Empty;

	[JsonPropertyName("slug")]
	public string Slug { get; init; } = string.Empty;

	[JsonPropertyName("accentColor")]
	public string AccentColor { get; init; } = string.Empty;

	[JsonPropertyName("hint")]
	public LocalizedHelpText Hint { get; init; } = new();
}

internal sealed class LocalizedHelpText : Dictionary<string, string>
{
}
