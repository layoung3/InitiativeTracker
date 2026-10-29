"use strict";

const dmConnection =
    new signalR.HubConnectionBuilder()
        .withUrl("/hubs/encounter")
        .withAutomaticReconnect()
        .build();

let dmReloadPending = false;

dmConnection.on(
    "EncounterUpdated",
    () => {

        if (window.visibilitySaving || window.visibilityError) {
            window.visibilityRefreshPending = true;
            return;
        }
        if (dmReloadPending) {
            return;
        }

        dmReloadPending = true;

        window.location.reload();
    });

async function startDmConnection() {

    try {

        await dmConnection.start();

    }
    catch (error) {

        console.error(
            "Unable to connect DM client.",
            error);

        setTimeout(
            startDmConnection,
            5000);
    }
}

dmConnection.onclose(
    () => {

        setTimeout(
            startDmConnection,
            5000);
    });

startDmConnection();
// Serialize changes across forms; retain dirty controls on errors and allow retry.
let visibilityQueue = Promise.resolve();
let visibilityOutstanding = 0;
document.querySelectorAll("form[data-visibility-autosave]").forEach(form => {
    // Keep the server-rendered submit button as the no-JavaScript fallback.
    const retry = form.querySelector('button[type="submit"]');
    retry.hidden = true;
    retry.textContent = "Retry save";
    const status = document.createElement("span");
    status.setAttribute("role", "status");
    status.setAttribute("aria-live", "polite");
    form.appendChild(status);
    const save = () => {
        const body = new FormData(form);
        visibilityOutstanding++;
        window.visibilitySaving = true;
        retry.hidden = true;
        status.textContent = "Saving…";
        visibilityQueue = visibilityQueue.then(async () => {
            try {
                const response = await fetch(form.action, {
                    method: "POST", body, headers: { "X-Visibility-Autosave": "1" }
                });
                if (!response.ok || !(await response.json()).saved) throw new Error("Save failed");
                form.dataset.saveError = "";
                retry.hidden = true;
                status.textContent = "Saved";
            } catch {
                form.dataset.saveError = "true";
                retry.hidden = false;
                status.textContent = "Not saved. Retry the save.";
            } finally {
                visibilityOutstanding--;
                window.visibilitySaving = visibilityOutstanding > 0;
                window.visibilityError = !!document.querySelector('[data-save-error="true"]');
                if (!window.visibilitySaving && !window.visibilityError && window.visibilityRefreshPending) {
                    setTimeout(() => {
                        if (!window.visibilitySaving && !window.visibilityError) window.location.reload();
                    }, 1500);
                }
            }
        });
    };
    form.addEventListener("change", save);
    form.addEventListener("submit", event => { event.preventDefault(); save(); });
});
