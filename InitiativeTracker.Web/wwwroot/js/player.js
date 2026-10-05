"use strict";

const playerConnection =
    new signalR
        .HubConnectionBuilder()
        .withUrl(
            "/hubs/encounter")
        .withAutomaticReconnect()
        .build();

let playerReloadPending =
    false;


playerConnection.on(
    "PlayerRemoved",
    () => {

        window.location.href =
            "/join";
    });


playerConnection.on(
    "EncounterUpdated",
    () => {

        if (playerReloadPending) {
            return;
        }

        playerReloadPending =
            true;

        window.location.reload();
    });


playerConnection.onreconnecting(
    error => {

        console.warn(
            "Player connection interrupted. Reconnecting...",
            error);
    });


playerConnection.onreconnected(
    () => {

        /*
         * Refresh from the server after reconnect so HP,
         * initiative, conditions, and controller state all
         * come from authoritative encounter state.
         */
        window.location.reload();
    });


playerConnection.onclose(
    error => {

        console.warn(
            "Player connection closed. Retrying...",
            error);

        window.setTimeout(
            startPlayerConnection,
            5000);
    });


async function startPlayerConnection() {

    if (playerConnection.state !==
        signalR.HubConnectionState.Disconnected) {

        return;
    }

    try {

        await playerConnection.start();
    }
    catch (error) {

        console.error(
            "Unable to connect player client.",
            error);

        window.setTimeout(
            startPlayerConnection,
            5000);
    }
}


startPlayerConnection();