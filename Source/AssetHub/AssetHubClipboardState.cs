namespace ComfyUI_Nexus.AssetHub;

internal enum AssetHubClipboardOperation
{
	None,
	Copy,
	Cut,
}

internal sealed class AssetHubClipboardState
{
	private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

	public AssetHubClipboardOperation Operation { get; private set; } = AssetHubClipboardOperation.None;

	public IReadOnlyCollection<string> Paths => _paths;

	public bool HasEntries => _paths.Count > 0 && Operation != AssetHubClipboardOperation.None;

	public void SetCopy(IEnumerable<string> paths) => Replace(AssetHubClipboardOperation.Copy, paths);

	public void SetCut(IEnumerable<string> paths) => Replace(AssetHubClipboardOperation.Cut, paths);

	public void Clear()
	{
		Operation = AssetHubClipboardOperation.None;
		_paths.Clear();
	}

	public bool Contains(string path) => _paths.Contains(path);

	private void Replace(AssetHubClipboardOperation operation, IEnumerable<string> paths)
	{
		Operation = operation;
		_paths.Clear();

		foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
		{
			_paths.Add(path);
		}

		if (_paths.Count == 0)
		{
			Operation = AssetHubClipboardOperation.None;
		}
	}
}
