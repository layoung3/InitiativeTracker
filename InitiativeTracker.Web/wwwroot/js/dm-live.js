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