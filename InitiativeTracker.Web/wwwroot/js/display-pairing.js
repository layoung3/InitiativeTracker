"use strict";

const statusElement =
    document.getElementById(
        "pairingStatus");

const restartForm =
    document.getElementById(
        "restartPairingForm");

let pairingComplete =
    false;

let pairingPollInFlight = false;

async function checkPairingStatus() {

    if (pairingComplete || pairingPollInFlight) {
        return;
    }

    pairingPollInFlight = true;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 8000);

    try {

        const response =
            await fetch(
                "/display/pair?handler=Status",
                {
                    cache: "no-store",
                    signal: controller.signal
                });

        if (!response.ok) {
            throw new Error(
                "Unable to check pairing status.");
        }

        const result =
            await response.json();

        if (pairingComplete) return;

        statusElement.textContent = "Waiting for DM approval…";


        if (result.status === "approved") {

            pairingComplete =
                true;

            statusElement.textContent =
                "Connected. Opening display...";

            window.location.href =
                result.redirect;

            return;
        }


        if (result.status === "rejected") {

            pairingComplete =
                true;

            statusElement.textContent =
                "Pairing request was rejected.";

            restartForm.classList.remove(
                "d-none");

            return;
        }


        if (result.status === "expired" ||
            result.status === "missing") {

            pairingComplete =
                true;

            statusElement.textContent =
                "Pairing request expired.";

            restartForm.classList.remove(
                "d-none");

            return;
        }

    }
    catch (error) {

        if (!pairingComplete) {
            statusElement.textContent = "Hub connection unavailable. Retrying automatically…";
        }

        console.error(
            "Pairing status check failed.",
            error);
    }
    finally {
        clearTimeout(timeout);
        pairingPollInFlight = false;
    }
}

checkPairingStatus();

setInterval(
    checkPairingStatus,
    2000);
