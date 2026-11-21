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
    const refresh = () => {
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
