namespace ComfyUI_Nexus;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;

public partial class AppShell : Shell
{
	public AppShell()
	{
		NexusLog.Info("[STARTUP] AppShell constructor starting.");
		try
		{
			InitializeComponent();
			Title = SafeAppInfo.DisplayName;
			NexusLog.Info("[STARTUP] AppShell InitializeComponent completed.");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] AppShell InitializeComponent failed");
			throw;
		}
	}
}
