"use strict";

document.addEventListener("DOMContentLoaded", () => {

    /* ==========================================================
       DISPLAY THEME
       ========================================================== */

    const allowedThemes =
        new Set([
            "dark",
            "light",
            "parchment",
            "arcane",
            "infernal",
            "frost",
            "forest",
            "blood-moon"
        ]);

    const themeStorageKey =
        "initiative-display-theme";

    const themeOptions =
        Array.from(
            document.querySelectorAll(
                "[data-display-theme-option]"));

    const fullscreenButton =
        document.getElementById(
            "displayFullscreenButton");


    function normalizeTheme(theme) {

        return allowedThemes.has(theme)
            ? theme
            : "dark";
    }


    function applyTheme(
        theme,
        persist = true) {

        const normalized =
            normalizeTheme(theme);


        document.documentElement
            .dataset
            .displayTheme =
            normalized;


        themeOptions.forEach(
            option => {

                option.checked =
                    option.value ===
                    normalized;
            });


        if (persist) {

            localStorage.setItem(
                themeStorageKey,
                normalized);
        }
    }


    function loadSavedTheme() {

        return normalizeTheme(
            localStorage.getItem(
                themeStorageKey));
    }


    themeOptions.forEach(
        option => {

            option.addEventListener(
                "change",
                () => {

                    if (option.checked) {

                        applyTheme(
                            option.value,
                            true);
                    }
                });
        });


    /*
     * Use the display's locally saved theme immediately so the
     * page does not flash back to Dark while SignalR connects.
     * Once connected, the hub's persisted theme becomes
     * authoritative and is synchronized below.
     */
    applyTheme(
        loadSavedTheme(),
        false);


    function handleRemoteTheme(
        theme) {

        applyTheme(
            theme,
            true);
    }


    /* ==========================================================
       FULLSCREEN
       ========================================================== */

    function updateFullscreenButton() {

        if (!fullscreenButton) {
            return;
        }

        fullscreenButton.textContent =
            document.fullscreenElement
                ? "Exit Fullscreen"
                : "Enter Fullscreen";
    }


    fullscreenButton
        ?.addEventListener(
            "click",
            async () => {

                try {

                    if (document.fullscreenElement) {

                        await document
                            .exitFullscreen();
                    }
                    else {

                        await document
                            .documentElement
                            .requestFullscreen();
                    }
                }
                catch (error) {

                    console.warn(
                        "Fullscreen is not available.",
                        error);
                }
            });


    document.addEventListener(
        "fullscreenchange",
        updateFullscreenButton);

    updateFullscreenButton();


    /* ==========================================================
       CONNECTION STATUS
       ========================================================== */

    const connectionStatus =
        document.getElementById(
            "displayConnectionStatus");

    const connectionText =
        document.getElementById(
            "displayConnectionText");


    function setConnectionStatus(
        state,
        text) {

        if (!connectionStatus ||
            !connectionText) {
            return;
        }


        connectionStatus
            .classList
            .remove(
                "is-connected",
                "is-disconnected",
                "is-connecting");


        connectionStatus
            .classList
            .add(
                `is-${state}`);


        connectionText.textContent =
            text;
    }


    /* ==========================================================
       SIGNALR
       ========================================================== */

    const connection =
        new signalR
            .HubConnectionBuilder()
            .withUrl(
                "/hubs/encounter")
            .withAutomaticReconnect()
            .build();


    connection.on(
        "EncounterUpdated",
        state => {

            renderEncounter(
                state);
        });


    connection.on(
        "DisplayThemeUpdated",
        theme => {

            handleRemoteTheme(
                theme);
        });


    connection.onreconnecting(
        error => {

            console.warn(
                "Initiative display disconnected. Reconnecting...",
                error);

            setConnectionStatus(
                "connecting",
                "Reconnecting");
        });


    connection.onreconnected(
        async () => {

            console.log(
                "Initiative display reconnected.");

            setConnectionStatus(
                "connected",
                "Connected");

            await syncDisplayTheme();
            await syncEncounter();
        });


    connection.onclose(
        error => {

            console.error(
                "Initiative display connection closed.",
                error);

            setConnectionStatus(
                "disconnected",
                "Disconnected");

            window.setTimeout(
                startConnection,
                5000);
        });


    connection.on(
        "DeviceRevoked",
        () => {

            window.location.href =
                "/display/pair";
        });


    async function startConnection() {

        if (connection.state !==
            signalR.HubConnectionState.Disconnected) {

            return;
        }


        try {

            setConnectionStatus(
                "connecting",
                "Connecting");


            await connection.start();


            console.log(
                "Initiative display connected.");


            setConnectionStatus(
                "connected",
                "Connected");


            await syncDisplayTheme();
            await syncEncounter();
        }
        catch (error) {

            console.error(
                "Unable to connect to initiative hub.",
                error);


            setConnectionStatus(
                "disconnected",
                "Retrying");


            window.setTimeout(
                startConnection,
                5000);
        }
    }


    /* ==========================================================
       ENCOUNTER RENDERING
       ========================================================== */

    function renderEncounter(
        state) {

        renderHeader(
            state);

        renderCombatants(
            state.combatants);
    }


    function renderHeader(
        state) {

        const roundDisplay =
            document.getElementById(
                "roundDisplay");

        const turnDisplay =
            document.getElementById(
                "turnDisplay");


        if (!roundDisplay ||
            !turnDisplay) {
            return;
        }


        roundDisplay
            .replaceChildren();

        turnDisplay
            .replaceChildren();


        const roundLabel =
            document.createElement(
                "span");

        roundLabel.className =
            "display-status-label";


        const roundValue =
            document.createElement(
                "strong");

        roundValue.className =
            "display-status-value";


        if (!state.isStarted) {

            roundLabel.textContent =
                "Encounter";

            roundValue.textContent =
                "Setup";

            roundValue.classList.add(
                "display-status-value--text");


            roundDisplay.append(
                roundLabel,
                roundValue);

            return;
        }


        roundLabel.textContent =
            "Round";

        roundValue.textContent =
            state.round;


        roundDisplay.append(
            roundLabel,
            roundValue);


        const turnLabel =
            document.createElement(
                "span");

        turnLabel.className =
            "display-status-label";

        turnLabel.textContent =
            "Turn";


        const turnValue =
            document.createElement(
                "strong");

        turnValue.className =
            "display-status-value";


        if (state.currentTurnNumber > 0) {

            const current =
                document.createTextNode(
                    state.currentTurnNumber);

            const divider =
                document.createElement(
                    "span");

            divider.className =
                "display-status-divider";

            divider.textContent =
                " / ";

            const total =
                document.createTextNode(
                    state.activeCombatantCount);


            turnValue.append(
                current,
                divider,
                total);
        }
        else {

            turnValue.textContent =
                "—";
        }


        turnDisplay.append(
            turnLabel,
            turnValue);
    }


    function renderCombatants(
        combatants) {

        const initiativeList =
            document.getElementById(
                "initiativeList");


        if (!initiativeList) {
            return;
        }


        const retainedPortraits = new Map(Array.from(initiativeList.querySelectorAll(".initiative-row"), row =>
            [row.dataset.combatantId, row.querySelector(".tracker-display-portrait")]));

        initiativeList
            .replaceChildren();


        if (!combatants ||
            combatants.length === 0) {

            initiativeList.appendChild(
                createEmptyDisplay());

            return;
        }


        for (const combatant
            of combatants) {

            initiativeList.appendChild(
                createCombatantRow(
                    combatant, retainedPortraits.get(String(combatant.id))));
        }


        scrollToCurrentCombatant();
    }


    function createEmptyDisplay() {

        const empty =
            document.createElement(
                "div");

        empty.className =
            "empty-display";


        const symbol = document.getElementById("displayBrandTemplate")?.content.cloneNode(true)
            ?? document.createTextNode("Initiative Tracker");


        const title =
            document.createElement(
                "div");

        title.className =
            "empty-display-title";

        title.textContent =
            "Waiting for encounter";


        const detail =
            document.createElement(
                "div");

        detail.className =
            "empty-display-detail";

        detail.textContent =
            "The initiative order will appear here when combat begins.";


        empty.append(
            symbol,
            title,
            detail);


        return empty;
    }


    function createCombatantRow(
        combatant, retainedPortrait) {

        const row =
            document.createElement(
                "div");

        row.className =
            "initiative-row";

        row.dataset.combatantId =
            combatant.id;


        if (combatant.isCurrent) {

            row.classList.add(
                "is-current");
        }


        if (!combatant.isActive) {

            row.classList.add(
                "is-inactive");
        }


        const indicator =
            document.createElement(
                "div");

        indicator.className =
            "turn-indicator";

        indicator.setAttribute(
            "aria-hidden",
            "true");

        indicator.textContent =
            combatant.isCurrent
                ? "▶"
                : "";


        const name =
            document.createElement(
                "div");

        name.className =
            "combatant-name";

        const portraitUrl = /^\/media\/character\/[0-9a-f-]{36}\/portrait$/i.test(combatant.portraitUrl ?? "")
            ? combatant.portraitUrl : "/images/default-character.svg";
        const portrait = retainedPortrait?.dataset.portraitResource === portraitUrl ? retainedPortrait : document.createElement("img");
        portrait.width = 48;
        portrait.height = 48;
        portrait.className = "tracker-portrait tracker-portrait--combat tracker-display-portrait";
        portrait.alt = `Portrait of ${combatant.name}`;
        portrait.dataset.trackerMedia = "";
        if (portrait.dataset.portraitResource !== portraitUrl) portrait.src = portraitUrl;
        portrait.dataset.portraitResource = portraitUrl;
        name.appendChild(portrait);


        const nameText =
            document.createElement(
                "div");

        nameText.className =
            "combatant-name-text";

        nameText.textContent =
            combatant.name;


        const nameHeading = document.createElement("div");
        nameHeading.className = "combatant-name-heading";
        nameHeading.appendChild(nameText);
        name.appendChild(nameHeading);


        const conditions =
            combatant.conditions ??
            [];


        if (combatant.exhaustionLevel > 0) {
            const exhaustion = document.createElement("span");
            exhaustion.className = "condition-badge";
            exhaustion.textContent = `Exhaustion ${combatant.exhaustionLevel}`;
            nameHeading.appendChild(exhaustion);
        }

        if (conditions.length > 0) {

            const conditionList =
                document.createElement(
                    "div");

            conditionList.className =
                "condition-badges";


            for (const condition
                of conditions) {

                const badge =
                    document.createElement(
                        "span");


                const styleKey =
                    /^[a-z0-9-]+$/
                        .test(
                            condition.styleKey ??
                            "")
                        ? condition.styleKey
                        : "custom";


                badge.classList.add(
                    "condition-badge",
                    `condition-${styleKey}`);


                badge.textContent =
                    condition.name;


                conditionList.appendChild(
                    badge);
            }


            name.appendChild(
                conditionList);
        }


        row.append(
            indicator,
            name);


        const hpIsVisible =
            combatant.currentHp !== null ||
            combatant.maxHp !== null;


        if (hpIsVisible) {

            row.appendChild(
                createHpDisplay(
                    combatant));
        }


        return row;
    }


    function createHpDisplay(
        combatant) {

        const hp =
            document.createElement(
                "div");

        hp.className =
            "combatant-hp";


        const label =
            document.createElement(
                "span");

        label.className =
            "combatant-hp-label";

        label.textContent =
            "HP";


        const value =
            document.createElement(
                "span");

        value.className =
            "combatant-hp-value";


        const current =
            document.createTextNode(
                combatant.currentHp ??
                "—");


        const divider =
            document.createElement(
                "span");

        divider.className =
            "combatant-hp-divider";

        divider.textContent =
            " / ";


        const maximum =
            document.createTextNode(
                combatant.maxHp ??
                "—");


        value.append(
            current,
            divider,
            maximum);


        hp.append(
            label,
            value);


        if (combatant.temporaryHp !== null &&
            combatant.temporaryHp > 0) {

            const temporary =
                document.createElement(
                    "span");

            temporary.className =
                "combatant-temp-hp";

            temporary.textContent =
                `+${combatant.temporaryHp} Temp`;


            hp.appendChild(
                temporary);
        }


        return hp;
    }


    function scrollToCurrentCombatant() {

        const current =
            document.querySelector(
                ".initiative-row.is-current");


        if (!current) {
            return;
        }


        const reduceMotion =
            window.matchMedia(
                "(prefers-reduced-motion: reduce)")
                .matches;


        // Scroll only the bounded list. scrollIntoView can also move the page
        // and its ancestors, taking the round/turn header out of the viewport.
        const list = document.getElementById("initiativeList");
        if (!list) return;
        const rowBounds = current.getBoundingClientRect();
        const listBounds = list.getBoundingClientRect();
        const centeringSpace = Math.max(0, (list.clientHeight - rowBounds.height) / 2);

        list.scrollTo({
            top: list.scrollTop + rowBounds.top - listBounds.top - centeringSpace,
            behavior:
                reduceMotion
                    ? "auto"
                    : "smooth"
        });
    }


    async function syncDisplayTheme() {

        try {

            const theme =
                await connection.invoke(
                    "GetDisplayTheme");


            handleRemoteTheme(
                theme);
        }
        catch (error) {

            console.error(
                "Unable to synchronize display theme.",
                error);
        }
    }


    async function syncEncounter() {

        try {

            const state =
                await connection.invoke(
                    "GetEncounterState");


            renderEncounter(
                state);
        }
        catch (error) {

            console.error(
                "Unable to synchronize encounter state.",
                error);
        }
    }


    startConnection();
});
