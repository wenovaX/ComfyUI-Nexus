namespace ComfyUI_Nexus.Setup.Diagnostics;

using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

internal sealed class DiagnosticActionViewModel
{
	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
	public string WorkingHint { get; set; } = string.Empty;
	public Color NormalBackground { get; set; } = NexusColors.SurfaceSubtle;
	public Color HoverBackground { get; set; } = Color.FromArgb("#1Affffff");
	public Color TextColor { get; set; } = NexusColors.TextDim;
	public ICommand? Command { get; set; }
}

internal interface IDiagnosticCompletionSignal
{
	event EventHandler? CompletionSignalChanged;
	void SignalCompletionChanged();
}

internal sealed class DiagnosticNodeViewModel : INotifyPropertyChanged, IDiagnosticCompletionSignal
{
	private static readonly Color TransparentStateColor = Colors.Transparent;
	private static readonly Color DiagnosticAccentColor = Color.FromArgb("#31d8ff");
	private static readonly Color DiagnosticMutedColor = Color.FromArgb("#7fa0b8");
	private static readonly Color DiagnosticOptionalTextColor = Color.FromArgb("#8ecfff");
	private static readonly Color DiagnosticHealthyColor = Color.FromArgb("#00ff88");
	private static readonly Color DiagnosticOptionalWarningColor = Color.FromArgb("#ffd166");
	private static readonly Color DiagnosticRecoveryColor = Color.FromArgb("#ff9f5a");
	private static readonly Color DiagnosticErrorColor = Color.FromArgb("#ff6d8f");
	private static readonly Color DiagnosticFocusBackgroundColor = Color.FromArgb("#1Affffff");

	private string _iconSource = "status_pending.png";
	private Color _statusColor = DiagnosticMutedColor;
	private Brush _statusBrush = new SolidColorBrush(DiagnosticMutedColor);
	private string _displayName = string.Empty;
	private string _description = string.Empty;
	private string _actionText = Text("setup.status.pending");
	private string _environmentDetails = string.Empty;
	private string _environmentPath = string.Empty;
	private string _environmentDisplayPath = string.Empty;
	private string _operationProgressText = string.Empty;
	private bool _isLoading;
	private bool _canCancel;
	private bool _showProgress;
	private double _progressValue;
	private string _workingHint = string.Empty;
	private Color _detailsColor = DiagnosticMutedColor;
	private bool _preferStatusEditAction;
	private bool _isHighlighted;
	private Color _highlightBackground = TransparentStateColor;
	private double _interactionOverlayOpacity;
	private long _lastProgressDisplayTicks;
	private HealthState _currentHealth = HealthState.Pending;
	private IDispatcherTimer? _workingTextTimer;
	private int _workingTextFrameIndex;

	public IRuntimeDiagnosticNode Node { get; }
	public HealthState CurrentHealth { get => _currentHealth; private set => SetProperty(ref _currentHealth, value); }

