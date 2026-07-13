namespace ComfyUI_Nexus.Setup.Diagnostics;

using System.ComponentModel;
using System.Runtime.CompilerServices;

internal enum SetupDiagnosticStepState
{
	NotStarted,
	Preparing,
	WaitingForUser,
	Working,
	Verified,
	Skipped,
	Failed
}

internal enum SetupScrollReason
{
	StepFocused,
	OptionalSectionFocused,
	ItemUpdated
}

internal sealed class SetupDiagnosticStep(DiagnosticNodeViewModel viewModel, bool isRequired) : INotifyPropertyChanged
{
	private SetupDiagnosticStepState _state = SetupDiagnosticStepState.NotStarted;

	public DiagnosticNodeViewModel ViewModel { get; } = viewModel;
	public bool IsRequired { get; } = isRequired;

	public SetupDiagnosticStepState State
	{
		get => _state;
		set => SetProperty(ref _state, value);
	}

	public bool CountsAsReady => State is SetupDiagnosticStepState.Verified or SetupDiagnosticStepState.Skipped;

	public event PropertyChangedEventHandler? PropertyChanged;

	private void SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
	{
		if (EqualityComparer<T>.Default.Equals(backingStore, value)) return;
		backingStore = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
