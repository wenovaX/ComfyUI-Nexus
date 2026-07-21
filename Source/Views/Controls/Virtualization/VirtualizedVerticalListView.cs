using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Layouts;

namespace ComfyUI_Nexus.Views.Controls.Virtualization;

/// <summary>
/// Reusable vertical virtual list for rail-style panels that need ScrollView behavior
/// without materializing every row into the visual tree.
/// </summary>
public sealed class VirtualizedVerticalListView : ContentView
{
	private const double DefaultItemHeight = 28;
	private const double RenderBuffer = 520;

	private readonly ScrollView _scrollView;
	private readonly Grid _surface;
	private readonly ContentView _spacer;
	private readonly AbsoluteLayout _canvas;
	private readonly Dictionary<int, View> _visibleViews = new();
	private readonly Dictionary<DataTemplate, Stack<View>> _viewPools = new();
	private readonly List<object> _items = [];
	private readonly List<double> _offsets = [];
	private readonly List<double> _heights = [];
	private INotifyCollectionChanged? _observedCollection;
	private int _batchUpdateDepth;
	private int _visibleUpdateVersion;
	private bool _refreshQueued;
	private bool _isLoaded;
	private int _lifecycleVersion;

	public VirtualizedVerticalListView()
	{
		_spacer = new ContentView { HeightRequest = 0 };
		_canvas = new AbsoluteLayout
		{
			HorizontalOptions = LayoutOptions.Start,
			VerticalOptions = LayoutOptions.Start,
		};
		_surface = new Grid
		{
			HorizontalOptions = LayoutOptions.Start,
			VerticalOptions = LayoutOptions.Start,
		};
		_surface.Children.Add(_spacer);
		_surface.Children.Add(_canvas);
		_scrollView = new ScrollView
		{
			Content = _surface,
		};
		_scrollView.Scrolled += (_, _) => UpdateVisibleViews();
		_scrollView.SizeChanged += (_, _) => UpdateVisibleViews();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		Content = _scrollView;
	}

