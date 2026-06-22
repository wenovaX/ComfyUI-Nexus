namespace ComfyUI_Nexus.Views.Rail.Contracts;

internal interface IRailToolView
{
	View View { get; }

	bool IsReady { get; }

	bool IsBusy { get; }

	Task PrewarmAsync(CancellationToken cancellationToken);

	Task OpenAsync(CancellationToken cancellationToken);

	void ResetPresentation();
}
