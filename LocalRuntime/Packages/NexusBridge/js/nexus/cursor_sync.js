export function setupCursorSync(bridge) {
	let lastCursor = "";
	let lastSendTime = 0;
	let lockedCursor = "";

	const setResizeCursorLock = (cursor) => {
		if (lockedCursor === cursor) {
			return;
		}

		releaseResizeCursorLock();
		lockedCursor = cursor;

		const className = cursor === "row-resize"
			? "nexus-splitter-row-resizing"
			: "nexus-splitter-col-resizing";

		document.documentElement.classList.add(className);
		document.body?.classList.add(className);
	};

	const releaseResizeCursorLock = () => {
		if (!lockedCursor) {
			return;
		}

		document.documentElement.classList.remove("nexus-splitter-row-resizing", "nexus-splitter-col-resizing");
		document.body?.classList.remove("nexus-splitter-row-resizing", "nexus-splitter-col-resizing");
		lockedCursor = "";
	};

	const getResizeCursor = (target) => {
		try {
			const gutter = target?.closest?.('.p-splitter-gutter');
			if (!gutter) return null;

			const handle = gutter.querySelector?.('.p-splitter-gutter-handle[aria-orientation]');
			const orientation = handle?.getAttribute('aria-orientation') ||
				gutter.getAttribute('aria-orientation') ||
				target?.getAttribute?.('aria-orientation');

			// Matching working CSS mapping:
			// horizontal = col-resize (Left/Right)
			// vertical = row-resize (Up/Down)
			return (orientation === 'vertical') ? 'row-resize' : 'col-resize';
		} catch (err) {}

		return null;
	};

	const sendCursor = (cursor) => {
		const now = Date.now();
		if (cursor === lastCursor && now - lastSendTime <= 500) {
			return;
		}

		lastCursor = cursor;
		lastSendTime = now;
		bridge.send("CURSOR_CHANGE", cursor);
	};

	const beginResizeCursor = (event) => {
		const cursor = getResizeCursor(event?.target);
		if (!cursor) return;

		setResizeCursorLock(cursor);
		sendCursor(cursor);
	};

	const endResizeCursor = () => {
		releaseResizeCursorLock();
		if (lastCursor !== "" && lastCursor !== "default") {
			sendCursor("default");
		}
	};

	document.addEventListener('pointerdown', beginResizeCursor, { passive: true, capture: true });
	window.addEventListener('pointerup', endResizeCursor, { passive: true });
	window.addEventListener('pointercancel', endResizeCursor, { passive: true });
	window.addEventListener('blur', endResizeCursor, { passive: true });

	return () => {
		document.removeEventListener('pointerdown', beginResizeCursor, true);
		window.removeEventListener('pointerup', endResizeCursor);
		window.removeEventListener('pointercancel', endResizeCursor);
		window.removeEventListener('blur', endResizeCursor);
		endResizeCursor();
	};
}
