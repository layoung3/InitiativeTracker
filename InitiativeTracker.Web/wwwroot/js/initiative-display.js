"use strict";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/encounter")
    .withAutomaticReconnect()
    .build();

connection.on("EncounterUpdated", state => {
    renderEncounter(state);
});

connection.onreconnecting(error => {
    console.warn("Initiative display disconnected. Reconnecting...", error);
});

connection.onreconnected(async () => {

    console.log(
        "Initiative display reconnected.");

    await syncEncounter();
});

connection.onclose(error => {
    console.error("Initiative display connection closed.", error);

    setTimeout(startConnection, 5000);
});

connection.on(
    "DeviceRevoked",
    () => {

        window.location.href =
            "/display/pair";
    });

async function startConnection() {
    try {
        await connection.start();

        console.log(
            "Initiative display connected.");

        await syncEncounter();
    }
    catch (error) {
        console.error(
            "Unable to connect to initiative hub.",
            error);

        setTimeout(startConnection, 5000);
    }
}

function renderEncounter(state) {

    renderHeader(state);
    renderCombatants(state.combatants);
}

function renderHeader(state) {

    const roundDisplay =
        document.getElementById("roundDisplay");

    const turnDisplay =
        document.getElementById("turnDisplay");

    if (!state.isStarted) {

        roundDisplay.innerHTML =
            "<span>ENCOUNTER SETUP</span>";

        turnDisplay.innerHTML = "";

        return;
    }

    roundDisplay.innerHTML =
        `<span>
            ROUND
            <strong>${state.round}</strong>
        </span>`;

    turnDisplay.innerHTML =
        `<span>
            TURN
            <strong>
                ${state.currentTurnNumber}
                /
                ${state.activeCombatantCount}
            </strong>
        </span>`;
}

function renderCombatants(combatants) {

    const initiativeList =
        document.getElementById("initiativeList");

    initiativeList.innerHTML = "";

    if (!combatants || combatants.length === 0) {

        const emptyDisplay =
            document.createElement("div");

        emptyDisplay.className =
            "empty-display";

        emptyDisplay.textContent =
            "Waiting for encounter...";

        initiativeList.appendChild(emptyDisplay);

        return;
    }

    for (const combatant of combatants) {

        const row =
            document.createElement("div");

        row.classList.add(
            "initiative-row",
            "align-items-center",
            "border",
            "rounded",
            "px-3",
            "py-2",
            "mb-2",
            "fs-4"
        );

        if (combatant.isCurrent) {
            row.classList.add(
                "current",
                "border-primary"
            );
        }

        if (!combatant.isActive) {
            row.classList.add("opacity-50");
        }

        const indicator =
            document.createElement("div");

        indicator.className =
            "turn-indicator";

        indicator.textContent =
            combatant.isCurrent ? "➜" : "";

        const name =
            document.createElement("div");

        name.className =
            "combatant-name";

        name.textContent =
            combatant.name;

        const hp =
            document.createElement("div");

        hp.className = "combatant-hp ms-3";

        if (combatant.currentHp !== null ||
            combatant.maxHp !== null) {

            const current =
                combatant.currentHp ?? "—";

            const max =
                combatant.maxHp ?? "—";

            hp.textContent =
                `${current} / ${max}`;
        }

        row.appendChild(indicator);
        row.appendChild(name);
        row.appendChild(hp);

        initiativeList.appendChild(row);
    }

    scrollToCurrentCombatant();
}

function scrollToCurrentCombatant() {

    const current =
        document.querySelector(
            ".initiative-row.current");

    if (!current) {
        return;
    }

    current.scrollIntoView({
        behavior: "smooth",
        block: "center"
    });
}

async function syncEncounter() {
    try {
        const state =
            await connection.invoke(
                "GetEncounterState");

        renderEncounter(state);
    }
    catch (error) {
        console.error(
            "Unable to synchronize encounter state.",
            error);
    }
}

startConnection();