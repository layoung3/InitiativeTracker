"use strict";

const playerConnection =
    new signalR.HubConnectionBuilder()
        .withUrl("/hubs/encounter")
        .withAutomaticReconnect()
        .build();

playerConnection.on(
    "PlayerRemoved",
    () => {

        window.location.href =
            "/join";
    });

async function startPlayerConnection() {

    try {

        await playerConnection.start();

    }
    catch (error) {

        console.error(
            "Unable to connect player client.",
            error);

        setTimeout(
            startPlayerConnection,
            5000);
    }
}

playerConnection.onclose(
    () => {

        setTimeout(
            startPlayerConnection,
            5000);
    });

startPlayerConnection();