"use strict";

document.addEventListener("DOMContentLoaded", () => {

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

    const darkThemes =
        new Set([
            "dark",
            "arcane",
            "infernal",
            "frost",
            "forest",
            "blood-moon"
        ]);

    const storageKey =
        "initiative-dm-theme";

    const themeButtons =
        Array.from(
            document.querySelectorAll(
                "[data-dm-theme-choice]"));


    function normalizeTheme(theme) {

        return allowedThemes.has(theme)
            ? theme
            : "light";
    }


    function applyTheme(
        theme,
        persist = true) {

        const normalized =
            normalizeTheme(theme);


        document.documentElement
            .dataset
            .dmTheme =
            normalized;


        document.documentElement
            .setAttribute(
                "data-bs-theme",
                darkThemes.has(normalized)
                    ? "dark"
                    : "light");


        themeButtons.forEach(
            button => {

                const isActive =
                    button.dataset
                        .dmThemeChoice ===
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
                storageKey,
                normalized);
        }
    }


    function loadSavedTheme() {

        return normalizeTheme(
            localStorage.getItem(
                storageKey));
    }


    themeButtons.forEach(
        button => {

            button.addEventListener(
                "click",
                () => {

                    applyTheme(
                        button.dataset
                            .dmThemeChoice);
                });
        });


    applyTheme(
        loadSavedTheme(),
        false);
});