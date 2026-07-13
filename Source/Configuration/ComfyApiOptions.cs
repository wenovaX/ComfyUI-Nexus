namespace ComfyUI_Nexus.Configuration;

using ComfyUI_Nexus.Setup.Services;

internal static class ComfyApiOptions
{
	internal const string DefaultListenAddress = "127.0.0.1";
	internal const int DefaultPort = 8188;
	internal const string GlobalSubgraphsPath = "/api/global_subgraphs";
	internal const string GlobalSubgraphDetailsPathPrefix = "/api/global_subgraphs/";

	internal static string LocalBaseUrl => BuildLocalBaseUrl();
	internal static int LocalPort => SetupSettingsService.Instance.Settings.ServerPort;
	internal static string ObjectInfoUrl => $"{LocalBaseUrl}/api/object_info";
	internal static string WorkflowProbeUrl => $"{LocalBaseUrl}/userdata?dir=workflows&recurse=false&full_info=true";

	internal static string ModelCategoryUrl(string category)
		=> $"{LocalBaseUrl}/api/experiment/models/{Uri.EscapeDataString(category)}";

	private static string BuildLocalBaseUrl()
	{
		var settings = SetupSettingsService.Instance.Settings;
		string host = NormalizeClientHost(settings.ListenAddress);
		int port = settings.ServerPort is > 0 and <= 65535 ? settings.ServerPort : DefaultPort;

		return $"http://{host}:{port}";
	}

	private static string NormalizeClientHost(string? listenAddress)
	{
		if (string.IsNullOrWhiteSpace(listenAddress)) return DefaultListenAddress;

		string normalized = listenAddress.Trim();
		return normalized is "0.0.0.0" or "::" or "*" ? DefaultListenAddress : normalized;
	}
}
