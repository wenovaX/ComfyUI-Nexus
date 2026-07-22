using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Services;

namespace ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

internal sealed class NodeLibraryService
{
	private const string PartnerPrefix = "api node/";
	private static readonly char[] CategoryPathSeparators = ['/', '\\'];
	private readonly SetupSettingsService _settingsService;
	private readonly NexusPreferenceStore _preferences;

	internal NodeLibraryService(SetupSettingsService settingsService, NexusPreferenceStore preferences)
	{
		_settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
		_preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
	}

	internal async Task<NodeLibraryRoot> FetchNodesAsync()
	{
		var root = new NodeLibraryRoot();

		try
		{
			string json = _preferences.Get(PreferenceKeys.BookmarkedNodes, "[]");
			var saved = JsonSerializer.Deserialize<List<string>>(json);
			if (saved != null)
			{
				foreach (var s in saved) root.BookmarkedTypes.Add(s);
			}

			string catJson = _preferences.Get(PreferenceKeys.BookmarkedCategories, "[]");
			var savedCats = JsonSerializer.Deserialize<List<string>>(catJson);
			if (savedCats != null)
			{
				foreach (var s in savedCats) root.BookmarkedCategoryPaths.Add(s);
			}
		}
		catch { }

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Comfy-User", string.Empty);

			var response = await httpClient.GetAsync(
				ComfyApiOptions.GetObjectInfoUrl(_settingsService.Settings)).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) return root;

			using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
			using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

			foreach (var property in doc.RootElement.EnumerateObject())
			{
				string type = property.Name;
				var info = property.Value;

				try
				{
					var entry = ParseNodeEntry(type, info);
					AddEntryToRoot(root, entry);

					if (root.BookmarkedTypes.Contains(type))
					{
						root.Bookmarked.Add(entry);
					}
				}
				catch (Exception nodeEx)
				{
					NexusLog.Warning($"Skipping invalid node '{type}': {nodeEx.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[NODE_LIB] CRITICAL ERROR during fetch");
		}

		// After the tree is fully built, resolve bookmarked categories
		void ResolveCategories(NodeCategoryNode node)
		{
			if (root.BookmarkedCategoryPaths.Contains(node.FullPath))
			{
				root.BookmarkedCategories.Add(node);
			}
			foreach (var sub in node.SubCategories) ResolveCategories(sub);
		}
		ResolveCategories(root.NexusFamilyRoot);
		ResolveCategories(root.BlueprintRoot);
		ResolveCategories(root.PartnerRoot);
		ResolveCategories(root.ComfyRoot);
		ResolveCategories(root.ExtensionRoot);

		// Clean up invalid bookmarks
		var validBookmarks = root.Bookmarked.Select(e => e.Type).ToList();
		if (validBookmarks.Count != root.BookmarkedTypes.Count || root.BookmarkedCategories.Count != root.BookmarkedCategoryPaths.Count)
		{
			root.BookmarkedTypes.Clear();
			foreach (var v in validBookmarks) root.BookmarkedTypes.Add(v);

			root.BookmarkedCategoryPaths.Clear();
			foreach (var c in root.BookmarkedCategories) root.BookmarkedCategoryPaths.Add(c.FullPath);

			SaveBookmarks(root);
		}

		return root;
	}

	internal void SaveBookmarks(NodeLibraryRoot root)
	{
		try
		{
			string json = JsonSerializer.Serialize(root.BookmarkedTypes.ToList());
			_preferences.Set(PreferenceKeys.BookmarkedNodes, json);

			string catJson = JsonSerializer.Serialize(root.BookmarkedCategoryPaths.ToList());
			_preferences.Set(PreferenceKeys.BookmarkedCategories, catJson);
		}
		catch { }
	}

	private NodeLibraryEntry ParseNodeEntry(string type, JsonElement info)
	{
		string GetSafeString(string propName, string defaultVal)
		{
			if (!info.TryGetProperty(propName, out var prop)) return defaultVal;
			if (prop.ValueKind == JsonValueKind.Null) return defaultVal;
			if (prop.ValueKind == JsonValueKind.String) return prop.GetString() ?? defaultVal;
			return prop.ToString();
		}

		string displayName = GetSafeString("display_name", type);
		string description = GetSafeString("description", "");
		string category = GetSafeString("category", "uncategorized");
		string pythonModule = GetSafeString("python_module", "");

		NodeGroupKind groupKind = DetermineGroupKind(category, pythonModule);

		return new NodeLibraryEntry(
			type,
			displayName,
			description,
			category,
			pythonModule,
			groupKind,
			GetColorForCategory(category)
		);
	}

