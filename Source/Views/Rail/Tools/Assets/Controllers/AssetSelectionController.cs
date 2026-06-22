namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

internal sealed class AssetSelectionController
{
	private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
	private string? _anchorPath;

	internal int Count => _selectedPaths.Count;
	internal IReadOnlyCollection<string> Paths => _selectedPaths;

	internal bool Contains(string path)
		=> _selectedPaths.Contains(path);

	internal bool IsSelected(string path)
		=> _selectedPaths.Contains(path);

	internal void Clear()
	{
		_selectedPaths.Clear();
		_anchorPath = null;
	}

	internal void ReplaceAll(IEnumerable<string> paths, string? anchorPath = null)
	{
		_selectedPaths.Clear();
		foreach (string path in paths)
		{
			_selectedPaths.Add(path);
		}

		_anchorPath = !string.IsNullOrWhiteSpace(anchorPath)
			? anchorPath
			: _selectedPaths.LastOrDefault();
	}

	internal void ReplaceWithSingle(string path)
	{
		_selectedPaths.Clear();
		_selectedPaths.Add(path);
		_anchorPath = path;
	}

	internal void EnsureAnchor(string path)
	{
		_anchorPath ??= path;
	}

	internal bool IsSingleSelection(string path)
		=> _selectedPaths.Count == 1 && _selectedPaths.Contains(path);

	internal string? GetPrimarySelectedPath()
	{
		if (!string.IsNullOrWhiteSpace(_anchorPath) && _selectedPaths.Contains(_anchorPath))
		{
			return _anchorPath;
		}

		return _selectedPaths.LastOrDefault();
	}

	internal List<string> GetExistingPaths()
	{
		return _selectedPaths
			.Where(path => File.Exists(path) || Directory.Exists(path))
			.ToList();
	}

	internal void Normalize()
	{
		_selectedPaths.RemoveWhere(path => !File.Exists(path) && !Directory.Exists(path));
		if (!string.IsNullOrWhiteSpace(_anchorPath) && !_selectedPaths.Contains(_anchorPath))
		{
			_anchorPath = _selectedPaths.LastOrDefault();
		}
	}

	internal void SelectSingle(string path, Action<string> deselect, Action<string> select)
	{
		if (IsSingleSelection(path))
		{
			return;
		}

		foreach (string selectedPath in _selectedPaths.ToArray())
		{
			deselect(selectedPath);
		}

		ReplaceWithSingle(path);
		select(path);
	}

	internal void Toggle(string path, Action<string> deselect, Action<string> select)
	{
		if (_selectedPaths.Remove(path))
		{
			if (string.Equals(_anchorPath, path, StringComparison.OrdinalIgnoreCase))
			{
				_anchorPath = _selectedPaths.LastOrDefault();
			}

			deselect(path);
			return;
		}

		_selectedPaths.Add(path);
		_anchorPath ??= path;
		select(path);
	}

	internal void SelectRange(string targetPath, IReadOnlyList<string> visiblePaths, Action<string> deselect, Action<string> select)
	{
		string anchorPath = _anchorPath ?? _selectedPaths.LastOrDefault() ?? targetPath;
		int anchorIndex = visiblePaths
			.Select((path, index) => (path, index))
			.FirstOrDefault(x => string.Equals(x.path, anchorPath, StringComparison.OrdinalIgnoreCase))
			.index;
		int targetIndex = visiblePaths
			.Select((path, index) => (path, index))
			.FirstOrDefault(x => string.Equals(x.path, targetPath, StringComparison.OrdinalIgnoreCase))
			.index;

		bool anchorExists = visiblePaths.Any(path => string.Equals(path, anchorPath, StringComparison.OrdinalIgnoreCase));
		bool targetExists = visiblePaths.Any(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase));
		if (!anchorExists || !targetExists)
		{
			SelectSingle(targetPath, deselect, select);
			return;
		}

		foreach (string selectedPath in _selectedPaths.ToArray())
		{
			deselect(selectedPath);
		}

		int start = Math.Min(anchorIndex, targetIndex);
		int end = Math.Max(anchorIndex, targetIndex);
		var replacement = new List<string>(end - start + 1);
		for (int i = start; i <= end; i++)
		{
			string path = visiblePaths[i];
			replacement.Add(path);
			select(path);
		}

		ReplaceAll(replacement, anchorPath);
	}
}
