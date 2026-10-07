(() => {
    const tbody = document.getElementById('leaver-requests-body');
    if (!tbody) {
        return;
    }

    const pollMs = 20000;
    const search = tbody.dataset.search || '';
    let timerId = null;
    let inFlight = false;

    const hasActiveRows = () =>
        tbody.querySelector('[data-active="true"]') !== null;

    const setPollingIndicator = (visible) => {
        const indicator = document.getElementById('leaver-list-live-indicator');
        if (indicator) {
            indicator.hidden = !visible;
        }
    };

    const applyStatus = (item) => {
        const row = tbody.querySelector(`tr[data-request-id="${item.id}"]`);
        if (!row) {
            return;
        }

        const statusClass = `status-${item.status.toLowerCase()}`;
        row.className = statusClass;
        row.dataset.active = item.isActive ? 'true' : 'false';

        const label = row.querySelector('[data-status-label]');
        const displayStatus = item.statusLabel || item.status;
        if (label && label.textContent !== displayStatus) {
            label.textContent = displayStatus;
            label.classList.add('status-label-updated');
            window.setTimeout(() => label.classList.remove('status-label-updated'), 600);
        }

        const detail = row.querySelector('[data-status-detail]');
        if (detail) {
            detail.textContent = item.statusDetail || '';
            detail.className = item.status === 'Failed'
                ? 'failed-note'
                : item.status === 'Pending'
                    ? 'pending-note'
                    : 'status-subtle';
        }

        const asap = row.querySelector('[data-asap-badge]');
        if (asap) {
            asap.hidden = !item.disableAsap;
        }

        const retry = row.querySelector('[data-retry-action]');
        if (retry) {
            retry.hidden = !item.showRetry;
        }
    };

    const refreshStatuses = async () => {
        if (inFlight || document.hidden) {
            return;
        }

        inFlight = true;
        try {
            const query = search ? `?search=${encodeURIComponent(search)}` : '';
            const response = await fetch(`/api/leavers/requests/status${query}`, {
                headers: { Accept: 'application/json' },
                credentials: 'same-origin'
            });

            if (!response.ok) {
                return;
            }

            const items = await response.json();
            items.forEach(applyStatus);

            const stillActive = items.some((item) => item.isActive);
            setPollingIndicator(stillActive);

            if (!stillActive && timerId !== null) {
                window.clearInterval(timerId);
                timerId = null;
            }
        }
        catch {
            // Ignore transient network errors — next poll will retry.
        }
        finally {
            inFlight = false;
        }
    };

    const startPolling = () => {
        if (timerId !== null) {
            return;
        }

        setPollingIndicator(true);
        timerId = window.setInterval(refreshStatuses, pollMs);
    };

    if (hasActiveRows()) {
        startPolling();
    }

    const highlightId = tbody.dataset.highlightId;
    if (highlightId) {
        const row = tbody.querySelector(`tr[data-request-id="${highlightId}"]`);
        if (row) {
            row.scrollIntoView({ behavior: 'smooth', block: 'center' });
            window.setTimeout(() => row.classList.remove('row-highlight'), 8000);
        }
    }

    document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
            if (timerId !== null) {
                window.clearInterval(timerId);
                timerId = null;
            }
            return;
        }

        if (hasActiveRows()) {
            refreshStatuses();
            startPolling();
        }
    });
})();
