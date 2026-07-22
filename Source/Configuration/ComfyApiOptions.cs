namespace ComfyUI_Nexus.Configuration;

using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Setup.Models;

internal static class ComfyApiOptions
{
	internal const string DefaultListenAddress = "127.0.0.1";
	internal const int DefaultPort = 8188;
	internal const string GlobalSubgraphsPath = "/api/global_subgraphs";
	internal const string GlobalSubgraphDetailsPathPrefix = "/api/global_subgraphs/";

	internal static string GetLocalBaseUrl(SetupSettings settings) => BuildLocalBaseUrl(settings);
	internal static int GetLocalPort(SetupSettings settings) => settings.ServerPort;
	internal static string GetObjectInfoUrl(SetupSettings settings) => $"{GetLocalBaseUrl(settings)}/api/object_info";
	internal static string GetWorkflowProbeUrl(SetupSettings settings) => $"{GetLocalBaseUrl(settings)}/userdata?dir=workflows&recurse=false&full_info=true";

	internal static string GetModelCategoryUrl(SetupSettings settings, string category)
		=> $"{GetLocalBaseUrl(settings)}/api/experiment/models/{Uri.EscapeDataString(category)}";

	private static string BuildLocalBaseUrl(SetupSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
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
