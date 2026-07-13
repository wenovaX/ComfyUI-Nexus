namespace ComfyUI_Nexus.AssetHub;

internal sealed class AssetHubSelectionState
{
	private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyCollection<string> Paths => _paths;

	public string? LastFocusedPath { get; private set; }

	public int Count => _paths.Count;

	public void Clear(bool resetFocus = false)
	{
		_paths.Clear();
		if (resetFocus)
		{
			LastFocusedPath = null;
		}
	}

	public bool Contains(string path) => _paths.Contains(path);

	public void SetSingle(string path)
	{
		_paths.Clear();
		if (!string.IsNullOrWhiteSpace(path))
		{
			_paths.Add(path);
			LastFocusedPath = path;
		}
	}

	public void Toggle(string path)
	{
		if (_paths.Contains(path))
		{
			_paths.Remove(path);
		}
		else if (!string.IsNullOrWhiteSpace(path))
		{
			_paths.Add(path);
			LastFocusedPath = path;
		}
	}

	public void SelectAll(IEnumerable<string> paths)
	{
		_paths.Clear();
		foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
		{
			_paths.Add(path);
		}
	}
}
