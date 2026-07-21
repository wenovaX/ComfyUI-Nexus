export function setupFocusTracking(bridge) {
    const emitInputMode = (isEditing) => {
        if (bridge.lastInputMode === isEditing) return;
        bridge.lastInputMode = isEditing;
        bridge.send("WEB_INPUT_MODE", isEditing);
    };

    const isEditable = (element) => {
        if (!(element instanceof Element)) return false;
        return element.matches("input, textarea, select, [contenteditable='true'], [role='textbox']") ||
            Boolean(element.closest("[contenteditable='true'], [role='textbox']"));
    };

    const hasModal = () => Boolean(document.querySelector(
        "[role='dialog'][aria-modal='true'], .p-dialog-mask, .p-dialog"
    ));
    const syncInputMode = () => emitInputMode(isEditable(document.activeElement) || hasModal());
    const onFocusIn = () => syncInputMode();
    const onFocusOut = () => queueMicrotask(syncInputMode);

    // Mirror Ctrl+W tab-close behavior inside the embedded app.
    const onKeyDown = (e) => {
        if (e.ctrlKey && e.key.toLowerCase() === 'w') {
            e.preventDefault();
            const activeIndex = bridge.getActiveWorkflowIndex?.() ?? -1;
            if (activeIndex >= 0) {
                window.NexusAction?.("CLOSE_WORKFLOW", { index: activeIndex });
            }
        }
    };

    document.addEventListener('focusin', onFocusIn, true);
    document.addEventListener('focusout', onFocusOut, true);
    document.addEventListener('keydown', onKeyDown);

    return () => {
        document.removeEventListener('focusin', onFocusIn, true);
        document.removeEventListener('focusout', onFocusOut, true);
        document.removeEventListener('keydown', onKeyDown);
    };
}
