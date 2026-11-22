// Only trusted HTTPS /secure-access/wait renders this script. All authority
// changes use Razor CSRF forms and ordinary same-origin POST; no GET starts
// approval, no code/cookie is placed in a URL, and denial never auto-retries.
(() => {
    'use strict';
    const form = document.querySelector('form[data-auto-submit="true"]');
    if (form) {
        const submit = () => { if (!document.hidden) form.requestSubmit(); };
        if (document.hidden)
            document.addEventListener('visibilitychange', submit, {once: true});
        else submit();
        return;
    }
    if (!document.querySelector('[data-await-approval]')) return;
    const preferenceForm = document.querySelector('form[data-remember-preference]');
    let savingPreference = false;
    if (preferenceForm) {
        const checkbox = preferenceForm.querySelector('input[name=remember]');
        const status = preferenceForm.querySelector('[data-preference-status]');
        const save = preferenceForm.querySelector('[data-preference-save]');
        const persist = async () => {
            if (savingPreference) return;
            // Capture the checked value BEFORE disabling the checkbox:
            // disabled form inputs are omitted by FormData.
            const choice = new URLSearchParams(new FormData(preferenceForm));
            savingPreference = true;
            checkbox.disabled = true;
            save.disabled = true;
            status.textContent = 'Saving choice…';
            try {
                const response = await fetch(preferenceForm.action, {
                    method: 'POST', credentials: 'same-origin', cache: 'no-store',
                    redirect: 'follow', headers: {'Content-Type':'application/x-www-form-urlencoded'},
                    body: choice
                });
                if (!response.ok || new URL(response.url).origin !== location.origin)
                    throw new Error('Preference rejected');
                status.textContent = 'Choice saved for this approval.';
            } catch {
                checkbox.checked = !checkbox.checked;
                status.textContent = 'Could not save the choice. Try again.';
            } finally {
                checkbox.disabled = false;
                save.disabled = false;
                savingPreference = false;
            }
        };
        preferenceForm.addEventListener('submit', event => {
            event.preventDefault();
            persist();
        });
        checkbox.addEventListener('change', persist);
        // The explicit button remains available when JavaScript is unavailable.
        save.hidden = true;
    }
    const refresh = () => {
        if (savingPreference) { window.setTimeout(refresh, 750); return; }
        if (document.hidden) {
            document.addEventListener('visibilitychange', refresh, {once: true});
            return;
        }
        // The GET only reads the pending state and may restore independently
        // validated 30-day remembered trust; it never reissues an approval.
        window.location.replace('/secure-access/wait');
    };
    window.setTimeout(refresh, 3000);
})();
