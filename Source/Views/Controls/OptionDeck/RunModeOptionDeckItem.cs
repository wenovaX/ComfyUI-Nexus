using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views.Controls.OptionDeck;

/// <summary>
/// Display item for the run-mode option deck.
/// </summary>
/// <param name="Value">Stable value emitted when the option is selected.</param>
/// <param name="Text">Visible label shown in the deck.</param>
/// <param name="Icon">Icon source shown before the label.</param>
/// <param name="TextColor">Text color for this option.</param>
internal sealed record RunModeOptionDeckItem(
	string Value,
	string Text,
	ImageSource Icon,
	Color TextColor);