	private NodeGroupKind DetermineGroupKind(string category, string pythonModule)
	{
		// Partner = any node whose category starts with "api node/"
		if (category.StartsWith(PartnerPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return NodeGroupKind.Partner;
		}

		if (pythonModule == "nodes" || string.IsNullOrEmpty(pythonModule))
		{
			return NodeGroupKind.Comfy;
		}

		return NodeGroupKind.Extension;
	}

	private void AddEntryToRoot(NodeLibraryRoot root, NodeLibraryEntry entry)
	{
		switch (entry.GroupKind)
		{
			case NodeGroupKind.Partner:
				// Partner tree: strip "api node/" prefix and use mediaType/vendor path.
				// e.g. "api node/image/BFL" becomes ["image", "BFL"].
				string partnerPath = entry.Category.Substring(PartnerPrefix.Length);
				var partnerParts = partnerPath.Split(CategoryPathSeparators, StringSplitOptions.RemoveEmptyEntries);
				InjectIntoTree(root.PartnerRoot, entry, partnerParts);
				break;
			case NodeGroupKind.Comfy:
				InjectIntoTree(root.ComfyRoot, entry);
				break;
			case NodeGroupKind.Extension:
				// 1. All extension nodes merge into ComfyRoot categories (just like the Web UI)
				InjectIntoTree(root.ComfyRoot, entry);

				// 2. Put into ExtensionRoot grouped by [Extension Name] -> [Category]
				string extName = FormatExtensionName(entry.PythonModule);
				var catParts = entry.Category.Split(CategoryPathSeparators, StringSplitOptions.RemoveEmptyEntries);
				var extPath = new List<string> { extName };
				extPath.AddRange(catParts);

				// Separate Nexus Suite / ComfyUI-HUD into its own root
				if (string.Equals(extName, "ComfyUI-HUD", StringComparison.OrdinalIgnoreCase))
				{
					InjectIntoTree(root.NexusFamilyRoot, entry, NormalizeHudCategoryPath(catParts));
				}
				else
				{
					InjectIntoTree(root.ExtensionRoot, entry, extPath);
				}
				break;
		}
	}

	private string FormatExtensionName(string pythonModule)
	{
		if (string.IsNullOrWhiteSpace(pythonModule)) return "Unknown Extension";

		string ext = pythonModule;
		if (ext.StartsWith("custom_nodes.", StringComparison.OrdinalIgnoreCase))
		{
			ext = ext.Substring("custom_nodes.".Length);
		}

		var parts = ext.Split('.');
		return parts.Length > 0 ? parts[0] : ext;
	}

	private static IEnumerable<string> NormalizeHudCategoryPath(IEnumerable<string> categoryParts)
	{
		var parts = categoryParts.ToList();
		if (parts.Count > 0 && string.Equals(parts[0], "HUD", StringComparison.OrdinalIgnoreCase))
		{
			parts.RemoveAt(0);
		}

		return parts.Count > 0 ? parts : new[] { "uncategorized" };
	}

	private void InjectIntoTree(NodeCategoryNode rootNode, NodeLibraryEntry entry, IEnumerable<string>? overridePath = null)
	{
		var parts = overridePath ?? entry.Category.Split(CategoryPathSeparators, StringSplitOptions.RemoveEmptyEntries);
		if (!parts.Any()) parts = new[] { "uncategorized" };

		var current = rootNode;

		foreach (var part in parts)
		{
			var sub = current.SubCategories.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
			if (sub == null)
			{
				sub = new NodeCategoryNode(part, current);
				current.SubCategories.Add(sub);
			}
			current = sub;
		}

		current.Nodes.Add(entry);
	}

	private string GetColorForCategory(string category)
	{
		string primary = category.Split('/')[0].ToLower();
		return primary switch
		{
			"sampling" => "#ffd700",
			"image" => "#8de7ff",
			"video" => "#ff8c00",
			"audio" => "#da70d6",
			"text" => "#90ee90",
			"conditioning" => "#ff4500",
			"latent" => "#ba55d3",
			"loaders" => "#1e90ff",
			_ => "#b0c4de"
		};
	}
}
