namespace ComfyUI_Nexus.Configuration;

internal static class BridgeMessageTypes
{
	internal const string Heartbeat = "HEARTBEAT";
	internal const string JsLog = "JS_LOG";
	internal const string WebConsole = "WEB_CONSOLE";
	internal const string WebError = "WEB_ERROR";
	internal const string BootReady = "BOOT_READY";
	internal const string RefreshRequest = "REFRESH_REQUEST";
	internal const string WorkflowSync = "WORKFLOW_SYNC";
	internal const string FocusChange = "FOCUS_CHANGE";
	internal const string GpuStats = "GPU_STATS";
	internal const string ModeUpdate = "MODE_UPDATE";
	internal const string QueueUpdate = "QUEUE_UPDATE";
	internal const string ExecutionStateSync = "EXECUTION_STATE_SYNC";
	internal const string QueueButtonStateSync = "QUEUE_BUTTON_STATE_SYNC";
	internal const string BatchCountSync = "BATCH_COUNT_SYNC";
	internal const string CursorChange = "CURSOR_CHANGE";
	internal const string UiStateUpdate = "UI_STATE_UPDATE";
	internal const string BlueprintsSync = "BLUEPRINTS_SYNC";
	internal const string MediaAssetsSync = "MEDIA_ASSETS_SYNC";
}