	public ObservableCollection<DiagnosticActionViewModel> Actions { get; } = new();
	public bool IsAdvisoryNode => Node is IOptionalConfigurableDiagnosticNode;
	public bool HasActions => Actions.Count > 0;
	public bool HasNoActions => Actions.Count == 0;
	public bool CanRetry => CurrentHealth is HealthState.NeedsRecovery or HealthState.CriticalError
		&& Node is IConfigurableDiagnosticNode
		&& !IsLoading
		&& !HasActions;
	public bool CanEdit => CurrentHealth is HealthState.Healthy or HealthState.OptionalMissing
		&& Node is IConfigurableDiagnosticNode
		&& !IsLoading
		&& !HasActions;
	public bool CanHeaderEdit => CanEdit && !PreferStatusEditAction;
	public bool CanStatusEdit => CanEdit && PreferStatusEditAction;
	public bool HasEnvironmentDetails => !string.IsNullOrWhiteSpace(EnvironmentDetails);
	public int EnvironmentDetailsMaxLines => Node.NodeId switch
	{
		"manager-extension" => 16,
		"model-library" => 64,
		_ => 2
	};
	public double EnvironmentDetailsMinHeight => Node.NodeId == "manager-extension" ? 150 : 46;
	public bool HasEnvironmentPath => !string.IsNullOrWhiteSpace(EnvironmentPath);
	public bool HasOperationProgressText => !string.IsNullOrWhiteSpace(OperationProgressText);
	public string EnvironmentDisplayPath
	{
		get => _environmentDisplayPath;
		private set
		{
			SetProperty(ref _environmentDisplayPath, value);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEnvironmentPath)));
		}
	}

	public ICommand OpenPathCommand { get; }

	public DiagnosticNodeViewModel(IRuntimeDiagnosticNode node)
	{
		Node = node;
		DisplayName = node.DisplayName;
		Description = node.Description;

		OpenPathCommand = new Command(() =>
		{
			if (!string.IsNullOrWhiteSpace(EnvironmentPath))
			{
#if WINDOWS
				try
				{
					// Windows specific file explorer opening
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
					{
						FileName = EnvironmentPath,
						UseShellExecute = true,
						Verb = "open"
					});
				}
				catch { }
#endif
			}
		});
	}

	public string IconSource { get => _iconSource; set => SetProperty(ref _iconSource, value); }
	public Color StatusColor
	{
		get => _statusColor;
		set
		{
			if (_statusColor == value) return;

			_statusColor = value;
			_statusBrush = new SolidColorBrush(value);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
		}
	}
	public Brush StatusBrush => _statusBrush;
	public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
	public string Description { get => _description; set => SetProperty(ref _description, value); }
	public string ActionText { get => _actionText; set => SetProperty(ref _actionText, value); }
	public Color DetailsColor { get => _detailsColor; set => SetProperty(ref _detailsColor, value); }
	public bool PreferStatusEditAction
	{
		get => _preferStatusEditAction;
		set
		{
			if (System.Collections.Generic.EqualityComparer<bool>.Default.Equals(_preferStatusEditAction, value)) return;

			_preferStatusEditAction = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreferStatusEditAction)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanHeaderEdit)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStatusEdit)));
		}
	}
	public string OperationProgressText
	{
		get => _operationProgressText;
		set
		{
			SetProperty(ref _operationProgressText, value);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOperationProgressText)));
		}
	}
	public double InteractionOverlayOpacity { get => _interactionOverlayOpacity; set => SetProperty(ref _interactionOverlayOpacity, value); }
	public bool IsHighlighted
	{
		get => _isHighlighted;
		set
		{
			SetProperty(ref _isHighlighted, value);

			// If we are already Healthy or IsLoading, the background is managed by DataTriggers.
			// We only apply the white focus background for Pending nodes.
			if (CurrentHealth != HealthState.Pending) return;

			if (value)
			{
				HighlightBackground = DiagnosticFocusBackgroundColor; // White glass for active focus
			}
			else
			{
				HighlightBackground = TransparentStateColor;
			}
		}
	}
	public Color HighlightBackground { get => _highlightBackground; set => SetProperty(ref _highlightBackground, value); }

	public bool IsLoading
	{
		get => _isLoading;
		set
		{
			if (System.Collections.Generic.EqualityComparer<bool>.Default.Equals(_isLoading, value)) return;

			_isLoading = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanHeaderEdit)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStatusEdit)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWorkingHint)));
			if (value)
			{
				IconSource = "status_drive.png"; // Changed from pending to drive as requested
				ActionText = Text("setup.status.working");
				StatusColor = DiagnosticAccentColor;
				HighlightBackground = TransparentStateColor; // Let XAML Trigger handle the background
				StartWorkingTextLoop();
				return;
			}

			StopWorkingTextLoop();
		}
	}
	public bool CanCancel { get => _canCancel; set => SetProperty(ref _canCancel, value); }
	public bool ShowProgress { get => _showProgress; set => SetProperty(ref _showProgress, value); }
	public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }
	public string WorkingHint
	{
		get => _workingHint;
		set
		{
			SetProperty(ref _workingHint, value);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWorkingHint)));
		}
	}
	public bool HasWorkingHint => IsLoading && !string.IsNullOrWhiteSpace(WorkingHint);

	public string EnvironmentDetails
	{
		get => _environmentDetails;
		set
		{
			SetProperty(ref _environmentDetails, value);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEnvironmentDetails)));
		}
	}

	public string EnvironmentPath
	{
		get => _environmentPath;
		set
		{
			SetProperty(ref _environmentPath, value);
			EnvironmentDisplayPath = ToDisplayPath(value);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEnvironmentPath)));
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	public event EventHandler? CompletionSignalChanged;

	private void SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
	{
		if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(backingStore, value)) return;
		backingStore = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	public void NotifyActionsChanged()
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActions)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoActions)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanHeaderEdit)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStatusEdit)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
	}

	public void SignalCompletionChanged()
		=> CompletionSignalChanged?.Invoke(this, EventArgs.Empty);

	public void UpdateProgressDisplay(double progress, string message, bool force = false)
	{
		ProgressValue = progress;

		string operationProgressText = ExtractOperationProgressText(message);
		if (!string.IsNullOrWhiteSpace(operationProgressText))
		{
			OperationProgressText = operationProgressText;
		}

		long nowTicks = DateTime.UtcNow.Ticks;
		if (!force
			&& progress > 0
			&& progress < 1
			&& nowTicks - _lastProgressDisplayTicks < TimeSpan.FromMilliseconds(250).Ticks)
		{
			return;
		}

		_lastProgressDisplayTicks = nowTicks;
		EnvironmentDetails = FormatProgressMessage(message);
	}

	public void UpdateState(HealthState state)
	{
		CurrentHealth = state;
		IsLoading = false; // reset loading if state updates
		OperationProgressText = string.Empty;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanHeaderEdit)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStatusEdit)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));

		switch (state)
		{
			case HealthState.Healthy:
				if (Node is IOptionalConfigurableDiagnosticNode)
				{
					IconSource = "status_ready.png";
					StatusColor = DiagnosticAccentColor;
					DetailsColor = DiagnosticOptionalTextColor;
					ActionText = Text("setup.status.ready");
					HighlightBackground = TransparentStateColor;
					Actions.Clear();
					NotifyActionsChanged();
					break;
				}

				IconSource = "status_ready.png";
				StatusColor = DiagnosticHealthyColor; // Vivid Emerald Green
				DetailsColor = DiagnosticHealthyColor;
				ActionText = Text("setup.status.ready");
				HighlightBackground = TransparentStateColor; // Clear hover style
				Actions.Clear();
				NotifyActionsChanged();
				break;
			case HealthState.OptionalMissing:
				if (Node is IOptionalConfigurableDiagnosticNode)
				{
					IconSource = "status_drive.png";
					StatusColor = DiagnosticAccentColor;
					DetailsColor = DiagnosticOptionalTextColor;
					ActionText = Text("setup.status.setup");
					break;
				}

				IconSource = "status_warning.png";
				StatusColor = DiagnosticOptionalWarningColor;
				DetailsColor = DiagnosticOptionalWarningColor;
				ActionText = Text("setup.status.optional");
				break;
			case HealthState.Pending:
				IconSource = "status_drive.png"; // Changed from pending to drive to match Probing theme
				StatusColor = DiagnosticMutedColor;
				DetailsColor = DiagnosticMutedColor;
				ActionText = Text("setup.status.pending");
				break;
			case HealthState.NeedsRecovery:
				IconSource = "status_warning.png";
				StatusColor = DiagnosticRecoveryColor;
				DetailsColor = DiagnosticRecoveryColor;
				ActionText = Text("setup.status.missing");
				break;
			case HealthState.CriticalError:
				IconSource = "status_error.png";
				StatusColor = DiagnosticErrorColor;
				DetailsColor = DiagnosticErrorColor;
				ActionText = Text("setup.status.error");
				break;
		}

		SignalCompletionChanged();
	}

	private void StartWorkingTextLoop()
	{
		StopWorkingTextLoop();
		IDispatcher? dispatcher = Application.Current?.Dispatcher;
		if (dispatcher is null)
		{
			return;
		}

		_workingTextFrameIndex = 0;
		_workingTextTimer = dispatcher.CreateTimer();
		_workingTextTimer.Interval = TimeSpan.FromMilliseconds(420);
		_workingTextTimer.Tick += OnWorkingTextTimerTick;
		UpdateWorkingText();
		_workingTextTimer.Start();
	}

	private void StopWorkingTextLoop()
	{
		if (_workingTextTimer is null)
		{
			return;
		}

		_workingTextTimer.Stop();
		_workingTextTimer.Tick -= OnWorkingTextTimerTick;
		_workingTextTimer = null;
	}

	private void OnWorkingTextTimerTick(object? sender, EventArgs e)
	{
		if (!IsLoading)
		{
			StopWorkingTextLoop();
			return;
		}

		UpdateWorkingText();
	}

	private void UpdateWorkingText()
	{
		if (!IsLoading)
		{
			return;
		}

		string working = Text("setup.status.working");
		string[] frames = { working, $"{working}.", $"{working}..", $"{working}..." };
		ActionText = frames[_workingTextFrameIndex % frames.Length];
		_workingTextFrameIndex++;
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);

	private static string FormatProgressMessage(string message)
	{
		const int MaxDisplayLength = 220;
		if (string.IsNullOrWhiteSpace(message))
		{
			return string.Empty;
		}

		string compact = message
			.Replace('\r', ' ')
			.Replace('\n', ' ')
			.Trim();

		while (compact.Contains("  ", StringComparison.Ordinal))
		{
			compact = compact.Replace("  ", " ", StringComparison.Ordinal);
		}

		return compact.Length <= MaxDisplayLength
			? compact
			: string.Concat(compact.AsSpan(0, MaxDisplayLength - 3), "...");
	}

	private static string ExtractOperationProgressText(string message)
	{
		if (string.IsNullOrWhiteSpace(message)
			|| !message.StartsWith("[Extensions ", StringComparison.Ordinal))
		{
			return string.Empty;
		}

		int firstClose = message.IndexOf(']', StringComparison.Ordinal);
		if (firstClose <= 1)
		{
			return string.Empty;
		}

		string count = message.Substring(1, firstClose - 1);
		int labelStart = message.IndexOf('[', firstClose + 1);
		int labelEnd = labelStart >= 0
			? message.IndexOf(']', labelStart + 1)
			: -1;
		if (labelStart < 0 || labelEnd <= labelStart)
		{
			return count;
		}

		string label = message.Substring(labelStart + 1, labelEnd - labelStart - 1);
		return $"{count} · {label}";
	}

	private static string ToDisplayPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}

		try
		{
			string activeComfyPath = ComfyPathResolver.ResolveActiveComfyPath();
			if (!string.IsNullOrWhiteSpace(activeComfyPath)
				&& Path.IsPathRooted(path)
				&& Path.IsPathRooted(activeComfyPath))
			{
				string fullPath = Path.GetFullPath(path);
				string fullRoot = Path.GetFullPath(activeComfyPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					+ Path.DirectorySeparatorChar;

				if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
				{
					return Path.GetRelativePath(fullRoot, fullPath)
						.Replace(Path.DirectorySeparatorChar, '/')
						.Replace(Path.AltDirectorySeparatorChar, '/');
				}
			}
		}
		catch
		{
		}

		return path;
	}
}
