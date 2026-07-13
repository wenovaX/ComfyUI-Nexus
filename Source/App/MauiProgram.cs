using Microsoft.Extensions.Logging;

namespace ComfyUI_Nexus;

#if WINDOWS
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
#endif
public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				fonts.AddFont("JetBrainsMono-Regular.ttf", "JetBrainsMono");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
