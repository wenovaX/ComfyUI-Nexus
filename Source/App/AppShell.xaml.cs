namespace ComfyUI_Nexus;

using ComfyUI_Nexus.Diagnostics;

public partial class AppShell : Shell
{
	public AppShell()
	{
		NexusLog.Info("[STARTUP] AppShell constructor starting.");
		try
		{
			InitializeComponent();
			NexusLog.Info("[STARTUP] AppShell InitializeComponent completed.");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] AppShell InitializeComponent failed");
			throw;
		}
	}
}
