using ComfyUI_Nexus.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ComfyUI_Nexus.WinUI
{
	/// <summary>
	/// Provides application-specific behavior to supplement the default Application class.
	/// </summary>
	[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
	public partial class App : MauiWinUIApplication
	{
		/// <summary>
		/// Initializes the singleton application object.  This is the first line of authored code
		/// executed, and as such is the logical equivalent of main() or WinMain().
		/// </summary>
		public App()
		{
			this.InitializeComponent();
			UnhandledException += OnUnhandledException;
		}

		protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

		private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
			=> XamlUnhandledExceptionDiagnostics.Handle("WINUI", sender, e);
	}

}
