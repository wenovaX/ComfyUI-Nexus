namespace ComfyUI_Nexus.AssetHub;

internal sealed class AssetHubPathHistory
{
	private readonly int _maxEntries;
	private readonly List<string> _entries = new();
	private int _index = -1;

	public AssetHubPathHistory(int maxEntries = 50)
	{
		_maxEntries = Math.Max(1, maxEntries);
	}

	public IReadOnlyList<string> Entries => _entries;

	public void Record(string currentPath, string nextPath)
	{
		if (string.IsNullOrWhiteSpace(nextPath) ||
			string.Equals(currentPath, nextPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (_index < _entries.Count - 1)
		{
			_entries.RemoveRange(_index + 1, _entries.Count - (_index + 1));
		}

		_entries.Add(nextPath);
		if (_entries.Count > _maxEntries)
		{
			_entries.RemoveAt(0);
		}

		_index = _entries.Count - 1;
	}

	public string? Back()
	{
		if (_index <= 0)
		{
			return null;
		}

		_index--;
		return _entries[_index];
	}

	public string? Forward()
	{
		if (_index >= _entries.Count - 1)
		{
			return null;
		}

		_index++;
		return _entries[_index];
	}
}
