"use strict";

const playerThemeNames =
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

const playerDarkThemes =
    new Set([
        "dark",
        "arcane",
        "infernal",
        "frost",
        "forest",
        "blood-moon"
    ]);

const playerThemeStorageKey =
    "initiative-player-theme";


function normalizePlayerTheme(
    theme) {

    return playerThemeNames.has(theme)
        ? theme
        : "light";
}


function applyPlayerTheme(
    theme,
    persist = true) {

    const normalized =
        normalizePlayerTheme(theme);


    document.documentElement
        .dataset
        .playerTheme =
        normalized;


    document.documentElement
        .setAttribute(
            "data-bs-theme",
            playerDarkThemes.has(normalized)
                ? "dark"
                : "light");


    document
        .querySelectorAll(
            "[data-player-theme-choice]")
        .forEach(
            button => {

                const isActive =
                    button.dataset
                        .playerThemeChoice ===
                    normalized;


                button.classList.toggle(
                    "is-active",
                    isActive);


                button.setAttribute(
                    "aria-pressed",
                    isActive
                        ? "true"
                        : "false");
            });


    if (persist) {

        localStorage.setItem(
            playerThemeStorageKey,
            normalized);
    }
}


function loadPlayerTheme() {

    return normalizePlayerTheme(
        localStorage.getItem(
            playerThemeStorageKey));
}


applyPlayerTheme(
    loadPlayerTheme(),
    false);


document
    .querySelectorAll(
        "[data-player-theme-choice]")
    .forEach(
        button => {

            button.addEventListener(
                "click",
                () => {

                    applyPlayerTheme(
                        button.dataset
                            .playerThemeChoice);
                });
        });


window.addEventListener(
    "storage",
    event => {

        if (event.key !==
            playerThemeStorageKey) {
            return;
        }


        applyPlayerTheme(
            normalizePlayerTheme(
                event.newValue),
            false);
    });