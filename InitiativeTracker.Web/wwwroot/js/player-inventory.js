document.addEventListener(
    "DOMContentLoaded",
    () => {

        if (document.documentElement.dataset
            .playerInventoryInitialized === "true") {
            return;
        }

        document.documentElement.dataset
            .playerInventoryInitialized =
            "true";

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
   WEAPON CONFIGURATION
   ====================================================== */

        const weaponModal =
            document.getElementById(
                "weaponConfigurationModal");

        const weaponForm =
            document.getElementById(
                "weaponConfigurationForm");

        const weaponItemId =
            document.getElementById(
                "weaponItemId");

        const weaponItemName =
            document.getElementById(
                "weaponConfigurationItemName");

        const weaponTemplate =
            document.getElementById(
                "weaponTemplate");

        const weaponAttackAbility =
            document.getElementById(
                "weaponAttackAbility");

        const weaponUsesProficiency =
            document.getElementById(
                "weaponUsesProficiency");

        const weaponAttackBonus =
            document.getElementById(
                "weaponAttackBonus");

        const weaponPrimaryDamageBonus =
            document.getElementById(
                "weaponPrimaryDamageBonus");

        const weaponNormalRange =
            document.getElementById(
                "weaponNormalRange");

        const weaponLongRange =
            document.getElementById(
                "weaponLongRange");

        const damageContainer =
            document.getElementById(
                "weaponDamageComponents");

        const addDamageButton =
            document.getElementById(
                "addWeaponDamageComponent");


        let weaponCatalog = [];

        const weaponCatalogData =
            document.getElementById(
                "weaponCatalogData");


        if (weaponCatalogData) {

            try {

                weaponCatalog =
                    JSON.parse(
                        weaponCatalogData.textContent);

            }
            catch {

                weaponCatalog = [];
            }
        }


        function enableAllAttackAbilities() {

            if (!weaponAttackAbility) {
                return;
            }

            Array.from(
                weaponAttackAbility.options)
                .forEach(
                    option => {

                        option.disabled =
                            false;
                    });
        }


        function restrictAttackAbilities(
            allowedAbilities) {

            if (!weaponAttackAbility) {
                return;
            }


            Array.from(
                weaponAttackAbility.options)
                .forEach(
                    option => {

                        option.disabled =
                            !allowedAbilities.includes(
                                option.value);
                    });
        }


        function clearDamageComponents() {

            damageContainer
                ?.replaceChildren();
        }


        function addDamageComponent(
            values = {}) {

            if (!damageContainer) {
                return;
            }


            const row =
                document.createElement(
                    "div");

            row.className =
                "border rounded p-3 weapon-damage-row";


            row.innerHTML = `
        <div class="d-flex
            justify-content-between
            align-items-center
            mb-3">

            <div class="fw-semibold"
                 data-damage-title>
                Damage
            </div>

            <button type="button"
                    class="btn
                        btn-sm
                        btn-outline-danger"
                    data-remove-damage>
                Remove
            </button>

        </div>

        <div class="row g-3">

            <div class="col-6">

                <label class="form-label">
                    Dice
                </label>

                <input type="text"
                       maxlength="50"
                       class="form-control"
                       data-field="DamageDice"
                       placeholder="1d8"
                       required />

            </div>

            <div class="col-6">

                <label class="form-label">
                    Damage Type
                </label>

                <input type="text"
                       maxlength="100"
                       class="form-control"
                       data-field="DamageType"
                       placeholder="Slashing"
                       required />

            </div>

            <div class="col-12
                col-sm-6
                damage-versatile-field">

                <label class="form-label">
                    Versatile Dice
                </label>

                <input type="text"
                       maxlength="50"
                       class="form-control"
                       data-field="VersatileDamageDice"
                       placeholder="Optional" />

            </div>

            <div class="col-12
                col-sm-6
                d-flex
                align-items-end">

                <div class="form-check mb-2">

                    <input type="checkbox"
                           value="true"
                           class="form-check-input"
                           data-field="AddsAbilityModifier" />

                    <label class="form-check-label">
                        Add ability modifier
                    </label>

                </div>

            </div>

        </div>
    `;


            damageContainer.appendChild(
                row);


            row.querySelector(
                "[data-field='DamageDice']")
                .value =
                values.damageDice ?? "";


            row.querySelector(
                "[data-field='DamageType']")
                .value =
                values.damageType ?? "";


            row.querySelector(
                "[data-field='VersatileDamageDice']")
                .value =
                values.versatileDamageDice ?? "";


            row.querySelector(
                "[data-field='AddsAbilityModifier']")
                .checked =
                values.addsAbilityModifier === true;


            row.querySelector(
                "[data-remove-damage]")
                .addEventListener(
                    "click",
                    () => {

                        row.remove();

                        reindexDamageComponents();
                    });


            reindexDamageComponents();
        }


        function reindexDamageComponents() {

            if (!damageContainer) {
                return;
            }


            const rows =
                Array.from(
                    damageContainer.querySelectorAll(
                        ".weapon-damage-row"));


            rows.forEach(
                (row, index) => {

                    const isPrimary =
                        index === 0;


                    row.querySelector(
                        "[data-damage-title]")
                        .textContent =
                        isPrimary
                            ? "Primary Damage"
                            : `Additional Damage ${index}`;


                    const damageDice =
                        row.querySelector(
                            "[data-field='DamageDice']");

                    const damageType =
                        row.querySelector(
                            "[data-field='DamageType']");

                    const versatile =
                        row.querySelector(
                            "[data-field='VersatileDamageDice']");

                    const addsAbility =
                        row.querySelector(
                            "[data-field='AddsAbilityModifier']");

                    const versatileContainer =
                        row.querySelector(
                            ".damage-versatile-field");

                    const removeButton =
                        row.querySelector(
                            "[data-remove-damage]");


                    damageDice.name =
                        `damageComponents[${index}].DamageDice`;

                    damageType.name =
                        `damageComponents[${index}].DamageType`;

                    addsAbility.name =
                        `damageComponents[${index}].AddsAbilityModifier`;


                    if (isPrimary) {

                        versatile.name =
                            `damageComponents[${index}].VersatileDamageDice`;

                        versatileContainer
                            .classList
                            .remove(
                                "d-none");

                        removeButton
                            .classList
                            .add(
                                "d-none");
                    }
                    else {

                        versatile.name =
                            "";

                        versatile.value =
                            "";

                        versatileContainer
                            .classList
                            .add(
                                "d-none");

                        removeButton
                            .classList
                            .remove(
                                "d-none");
                    }
                });
        }


        function applyWeaponTemplate(
            definition) {

            if (!definition) {
                return;
            }


            enableAllAttackAbilities();

            restrictAttackAbilities(
                definition.allowedAttackAbilities);


            weaponAttackAbility.value =
                definition.defaultAttackAbility;

            weaponUsesProficiency.checked =
                true;

            weaponAttackBonus.value =
                "0";

            weaponPrimaryDamageBonus.value =
                "0";

            weaponNormalRange.value =
                definition.normalRange ?? "";

            weaponLongRange.value =
                definition.longRange ?? "";


            clearDamageComponents();


            definition.damageComponents
                .forEach(
                    damage => {

                        addDamageComponent(
                            damage);
                    });
        }


        weaponTemplate
            ?.addEventListener(
                "change",
                () => {

                    const key =
                        weaponTemplate.value;


                    if (!key) {

                        enableAllAttackAbilities();

                        return;
                    }


                    const definition =
                        weaponCatalog.find(
                            x =>
                                x.key === key);


                    applyWeaponTemplate(
                        definition);
                });


        if (addDamageButton &&
            addDamageButton.dataset.bound !== "true") {

            addDamageButton.dataset.bound = "true";

            addDamageButton.addEventListener(
                "click",
                () => {

                    addDamageComponent(
                        {
                            damageDice: "",
                            damageType: "",
                            addsAbilityModifier: false,
                            versatileDamageDice: null
                        });
                });
        }


        weaponModal
            ?.addEventListener(
                "show.bs.modal",
                event => {

                    const button =
                        event.relatedTarget;

                    if (!button) {
                        return;
                    }


                    const itemId =
                        button.dataset.weaponItemId;

                    const itemName =
                        button.dataset.weaponItemName ?? "";

                    const configJson =
                        button.dataset.weaponConfig;


                    weaponItemId.value =
                        itemId ?? "";

                    weaponItemName.textContent =
                        itemName;

                    weaponTemplate.value =
                        "";

                    enableAllAttackAbilities();


                    weaponAttackAbility.value =
                        "Strength";

                    weaponUsesProficiency.checked =
                        true;

                    weaponAttackBonus.value =
                        "0";

                    weaponPrimaryDamageBonus.value =
                        "0";

                    weaponNormalRange.value =
                        "";

                    weaponLongRange.value =
                        "";


                    clearDamageComponents();


                    if (configJson) {

                        try {

                            const config =
                                JSON.parse(
                                    configJson);


                            weaponAttackAbility.value =
                                config.attackAbility ??
                                "Strength";

                            weaponUsesProficiency.checked =
                                config.usesProficiency === true;

                            weaponAttackBonus.value =
                                config.attackBonus ?? 0;

                            weaponPrimaryDamageBonus.value =
                                config.primaryDamageBonus ?? 0;

                            weaponNormalRange.value =
                                config.normalRange ?? "";

                            weaponLongRange.value =
                                config.longRange ?? "";


                            config.damageComponents
                                ?.forEach(
                                    damage => {

                                        addDamageComponent(
                                            damage);
                                    });

                        }
                        catch {

                            addDamageComponent();
                        }
                    }
                    else {

                        const matchingTemplate =
                            weaponCatalog.find(
                                x =>
                                    x.name.toLowerCase() ===
                                    itemName.toLowerCase());


                        if (matchingTemplate) {

                            weaponTemplate.value =
                                matchingTemplate.key;

                            applyWeaponTemplate(
                                matchingTemplate);
                        }
                        else {

                            addDamageComponent(
                                {
                                    addsAbilityModifier:
                                        true
                                });
                        }
                    }


                    if (damageContainer &&
                        damageContainer.children.length === 0) {

                        addDamageComponent(
                            {
                                addsAbilityModifier:
                                    true
                            });
                    }
                });


        weaponForm
            ?.addEventListener(
                "submit",
                () => {

                    reindexDamageComponents();
                });


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