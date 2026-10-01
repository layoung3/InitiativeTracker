document.addEventListener(
    "DOMContentLoaded",
    () => {

        const pageKey =
            window.location.pathname;

        const collapseStorageKey =
            `inventory-collapse:${pageKey}`;

        const scrollStorageKey =
            `inventory-scroll:${pageKey}`;


        /* ======================================================
           HELPERS
           ====================================================== */

        function canEquipItemType(
            itemType) {

            return itemType === "Weapon" ||
                itemType === "Armor" ||
                itemType === "Equipment";
        }


        function configureEquippedControl(
            itemTypeSelect,
            equippedCheckbox) {

            if (!itemTypeSelect ||
                !equippedCheckbox) {
                return;
            }


            const updateAvailability =
                () => {

                    const canEquip =
                        canEquipItemType(
                            itemTypeSelect.value);

                    equippedCheckbox.disabled =
                        !canEquip;

                    if (!canEquip) {
                        equippedCheckbox.checked =
                            false;
                    }
                };


            itemTypeSelect.addEventListener(
                "change",
                updateAvailability);


            updateAvailability();


            return updateAvailability;
        }


        /* ======================================================
           ADD ITEM EQUIPPED STATE
           ====================================================== */

        const addItemType =
            document.getElementById(
                "itemType");

        const addIsEquipped =
            document.getElementById(
                "isEquipped");


        configureEquippedControl(
            addItemType,
            addIsEquipped);


        /* ======================================================
           EDIT ITEM MODAL
           ====================================================== */

        const editModal =
            document.getElementById(
                "editInventoryItemModal");

        const editItemId =
            document.getElementById(
                "editItemId");

        const editItemName =
            document.getElementById(
                "editItemName");

        const editItemType =
            document.getElementById(
                "editItemType");

        const editItemQuantity =
            document.getElementById(
                "editItemQuantity");

        const editItemNotes =
            document.getElementById(
                "editItemNotes");

        const editIsEquipped =
            document.getElementById(
                "editIsEquipped");


        const updateEditEquippedAvailability =
            configureEquippedControl(
                editItemType,
                editIsEquipped);


        if (editModal) {

            editModal.addEventListener(
                "show.bs.modal",
                event => {

                    const button =
                        event.relatedTarget;

                    if (!button) {
                        return;
                    }


                    editItemId.value =
                        button.dataset.itemId ?? "";

                    editItemName.value =
                        button.dataset.itemName ?? "";

                    editItemType.value =
                        button.dataset.itemType ?? "General";

                    editItemQuantity.value =
                        button.dataset.itemQuantity ?? "1";

                    editItemNotes.value =
                        button.dataset.itemNotes ?? "";

                    editIsEquipped.checked =
                        button.dataset.itemEquipped ===
                        "true";


                    if (updateEditEquippedAvailability) {
                        updateEditEquippedAvailability();
                    }
                });
        }


        /* ======================================================
           COLLAPSED INVENTORY SECTIONS
           ====================================================== */

        const inventorySections =
            document.querySelectorAll(
                "[data-inventory-section]");


        let collapsedSections = [];

        try {

            collapsedSections =
                JSON.parse(
                    localStorage.getItem(
                        collapseStorageKey)) ?? [];
        }
        catch {

            collapsedSections = [];
        }


        inventorySections.forEach(
            section => {

                const sectionName =
                    section.dataset.inventorySection;

                const collapse =
                    bootstrap.Collapse
                        .getOrCreateInstance(
                            section,
                            {
                                toggle: false
                            });


                if (collapsedSections.includes(
                    sectionName)) {

                    collapse.hide();
                }


                section.addEventListener(
                    "hidden.bs.collapse",
                    () => {

                        if (!collapsedSections.includes(
                            sectionName)) {

                            collapsedSections.push(
                                sectionName);
                        }

                        saveCollapsedSections();
                    });


                section.addEventListener(
                    "shown.bs.collapse",
                    () => {

                        collapsedSections =
                            collapsedSections.filter(
                                x =>
                                    x !==
                                    sectionName);

                        saveCollapsedSections();
                    });
            });


        function saveCollapsedSections() {

            localStorage.setItem(
                collapseStorageKey,
                JSON.stringify(
                    collapsedSections));
        }


        /* ======================================================
           PRESERVE SCROLL DURING INVENTORY POSTS
           ====================================================== */

        const inventoryForms =
            document.querySelectorAll(
                "form[data-preserve-inventory-scroll='true']");


        inventoryForms.forEach(
            form => {

                form.addEventListener(
                    "submit",
                    () => {

                        sessionStorage.setItem(
                            scrollStorageKey,
                            window.scrollY.toString());
                    });
            });


        /* ======================================================
           RESTORE SCROLL AFTER REDIRECT / RELOAD
           ====================================================== */

        const savedScroll =
            sessionStorage.getItem(
                scrollStorageKey);


        if (savedScroll !== null) {

            sessionStorage.removeItem(
                scrollStorageKey);

            const scrollPosition =
                Number.parseInt(
                    savedScroll,
                    10);


            if (Number.isFinite(
                scrollPosition)) {

                requestAnimationFrame(
                    () => {

                        requestAnimationFrame(
                            () => {

                                window.scrollTo(
                                    {
                                        top:
                                            scrollPosition,

                                        left:
                                            0,

                                        behavior:
                                            "auto"
                                    });
                            });
                    });
            }
        }
    });