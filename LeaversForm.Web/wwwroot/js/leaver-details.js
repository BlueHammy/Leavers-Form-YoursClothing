(() => {
    const root = document.getElementById('leaver-details');
    if (!root || root.dataset.pending !== 'true') {
        return;
    }

    const requestId = root.dataset.requestId;
    const pollMs = 20000;
    let timerId = null;

    const applyStatus = (item) => {
        const label = root.querySelector('[data-status-label]');
        if (label) {
            label.textContent = item.statusLabel || item.status;
        }

        const detail = root.querySelector('[data-status-detail]');
        if (detail) {
            detail.textContent = item.statusDetail || '';
        }

        if (item.status !== 'Pending') {
            window.location.reload();
        }
    };

    const refreshStatus = async () => {
        if (document.hidden) {
            return;
        }

        try {
            const response = await fetch(`/api/leavers/requests/${requestId}/status`, {
                headers: { Accept: 'application/json' },
                credentials: 'same-origin'
            });

            if (!response.ok) {
                return;
            }

            applyStatus(await response.json());
        }
        catch {
            // Ignore transient network errors.
        }
    };

    timerId = window.setInterval(refreshStatus, pollMs);
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
            if (timerId !== null) {
                window.clearInterval(timerId);
                timerId = null;
            }
            return;
        }

        refreshStatus();
        if (timerId === null) {
            timerId = window.setInterval(refreshStatus, pollMs);
        }
    });
})();
