export function setupFocusTracking(bridge) {
    const emitFocusChange = (isFocused) => {
        if (bridge.lastFocusState === isFocused) return;
        bridge.lastFocusState = isFocused;
        bridge.send("FOCUS_CHANGE", isFocused);
    };

    const onFocus = () => emitFocusChange(true);
    const onBlur = () => emitFocusChange(false);

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

    window.addEventListener('focus', onFocus);
    window.addEventListener('blur', onBlur);
    document.addEventListener('keydown', onKeyDown);

    return () => {
        window.removeEventListener('focus', onFocus);
        window.removeEventListener('blur', onBlur);
        document.removeEventListener('keydown', onKeyDown);
    };
}
