export function sendToNative(type, data) {
    if (!type) {
        return false;
    }

    if (typeof window.__nexusNative?.post === "function") {
        window.__nexusNative.post(type, data);
        return true;
    }

    if (window.chrome?.webview?.postMessage) {
        window.chrome.webview.postMessage({
            type,
            data,
            timestamp: Date.now()
        });
        return true;
    }

    return false;
}
