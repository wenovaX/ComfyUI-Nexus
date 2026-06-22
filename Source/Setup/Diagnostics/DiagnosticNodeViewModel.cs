namespace ComfyUI_Nexus.Setup.Diagnostics;

using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Ui;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
	public Color NormalBackground { get; set; } = NexusColors.SurfaceSubtle;
	public Color HoverBackground { get; set; } = Color.FromArgb("#1Affffff");
	public Color TextColor { get; set; } = NexusColors.TextDim;
	public ICommand? Command { get; set; }
}

internal sealed class DiagnosticNodeViewModel : INotifyPropertyChanged
{
	private const string TransparentStateColor = "Transparent";
	private const string DiagnosticAccentHex = "#31d8ff";
	private const string DiagnosticMutedHex = "#7fa0b8";
	private const string DiagnosticOptionalTextHex = "#8ecfff";
	private const string DiagnosticHealthyHex = "#00ff88";
	private const string DiagnosticOptionalWarningHex = "#ffd166";
	private const string DiagnosticRecoveryHex = "#ff9f5a";
	private const string DiagnosticErrorHex = "#ff6d8f";
	private const string DiagnosticFocusBackgroundHex = "#1Affffff";

	private string _iconSource = "status_pending.png";
	private string _statusColor = DiagnosticMutedHex;
	private string _displayName = string.Empty;
	private string _description = string.Empty;
	private string _actionText = Text("setup.status.pending");
	private string _environmentDetails = string.Empty;
	private string _environmentPath = string.Empty;
	private bool _isLoading;
	private bool _showProgress;
	private double _progressValue;
	private string _detailsColor = DiagnosticMutedHex;
	private bool _isHighlighted;
	private string _highlightBorder = TransparentStateColor;
	private string _highlightBackground = TransparentStateColor;
	private double _interactionOverlayOpacity;
	private HealthState _currentHealth = HealthState.Pending;
	private CancellationTokenSource? _workingTextCts;

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
	public bool HasEnvironmentDetails => !string.IsNullOrWhiteSpace(EnvironmentDetails);
	public int EnvironmentDetailsMaxLines => Node.NodeId switch
	{
		"manager-extension" => 16,
		"model-library" => 64,
		_ => 2
	};
	public double EnvironmentDetailsMinHeight => Node.NodeId == "manager-extension" ? 150 : 46;
	public bool HasEnvironmentPath => !string.IsNullOrWhiteSpace(EnvironmentPath);

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
	public string StatusColor { get => _statusColor; set => SetProperty(ref _statusColor, value); }
	public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
	public string Description { get => _description; set => SetProperty(ref _description, value); }
	public string ActionText { get => _actionText; set => SetProperty(ref _actionText, value); }
	public string DetailsColor { get => _detailsColor; set => SetProperty(ref _detailsColor, value); }
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
				HighlightBackground = DiagnosticFocusBackgroundHex; // White glass for active focus
			}
			else
			{
				HighlightBackground = TransparentStateColor;
			}
		}
	}
	public string HighlightBorder { get => _highlightBorder; set => SetProperty(ref _highlightBorder, value); }
	public string HighlightBackground { get => _highlightBackground; set => SetProperty(ref _highlightBackground, value); }

	public bool IsLoading
	{
		get => _isLoading;
		set
		{
			if (System.Collections.Generic.EqualityComparer<bool>.Default.Equals(_isLoading, value)) return;

			_isLoading = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
			if (value)
			{
				IconSource = "status_drive.png"; // Changed from pending to drive as requested
				ActionText = Text("setup.status.working");
				StatusColor = DiagnosticAccentHex;
				HighlightBackground = TransparentStateColor; // Let XAML Trigger handle the background
				StartWorkingTextLoop();
				return;
			}

			StopWorkingTextLoop();
		}
	}
	public bool ShowProgress { get => _showProgress; set => SetProperty(ref _showProgress, value); }
	public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

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
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEnvironmentPath)));
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

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
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
	}

	public void UpdateState(HealthState state)
	{
		CurrentHealth = state;
		IsLoading = false; // reset loading if state updates
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));

		switch (state)
		{
			case HealthState.Healthy:
				if (Node is IOptionalConfigurableDiagnosticNode)
				{
					IconSource = "status_ready.png";
					StatusColor = DiagnosticAccentHex;
					DetailsColor = DiagnosticOptionalTextHex;
					ActionText = Text("setup.status.ready");
					HighlightBackground = TransparentStateColor;
					Actions.Clear();
					NotifyActionsChanged();
					break;
				}

				IconSource = "status_ready.png";
				StatusColor = DiagnosticHealthyHex; // Vivid Emerald Green
				DetailsColor = DiagnosticHealthyHex;
				ActionText = Text("setup.status.ready");
				HighlightBackground = TransparentStateColor; // Clear hover style
				Actions.Clear();
				NotifyActionsChanged();
				break;
			case HealthState.OptionalMissing:
				if (Node is IOptionalConfigurableDiagnosticNode)
				{
					IconSource = "status_drive.png";
					StatusColor = DiagnosticAccentHex;
					DetailsColor = DiagnosticOptionalTextHex;
					ActionText = Text("setup.status.setup");
					break;
				}

				IconSource = "status_warning.png";
				StatusColor = DiagnosticOptionalWarningHex;
				DetailsColor = DiagnosticOptionalWarningHex;
				ActionText = Text("setup.status.optional");
				break;
			case HealthState.Pending:
				IconSource = "status_drive.png"; // Changed from pending to drive to match Probing theme
				StatusColor = DiagnosticMutedHex;
				DetailsColor = DiagnosticMutedHex;
				ActionText = Text("setup.status.pending");
				break;
			case HealthState.NeedsRecovery:
				IconSource = "status_warning.png";
				StatusColor = DiagnosticRecoveryHex;
				DetailsColor = DiagnosticRecoveryHex;
				ActionText = Text("setup.status.missing");
				break;
			case HealthState.CriticalError:
				IconSource = "status_error.png";
				StatusColor = DiagnosticErrorHex;
				DetailsColor = DiagnosticErrorHex;
				ActionText = Text("setup.status.error");
				break;
		}
	}

	private void StartWorkingTextLoop()
	{
		StopWorkingTextLoop();

		var cts = new CancellationTokenSource();
		_workingTextCts = cts;
		_ = AnimateWorkingTextAsync(cts.Token);
	}

	private void StopWorkingTextLoop()
	{
		_workingTextCts?.Cancel();
		_workingTextCts = null;
	}

	private async Task AnimateWorkingTextAsync(CancellationToken token)
	{
		string working = Text("setup.status.working");
		string[] frames = { working, $"{working}.", $"{working}..", $"{working}..." };
		int index = 0;

		while (!token.IsCancellationRequested)
		{
			string frame = frames[index % frames.Length];
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (!token.IsCancellationRequested && IsLoading)
				{
					ActionText = frame;
				}
			});

			index++;

			try
			{
				await Task.Delay(420, token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
