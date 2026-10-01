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

    /*
        currentTurnNumber will be 0 when the actual
        current combatant is hidden from the public display.

        In that case we deliberately do not show:

            TURN 0 / 4
    */
    if (state.currentTurnNumber > 0) {

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
    else {

        turnDisplay.innerHTML =
            `<span>
                TURN
                <strong>—</strong>
            </span>`;
    }
}

function renderCombatants(combatants) {

    const initiativeList =
        document.getElementById("initiativeList");

    initiativeList.innerHTML = "";

    if (!combatants ||
        combatants.length === 0) {

        const emptyDisplay =
            document.createElement("div");

        emptyDisplay.className =
            "empty-display";

        emptyDisplay.textContent =
            "Waiting for encounter...";

        initiativeList.appendChild(
            emptyDisplay);

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

            row.classList.add(
                "opacity-50");
        }


        /*
         * TURN INDICATOR
         */
        const indicator =
            document.createElement("div");

        indicator.className =
            "turn-indicator";

        indicator.textContent =
            combatant.isCurrent
                ? "➜"
                : "";


        /*
         * NAME
         */
        const name = document.createElement("div");

        name.className = "combatant-name";


        const nameText =
            document.createElement("div");

        nameText.textContent = combatant.name;

        name.appendChild(nameText);


        const conditions = combatant.conditions ?? [];


        if (conditions.length > 0) {

            const conditionList =
                document.createElement(
                    "div");

            conditionList.className = "condition-badges mt-1";


            for (const condition
                of conditions) {

                const badge =
                    document.createElement(
                        "span");

                const styleKey =
                    /^[a-z0-9-]+$/
                        .test(
                            condition.styleKey ?? "")
                        ? condition.styleKey
                        : "custom";


                badge.classList.add(
                    "condition-badge",
                    `condition-${styleKey}`
                );

                badge.textContent = condition.name;


                conditionList.appendChild(badge);
            }


            name.appendChild(conditionList);
        }


        /*
         * HP
         *
         * If CurrentHp and MaxHp are both null,
         * the server intentionally hid HP.
         *
         * In that case we do not render an HP
         * value at all.
         */
        const hp = document.createElement("div");

        hp.className =
            "combatant-hp ms-3";


        const hpIsVisible =
            combatant.currentHp !== null ||
            combatant.maxHp !== null;


        if (hpIsVisible) {

            const current =
                combatant.currentHp ?? "—";

            const max =
                combatant.maxHp ?? "—";


            const hpValue =
                document.createElement("span");

            hpValue.textContent =
                `${current} / ${max}`;

            hp.appendChild(
                hpValue);


            /*
             * TEMPORARY HP
             */
            if (combatant.temporaryHp !== null &&
                combatant.temporaryHp > 0) {

                const tempHp =
                    document.createElement("span");

                tempHp.className =
                    "badge text-bg-info ms-2";

                tempHp.textContent =
                    `+${combatant.temporaryHp} Temp`;

                hp.appendChild(
                    tempHp);
            }
        }


        row.appendChild(
            indicator);

        row.appendChild(
            name);

        /*
         * Only add the HP element if HP
         * information is actually visible.
         *
         * This prevents an empty HP area from
         * appearing for HP-hidden enemies.
         */
        if (hpIsVisible) {

            row.appendChild(
                hp);
        }


        initiativeList.appendChild(
            row);
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