namespace ComfyUI_Nexus.Setup.Diagnostics;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Diagnostics.Nodes;
using ComfyUI_Nexus.Setup.Services;
using Microsoft.Maui.Graphics;
using ComfyUI_Nexus.Ui;

internal sealed class InitiationSequenceRunner(
	Action<DiagnosticNodeViewModel, IConfigurableDiagnosticNode> populateActions,
	Action<DiagnosticNodeViewModel> enableInteraction,
	Func<SetupDiagnosticStep, CancellationToken, Task> waitForStepCompleteAsync,
	Func<SetupDiagnosticStep, SetupScrollReason, Task> requestScrollAsync,
	Action evaluateReadiness,
	Action<DiagnosticNodeViewModel, double, string> updateProgress)
{
	private static readonly Color DiagnosticPendingColor = Color.FromArgb("#31d8ff");

	internal async Task RunArchitectAsync(IReadOnlyList<SetupDiagnosticStep> steps, CancellationToken cancellationToken)
		=> await RunInteractiveSequenceAsync(steps, cancellationToken);

	internal async Task RunVanguardAsync(IReadOnlyList<SetupDiagnosticStep> steps, CancellationToken cancellationToken)
		=> await RunInteractiveSequenceAsync(steps, cancellationToken);

	private async Task RunInteractiveSequenceAsync(IReadOnlyList<SetupDiagnosticStep> steps, CancellationToken cancellationToken)
	{
		for (int i = 0; i < steps.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			SetupDiagnosticStep step = steps[i];
			var vm = step.ViewModel;
			await RunOnMainThreadAsync(() =>
			{
				step.State = SetupDiagnosticStepState.Preparing;
				vm.IsHighlighted = true;
			});

			if (vm.Node is IConfigurableDiagnosticNode configurableNode)
			{
				await RunOnMainThreadAsync(() => vm.IsLoading = true);
				await ProbeConfigurableNodeAsync(vm, configurableNode, cancellationToken);
				await RunOnMainThreadAsync(() => vm.IsLoading = false);
				await RunOnMainThreadAsync(() => step.State = SetupDiagnosticStepState.WaitingForUser);
				await RunOnMainThreadAsync(() => enableInteraction(vm));
				await RunOnMainThreadAsync(evaluateReadiness);
				await requestScrollAsync(step, SetupScrollReason.StepFocused);
				await waitForStepCompleteAsync(step, cancellationToken);
				await RunOnMainThreadAsync(evaluateReadiness);
			}
			else
			{
				await RunOnMainThreadAsync(() => step.State = SetupDiagnosticStepState.Working);
				await requestScrollAsync(step, SetupScrollReason.StepFocused);
				await CheckOrRecoverNodeAsync(vm, cancellationToken);
				await RunOnMainThreadAsync(() => MarkStepFromHealth(step));
				await Task.Yield();
			}

			await RunOnMainThreadAsync(() =>
			{
				vm.IsHighlighted = false;
			});
			await RunOnMainThreadAsync(evaluateReadiness);
		}

		await RunOnMainThreadAsync(evaluateReadiness);
	}

	private async Task ProbeConfigurableNodeAsync(
		DiagnosticNodeViewModel vm,
		IConfigurableDiagnosticNode configurableNode,
		CancellationToken cancellationToken)
	{
		try
		{
			await RunOnMainThreadAsync(() =>
			{
				vm.ActionText = Text("setup.status.probing");
				vm.IconSource = "status_pending.png";
				vm.StatusColor = DiagnosticPendingColor;
			});

			await configurableNode.ProbeEnvironmentAsync(cancellationToken);
			await RunOnMainThreadAsync(() =>
			{
				vm.EnvironmentDetails = configurableNode.EnvironmentDetails;
				vm.EnvironmentPath = configurableNode.EnvironmentPath;
			});

			if (configurableNode is IOptionalConfigurableDiagnosticNode)
			{
				HealthState health = await vm.Node.CheckHealthAsync(cancellationToken);
				await RunOnMainThreadAsync(() =>
				{
					vm.UpdateState(health);
					vm.EnvironmentDetails = configurableNode.EnvironmentDetails;
					vm.EnvironmentPath = configurableNode.EnvironmentPath;
					if (configurableNode.NodeId == "model-library"
						&& health is HealthState.Healthy or HealthState.OptionalMissing)
					{
						populateActions(vm, configurableNode);
					}
				});
				return;
			}

			await RunOnMainThreadAsync(() =>
			{
				populateActions(vm, configurableNode);
				vm.IconSource = "status_drive.png";
				vm.StatusColor = DiagnosticPendingColor;
				vm.ActionText = configurableNode is ComfyCoreDiagnosticNode
					? Text("setup.status.install")
					: Text("setup.status.setup");
			});
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, $"[INIT] Configurable node probe failed: {configurableNode.NodeId}");
			await RunOnMainThreadAsync(() =>
			{
				vm.UpdateState(HealthState.CriticalError);
				vm.EnvironmentDetails = LocalizationManager.Format("setup.common.probe_failed", configurableNode.DisplayName, ex.Message);
				vm.EnvironmentPath = configurableNode.EnvironmentPath;
			});
		}
	}

	private async Task CheckOrRecoverNodeAsync(DiagnosticNodeViewModel vm, CancellationToken cancellationToken)
	{
		await RunOnMainThreadAsync(() =>
		{
			vm.ActionText = Text("setup.status.checking");
			vm.IconSource = "status_pending.png";
			vm.StatusColor = DiagnosticPendingColor;
		});

		HealthState health = await vm.Node.CheckHealthAsync(cancellationToken);
		if (health is HealthState.Healthy or HealthState.OptionalMissing)
		{
			await RunOnMainThreadAsync(() => vm.UpdateState(health));
			return;
		}

		await RecoverNodeAsync(vm, cancellationToken);
	}

	private async Task RecoverNodeAsync(DiagnosticNodeViewModel vm, CancellationToken cancellationToken)
	{
		await RunOnMainThreadAsync(() =>
		{
			vm.ShowProgress = true;
			vm.ProgressValue = 0;
			vm.ActionText = Text("setup.status.installing");
		});

		var originalOnProgress = ComfyInstallService.Instance.OnProgress;
		ComfyInstallService.Instance.OnProgress = (progress, message) => updateProgress(vm, progress, message);

		try
		{
			var progress = new Progress<double>(value => UiThread.TryBeginInvoke(() => vm.ProgressValue = value, "INITIATION:PROGRESS"));
			RecoveryResult result = await vm.Node.RecoverAsync(progress, cancellationToken);

			if (result.IsSuccess)
			{
				await RunOnMainThreadAsync(() =>
				{
					vm.ShowProgress = false;
					if (vm.Node is ComfyCoreDiagnosticNode comfyNode)
					{
						vm.EnvironmentDetails = comfyNode.EnvironmentDetails;
					}

					vm.UpdateState(HealthState.Healthy);
				});
				return;
			}

			await RunOnMainThreadAsync(() =>
			{
				vm.ShowProgress = false;
				vm.UpdateState(HealthState.CriticalError);
				vm.EnvironmentDetails = result.Message;
			});
		}
		finally
		{
			ComfyInstallService.Instance.OnProgress = originalOnProgress;
			await RunOnMainThreadAsync(() => vm.ShowProgress = false);
		}
	}

	private static Task RunOnMainThreadAsync(Action action)
	{
		return UiThread.InvokeAsync(() =>
		{
			action();
			return Task.CompletedTask;
		}, "INITIATION:UI");
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);

	private static void MarkStepFromHealth(SetupDiagnosticStep step)
	{
		step.State = step.ViewModel.CurrentHealth switch
		{
			HealthState.Healthy => SetupDiagnosticStepState.Verified,
			HealthState.OptionalMissing => SetupDiagnosticStepState.Skipped,
			_ => SetupDiagnosticStepState.Failed
		};
	}
}
