using System.Text.Json;
using ComfyUI_Nexus.Setup.Services;

namespace ComfyUI_Nexus.AssetHub;

internal sealed class AssetHubBookmarkStore
{
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
	private readonly string _filePath;

	public AssetHubBookmarkStore(string? filePath = null)
	{
		_filePath = filePath ?? BuildDefaultPath();
	}

	public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
	{
		if (!File.Exists(_filePath))
		{
			return Array.Empty<string>();
		}

		await using var stream = File.OpenRead(_filePath);
		var payload = await JsonSerializer.DeserializeAsync<AssetHubBookmarkPayload>(stream, cancellationToken: cancellationToken);
		return payload?.Bookmarks?
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray()
			?? Array.Empty<string>();
	}

	public async Task SaveAsync(IEnumerable<string> bookmarks, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

		var payload = new AssetHubBookmarkPayload
		{
			Bookmarks = bookmarks
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.ToArray(),
		};

		await using var stream = File.Create(_filePath);
		await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
	}

	private static string BuildDefaultPath()
		=> ComfyInstallService.GetLocalRuntimePath("State/asset-hub-bookmarks.json");

	private sealed class AssetHubBookmarkPayload
	{
		public string[] Bookmarks { get; set; } = Array.Empty<string>();
	}
}
