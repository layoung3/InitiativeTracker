// Only the requesting browser can finish its own bound, expiring approval.
// The physical screen never sends a token, credential, or cookie to this page.
(() => {
    'use strict';
    const form = document.getElementById('finishPhysicalApproval');
    if (!form) return;
    const request = () => {
        if (form.dataset.physicalApproved === 'true') {
            // Submit the Razor-generated antiforgery token by ordinary POST.
            form.requestSubmit();
            return;
        }
        // A fresh GET reads server-side approval state. No background POST,
        // polling API, bearer code, cross-origin request or trust bypass.
        window.location.reload();
    };
    window.setTimeout(request, form.dataset.physicalApproved === 'true' ? 100 : 4000);
})();
