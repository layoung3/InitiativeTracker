document.addEventListener("DOMContentLoaded", () => {
    /* ==========================================================
       CONDITIONS MODAL
       ========================================================== */

    const conditionsModal = document.getElementById("conditionsModal");

    if (conditionsModal) {
        const modal = bootstrap.Modal.getOrCreateInstance(conditionsModal);

        const updateConditionsModalQuery = isOpen => {
            const url = new URL(window.location.href);

            if (isOpen) {
                url.searchParams.set("modal", "conditions");
            } else if (url.searchParams.get("modal") === "conditions") {
                url.searchParams.delete("modal");
            }

            window.history.replaceState({}, "", url);
        };

        conditionsModal.addEventListener(
            "shown.bs.modal",
            () => updateConditionsModalQuery(true));

        conditionsModal.addEventListener(
            "hidden.bs.modal",
            () => updateConditionsModalQuery(false));

        if (conditionsModal.dataset.autoOpen === "true") {
            modal.show();
        }
    }


    /* ==========================================================
       COMBAT EDITORS
       ========================================================== */

    const combatEditors = document.getElementById("combatEditors");

    if (combatEditors) {
        const getEditorName = element => {
            switch (element.id) {
                case "hpEditor":
                    return "hp";

                case "temporaryHpEditor":
                    return "temp";

                default:
                    return null;
            }
        };

        const updateEditorQuery = editor => {
            const url = new URL(window.location.href);

            if (editor) {
                url.searchParams.set("editor", editor);
            } else {
                url.searchParams.delete("editor");
            }

            window.history.replaceState({}, "", url);
        };

        combatEditors.addEventListener("shown.bs.collapse", event => {
            const editor = getEditorName(event.target);

            if (editor) {
                updateEditorQuery(editor);
            }
        });

        combatEditors.addEventListener("hidden.bs.collapse", () => {
            window.setTimeout(() => {
                const openEditor =
                    combatEditors.querySelector(".collapse.show");

                updateEditorQuery(
                    openEditor
                        ? getEditorName(openEditor)
                        : null);
            }, 0);
        });
    }


    /* ==========================================================
       CHARACTER SHEET LAYOUT
       Desktop:
           Ability Scores / Saving Throws
           Combat / Attacks & Spells
           Skills / Details / Party
           Inventory across bottom

       Phone + Tablet:
           Player-selected section order
       ========================================================== */

    const layoutContainer =
        document.getElementById("characterDashboard");

    if (!layoutContainer) {
        return;
    }

    const layoutModal =
        document.getElementById("characterLayoutModal");

    const layoutList =
        document.getElementById("characterLayoutList");

    const layoutStatus =
        document.getElementById("characterLayoutStatus");

    const resetLayoutButton =
        document.getElementById("resetCharacterLayout");

    const sections = Array.from(
        layoutContainer.querySelectorAll("[data-character-section]"));

    if (sections.length === 0) {
        return;
    }

    const sectionMap = new Map(
        sections.map(section => [
            section.dataset.characterSection,
            section
        ]));

    const characterId =
        layoutContainer.dataset.characterId ??
        window.location.pathname;

    const storageKey =
        `character-layout:${characterId}`;

    const defaultCompactOrder = [
        "combat",
        "actions",
        "abilities",
        "saving-throws",
        "skills",
        "details",
        "party",
        "inventory"
    ];

    // Compatibility with the earlier layout implementation.
    const sectionAliases = {
        "attacks-spells": "actions"
    };

    const desktopLayout = {
        left: [
            "abilities",
            "saving-throws"
        ],
        center: [
            "combat",
            "actions"
        ],
        right: [
            "skills",
            "details",
            "party"
        ]
    };

    const desktopMedia =
        window.matchMedia("(min-width: 992px)");

    const normalizeSectionName = sectionName =>
        sectionAliases[sectionName] ?? sectionName;

    const normalizeOrder = order => {
        const requestedOrder =
            Array.isArray(order)
                ? order
                : defaultCompactOrder;

        const normalized = [];

        requestedOrder.forEach(rawName => {
            const sectionName =
                normalizeSectionName(rawName);

            if (
                sectionMap.has(sectionName) &&
                !normalized.includes(sectionName)
            ) {
                normalized.push(sectionName);
            }
        });

        defaultCompactOrder.forEach(sectionName => {
            if (
                sectionMap.has(sectionName) &&
                !normalized.includes(sectionName)
            ) {
                normalized.push(sectionName);
            }
        });

        sectionMap.forEach((_, sectionName) => {
            if (!normalized.includes(sectionName)) {
                normalized.push(sectionName);
            }
        });

        return normalized;
    };

    const loadSavedOrder = () => {
        try {
            const stored =
                localStorage.getItem(storageKey);

            if (!stored) {
                return normalizeOrder(defaultCompactOrder);
            }

            return normalizeOrder(
                JSON.parse(stored));
        } catch {
            return normalizeOrder(defaultCompactOrder);
        }
    };

    const saveOrder = order => {
        localStorage.setItem(
            storageKey,
            JSON.stringify(order));
    };

    const getSectionLabel = sectionName => {
        const section =
            sectionMap.get(sectionName);

        return section?.dataset.sectionTitle ??
            sectionName;
    };

    const createDesktopColumn = (
        className,
        sectionNames
    ) => {
        const column =
            document.createElement("div");

        column.className =
            `character-desktop-column ${className}`;

        sectionNames.forEach(sectionName => {
            const section =
                sectionMap.get(sectionName);

            if (section) {
                column.appendChild(section);
            }
        });

        return column;
    };

    let currentOrder =
        loadSavedOrder();

    const renderDesktopLayout = () => {
        const leftColumn =
            createDesktopColumn(
                "character-desktop-column--left",
                desktopLayout.left);

        const centerColumn =
            createDesktopColumn(
                "character-desktop-column--center",
                desktopLayout.center);

        const rightColumn =
            createDesktopColumn(
                "character-desktop-column--right",
                desktopLayout.right);

        layoutContainer.replaceChildren(
            leftColumn,
            centerColumn,
            rightColumn);

        const inventory =
            sectionMap.get("inventory");

        if (inventory) {
            layoutContainer.appendChild(inventory);
        }

        // Keep future sections visible even before we assign
        // them to a specific desktop column.
        sectionMap.forEach((section, sectionName) => {
            const assigned =
                desktopLayout.left.includes(sectionName) ||
                desktopLayout.center.includes(sectionName) ||
                desktopLayout.right.includes(sectionName) ||
                sectionName === "inventory";

            if (!assigned) {
                layoutContainer.appendChild(section);
            }
        });
    };

    const renderCompactLayout = () => {
        const fragment =
            document.createDocumentFragment();

        currentOrder.forEach(sectionName => {
            const section =
                sectionMap.get(sectionName);

            if (section) {
                fragment.appendChild(section);
            }
        });

        layoutContainer.replaceChildren(fragment);
    };

    const renderLayout = () => {
        if (desktopMedia.matches) {
            renderDesktopLayout();
        } else {
            renderCompactLayout();
        }
    };

    renderLayout();


    /* ==========================================================
       LAYOUT EDITOR
       ========================================================== */

    const announceLayoutChange = message => {
        if (!layoutStatus) {
            return;
        }

        layoutStatus.textContent = "";

        window.requestAnimationFrame(() => {
            layoutStatus.textContent = message;
        });
    };

    const createMoveButton = (
        sectionName,
        direction,
        disabled
    ) => {
        const button =
            document.createElement("button");

        const actionLabel =
            direction < 0
                ? "earlier"
                : "later";

        button.type = "button";
        button.className =
            "btn btn-sm btn-outline-secondary character-layout-move";
        button.disabled = disabled;
        button.title = `Move ${actionLabel}`;
        button.textContent =
            direction < 0
                ? "↑"
                : "↓";

        button.setAttribute(
            "aria-label",
            `Move ${getSectionLabel(sectionName)} ${actionLabel}`);

        button.addEventListener("click", () => {
            moveSection(
                sectionName,
                direction);
        });

        return button;
    };

    const renderLayoutEditor = () => {
        if (!layoutList) {
            return;
        }

        layoutList.replaceChildren();

        currentOrder.forEach((sectionName, index) => {
            const row =
                document.createElement("div");

            row.className =
                "list-group-item character-layout-item";

            const position =
                document.createElement("span");

            position.className =
                "character-layout-position";
            position.textContent =
                `${index + 1}`;

            const name =
                document.createElement("span");

            name.className =
                "character-layout-name";
            name.textContent =
                getSectionLabel(sectionName);

            const actions =
                document.createElement("div");

            actions.className =
                "character-layout-actions";

            actions.append(
                createMoveButton(
                    sectionName,
                    -1,
                    index === 0),
                createMoveButton(
                    sectionName,
                    1,
                    index === currentOrder.length - 1)
            );

            row.append(
                position,
                name,
                actions);

            layoutList.appendChild(row);
        });
    };

    const moveSection = (
        sectionName,
        direction
    ) => {
        const index =
            currentOrder.indexOf(sectionName);

        if (index < 0) {
            return;
        }

        const targetIndex =
            index + direction;

        if (
            targetIndex < 0 ||
            targetIndex >= currentOrder.length
        ) {
            return;
        }

        const updated =
            [...currentOrder];

        [
            updated[index],
            updated[targetIndex]
        ] = [
                updated[targetIndex],
                updated[index]
            ];

        currentOrder =
            normalizeOrder(updated);

        saveOrder(currentOrder);

        if (!desktopMedia.matches) {
            renderCompactLayout();
        }

        renderLayoutEditor();

        announceLayoutChange(
            `${getSectionLabel(sectionName)} moved ${direction < 0
                ? "earlier"
                : "later"
            }.`);
    };

    layoutModal?.addEventListener(
        "show.bs.modal",
        renderLayoutEditor);

    resetLayoutButton?.addEventListener(
        "click",
        () => {
            currentOrder =
                normalizeOrder(defaultCompactOrder);

            saveOrder(currentOrder);

            if (!desktopMedia.matches) {
                renderCompactLayout();
            }

            renderLayoutEditor();

            announceLayoutChange(
                "Character sheet layout reset to default.");
        });

    const handleViewportChange = () => {
        renderLayout();
    };

    if (
        typeof desktopMedia.addEventListener ===
        "function"
    ) {
        desktopMedia.addEventListener(
            "change",
            handleViewportChange);
    } else {
        desktopMedia.addListener(
            handleViewportChange);
    }
});