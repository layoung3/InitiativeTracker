// Render only on server-verified physical HTTPS requests. Never run on legacy HTTP.
// All decisions POST to the existing physical endpoint with its generated CSRF token.
(() => {
    'use strict';
    const dialog = document.getElementById('physicalApprovalOverlay');
    if (!dialog || location.protocol !== 'https:') return;
    const reference = document.getElementById('physicalApprovalReference');
    const purpose = document.getElementById('physicalApprovalPurpose');
    const error = document.getElementById('physicalApprovalError');
    const approve = document.getElementById('physicalApproveButton');
    const deny = document.getElementById('physicalDenyButton');
    const endpoint = '/secure-access/physical';
    let pending = null;
    let busy = false;
    let running = true;

    async function refresh() {
        if (busy || document.hidden) return;
        busy = true;
        try {
            const response = await fetch(endpoint, {credentials: 'same-origin', cache: 'no-store', redirect: 'error'});
            if (!response.ok) throw new Error('Physical approval unavailable');
            const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
            const section = [...doc.querySelectorAll('section[data-request]')]
                .find(node => node.dataset.physicalApproved === 'false');
            const token = doc.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!section || !token) {
                pending = null;
                if (dialog.open) dialog.close();
                return;
            }
            const id = section.dataset.request;
            const browser = section.querySelector('strong')?.textContent?.trim();
            if (!/^[0-9a-fA-F-]{36}$/.test(id || '') || !/^[0-9A-F]{32}$/i.test(browser || '')) {
                throw new Error('Invalid pending approval');
            }
            if (pending?.id !== id) error.textContent = '';
            pending = {id, token};
            reference.textContent = browser;
            purpose.textContent = section.querySelector('p')?.textContent?.replace(/^Browser reference.*?·\s*/, '') || 'DM authorization';
            if (!dialog.open) dialog.showModal();
        } catch {
            // A transient network failure must never be interpreted as approval.
            // Keep the existing decision visible; no credentials leave this origin.
            error.textContent = 'Unable to refresh requests. No decision was made.';
        } finally {
            busy = false;
        }
    }

    async function decide(handler) {
        if (busy || !pending) return;
        busy = true;
        approve.disabled = deny.disabled = true;
        const {id, token} = pending;
        try {
            const body = new URLSearchParams({id, __RequestVerificationToken: token});
            const response = await fetch(endpoint + '?handler=' + handler, {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: {'Content-Type': 'application/x-www-form-urlencoded'},
                body, redirect: 'follow'
            });
            if (!response.ok || new URL(response.url).origin !== location.origin)
                throw new Error('Approval was rejected');
            pending = null;
            error.textContent = '';
            if (dialog.open) dialog.close();
        } catch {
            error.textContent = 'Unable to complete the decision. Confirm the request has not expired.';
        } finally {
            approve.disabled = deny.disabled = false;
            busy = false;
            await refresh();
        }
    }
    approve.addEventListener('click', () => decide('Approve'));
    deny.addEventListener('click', () => decide('Deny'));
    // Prevent accidental dismissal without an explicit decision.
    dialog.addEventListener('cancel', event => event.preventDefault());
    async function loop() {
        if (!running) return;
        await refresh();
        if (running) window.setTimeout(loop, 3000);
    }
    window.addEventListener('pagehide', () => { running = false; });
    loop();
})();