	public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
		nameof(ItemsSource),
		typeof(IEnumerable),
		typeof(VirtualizedVerticalListView),
		default(IEnumerable),
		propertyChanged: (bindable, oldValue, newValue) =>
			((VirtualizedVerticalListView)bindable).OnItemsSourceChanged(oldValue as IEnumerable, newValue as IEnumerable));

	public IEnumerable? ItemsSource
	{
		get => (IEnumerable?)GetValue(ItemsSourceProperty);
		set => SetValue(ItemsSourceProperty, value);
	}

	public DataTemplateSelector? ItemTemplateSelector { get; set; }

	public Func<object, double>? ItemHeightSelector { get; set; }

	public async Task PrewarmViewPoolAsync(IEnumerable<object> sampleItems, int viewsPerTemplate, int batchSize, CancellationToken cancellationToken)
	{
		if (ItemTemplateSelector == null || viewsPerTemplate <= 0)
		{
			return;
		}

		int createdSinceYield = 0;
		foreach (DataTemplate template in GetDistinctTemplates(sampleItems))
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			if (!_viewPools.TryGetValue(template, out Stack<View>? pool))
			{
				pool = new Stack<View>();
				_viewPools[template] = pool;
			}

			while (pool.Count < viewsPerTemplate)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				object content = template.CreateContent();
				pool.Push(content is View view ? view : new ContentView());

				createdSinceYield++;
				if (createdSinceYield >= Math.Max(1, batchSize))
				{
					createdSinceYield = 0;
					await Task.Yield();
				}
			}
		}
	}

	public void BeginBatchUpdate()
	{
		_batchUpdateDepth++;
	}

	public void EndBatchUpdate()
	{
		if (_batchUpdateDepth <= 0)
		{
			return;
		}

		_batchUpdateDepth--;
		if (_batchUpdateDepth == 0 && _refreshQueued)
		{
			_refreshQueued = false;
			RefreshItems();
		}
	}

	public Task ScrollToTopAsync(bool animated = false)
		=> _scrollView.ScrollToAsync(0, 0, animated);

	public async Task MaterializeVisibleViewsAsync(CancellationToken cancellationToken)
	{
		if (!CanMaterialize())
		{
			return;
		}

		int updateVersion = BeginVisibleUpdate();
		int lifecycleVersion = _lifecycleVersion;
		if (_items.Count == 0 || Width <= 0)
		{
			_canvas.HeightRequest = 0;
			_canvas.TranslationY = 0;
			return;
		}

		double viewportHeight = Math.Max(1, _scrollView.Height);
		double viewportWidth = Math.Max(1, Width);
		double startY = Math.Max(0, _scrollView.ScrollY - RenderBuffer);
		double endY = _scrollView.ScrollY + viewportHeight + RenderBuffer;
		int firstIndex = FindFirstVisibleIndex(startY);
		int lastIndex = FindLastVisibleIndex(endY);
		if (firstIndex > lastIndex)
		{
			RecycleMissingVisibleViews(0, -1);
			return;
		}

		double renderTop = _offsets[firstIndex];
		double renderBottom = _offsets[lastIndex] + _heights[lastIndex];
		_canvas.WidthRequest = viewportWidth;
		_canvas.HeightRequest = Math.Max(1, renderBottom - renderTop);
		_canvas.TranslationY = renderTop;

		RecycleMissingVisibleViews(firstIndex, lastIndex);
		int attachedSinceYield = 0;
		for (int i = firstIndex; i <= lastIndex; i++)
		{
			if (cancellationToken.IsCancellationRequested || lifecycleVersion != _lifecycleVersion || !CanMaterialize() || updateVersion != _visibleUpdateVersion)
			{
				return;
			}

			if (_visibleViews.TryGetValue(i, out View? existingView))
			{
				UpdateViewLayout(i, existingView, renderTop, viewportWidth);
				continue;
			}

			View view = CreateViewForItem(_items[i]);
			view.CancelAnimations();
			view.Opacity = 1;
			view.TranslationY = 0;
			view.BindingContext = _items[i];
			_visibleViews[i] = view;
			UpdateViewLayout(i, view, renderTop, viewportWidth);
			_canvas.Children.Add(view);

			attachedSinceYield++;
			if (attachedSinceYield >= 12)
			{
				attachedSinceYield = 0;
				await Task.Yield();
			}
		}
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		_isLoaded = true;
		_lifecycleVersion++;
		UpdateVisibleViews();
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		_isLoaded = false;
		_lifecycleVersion++;
		BeginVisibleUpdate();
		RecycleMissingVisibleViews(0, -1);
	}

	private bool CanMaterialize()
		=> _isLoaded && Handler is not null && _canvas.Handler is not null;
	private void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
	{
		if (_observedCollection != null)
		{
			_observedCollection.CollectionChanged -= OnItemsSourceCollectionChanged;
			_observedCollection = null;
		}

		if (newValue is INotifyCollectionChanged notifyCollection)
		{
			_observedCollection = notifyCollection;
			_observedCollection.CollectionChanged += OnItemsSourceCollectionChanged;
		}

		RefreshItems();
	}

	private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (_batchUpdateDepth > 0)
		{
			_refreshQueued = true;
			return;
		}

		RefreshItems();
	}

	private IEnumerable<DataTemplate> GetDistinctTemplates(IEnumerable<object> sampleItems)
	{
		var templates = new HashSet<DataTemplate>();
		foreach (object item in sampleItems)
		{
			DataTemplate? template = ItemTemplateSelector?.SelectTemplate(item, this);
			if (template != null && templates.Add(template))
			{
				yield return template;
			}
		}
	}

	private void RefreshItems()
	{
		BeginVisibleUpdate();
		_items.Clear();
		_offsets.Clear();
		_heights.Clear();

		if (ItemsSource != null)
		{
			double offset = 0;
			foreach (object item in ItemsSource)
			{
				double height = Math.Max(1, ItemHeightSelector?.Invoke(item) ?? DefaultItemHeight);
				_items.Add(item);
				_offsets.Add(offset);
				_heights.Add(height);
				offset += height;
			}
		}

		_spacer.HeightRequest = _items.Count == 0 ? 0 : _offsets[^1] + _heights[^1];
		RecycleMissingVisibleViews(0, -1);
		UpdateVisibleViews();
	}

	// Initial attachment must be immediate: lazy first-paint can briefly show a single
	// center-prioritized row while the rail is settling. Subsequent scroll updates still
	// use delayed reveal so fast scrolling stays smooth.
	private void UpdateVisibleViews()
	{
		if (!CanMaterialize())
		{
			return;
		}

		int updateVersion = BeginVisibleUpdate();
		if (_items.Count == 0 || Width <= 0)
		{
			_canvas.HeightRequest = 0;
			_canvas.TranslationY = 0;
			return;
		}

		if (_scrollView.Height <= 1)
		{
			return;
		}

		double viewportHeight = _scrollView.Height;
		double viewportWidth = Math.Max(1, Width);
		double startY = Math.Max(0, _scrollView.ScrollY - RenderBuffer);
		double endY = _scrollView.ScrollY + viewportHeight + RenderBuffer;
		int firstIndex = FindFirstVisibleIndex(startY);
		int lastIndex = FindLastVisibleIndex(endY);
		if (firstIndex > lastIndex)
		{
			RecycleMissingVisibleViews(0, -1);
			return;
		}

		double renderTop = _offsets[firstIndex];
		double renderBottom = _offsets[lastIndex] + _heights[lastIndex];
		_canvas.WidthRequest = viewportWidth;
		_canvas.HeightRequest = Math.Max(1, renderBottom - renderTop);
		_canvas.TranslationY = renderTop;

		RecycleMissingVisibleViews(firstIndex, lastIndex);
		var missingIndexes = new List<int>();
		for (int i = firstIndex; i <= lastIndex; i++)
		{
			if (_visibleViews.TryGetValue(i, out View? view))
			{
				UpdateViewLayout(i, view, renderTop, viewportWidth);
				continue;
			}

			missingIndexes.Add(i);
		}

		if (missingIndexes.Count > 0)
		{
			AttachMissingViewsImmediately(missingIndexes, renderTop, viewportWidth, updateVersion);
		}
	}

	private int BeginVisibleUpdate()
	{
		return ++_visibleUpdateVersion;
	}

	private void AttachMissingViewsImmediately(IReadOnlyList<int> indexes, double renderTop, double viewportWidth, int updateVersion)
	{
		foreach (int index in indexes)
		{
			if (updateVersion != _visibleUpdateVersion || index < 0 || index >= _items.Count)
			{
				return;
			}

			if (_visibleViews.ContainsKey(index))
			{
				continue;
			}

			View view = CreateViewForItem(_items[index]);
			view.CancelAnimations();
			view.Opacity = 1;
			view.TranslationY = 0;
			view.BindingContext = _items[index];
			_visibleViews[index] = view;
			UpdateViewLayout(index, view, renderTop, viewportWidth);
			_canvas.Children.Add(view);
		}
	}

	private void UpdateViewLayout(int index, View view, double renderTop, double viewportWidth)
	{
		view.BindingContext = _items[index];
		AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
		AbsoluteLayout.SetLayoutBounds(
			view,
			new Rect(0, _offsets[index] - renderTop, viewportWidth, _heights[index]));
	}

	private int FindFirstVisibleIndex(double y)
	{
		for (int i = 0; i < _items.Count; i++)
		{
			if (_offsets[i] + _heights[i] >= y)
			{
				return i;
			}
		}

		return _items.Count - 1;
	}

	private int FindLastVisibleIndex(double y)
	{
		for (int i = 0; i < _items.Count; i++)
		{
			if (_offsets[i] > y)
			{
				return Math.Max(0, i - 1);
			}
		}

		return _items.Count - 1;
	}

	private View CreateViewForItem(object item)
	{
		DataTemplate? template = ItemTemplateSelector?.SelectTemplate(item, this);
		if (template == null)
		{
			return new ContentView();
		}

		if (_viewPools.TryGetValue(template, out Stack<View>? pool) && pool.Count > 0)
		{
			return pool.Pop();
		}

		object content = template.CreateContent();
		return content switch
		{
			View view => view,
			_ => new ContentView(),
		};
	}

	private void RecycleMissingVisibleViews(int firstIndex, int lastIndex)
	{
		foreach (int index in _visibleViews.Keys.ToArray())
		{
			if (index >= firstIndex && index <= lastIndex)
			{
				continue;
			}

			View view = _visibleViews[index];
			_canvas.Children.Remove(view);
			view.CancelAnimations();
			ReturnViewToPool(view);
			_visibleViews.Remove(index);
		}
	}

	private void ReturnViewToPool(View view)
	{
		if (view.BindingContext is not object item)
		{
			return;
		}

		DataTemplate? template = ItemTemplateSelector?.SelectTemplate(item, this);
		view.BindingContext = null;
		if (template == null)
		{
			return;
		}

		ReturnViewToPool(view, template);
	}

	private void ReturnViewToPool(View view, DataTemplate template)
	{
		if (!_viewPools.TryGetValue(template, out Stack<View>? pool))
		{
			pool = new Stack<View>();
			_viewPools[template] = pool;
		}

		pool.Push(view);
	}
}
