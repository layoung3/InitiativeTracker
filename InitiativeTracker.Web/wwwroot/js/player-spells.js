document.addEventListener(
    "DOMContentLoaded",
    () => {

        const pageKey =
            window.location.pathname;

        const collapseStorageKey =
            `spell-level-collapse:${pageKey}`;

        const preparedFilterStorageKey =
            `spell-prepared-only:${pageKey}`;


        /* ======================================================
           COLLAPSED SPELL LEVELS
           ====================================================== */

        const sections =
            document.querySelectorAll(
                "[data-spell-level]");

        let collapsedLevels = [];

        try {

            collapsedLevels =
                JSON.parse(
                    localStorage.getItem(
                        collapseStorageKey)) ?? [];
        }
        catch {

            collapsedLevels = [];
        }


        sections.forEach(
            section => {

                const level =
                    section.dataset.spellLevel;

                const collapse =
                    bootstrap.Collapse
                        .getOrCreateInstance(
                            section,
                            {
                                toggle: false
                            });

                if (collapsedLevels.includes(
                    level)) {

                    collapse.hide();
                }

                section.addEventListener(
                    "hidden.bs.collapse",
                    () => {

                        if (!collapsedLevels.includes(
                            level)) {

                            collapsedLevels.push(
                                level);
                        }

                        saveCollapsedLevels();
                    });

                section.addEventListener(
                    "shown.bs.collapse",
                    () => {

                        collapsedLevels =
                            collapsedLevels.filter(
                                x =>
                                    x !== level);

                        saveCollapsedLevels();
                    });
            });


        function saveCollapsedLevels() {

            localStorage.setItem(
                collapseStorageKey,
                JSON.stringify(
                    collapsedLevels));
        }


        /* ======================================================
           PREPARED ONLY FILTER
           ====================================================== */

        const preparedOnly =
            document.getElementById(
                "preparedSpellsOnly");


        if (preparedOnly) {

            preparedOnly.checked =
                localStorage.getItem(
                    preparedFilterStorageKey) ===
                "true";


            preparedOnly.addEventListener(
                "change",
                () => {

                    localStorage.setItem(
                        preparedFilterStorageKey,
                        preparedOnly.checked
                            ? "true"
                            : "false");

                    applyPreparedFilter();
                });


            applyPreparedFilter();
        }


        function applyPreparedFilter() {

            if (!preparedOnly) {
                return;
            }


            document
                .querySelectorAll(
                    "[data-spell-level]")
                .forEach(
                    levelContainer => {

                        const rows =
                            Array.from(
                                levelContainer
                                    .querySelectorAll(
                                        "[data-spell-row]"));

                        rows.forEach(
                            row => {

                                const isPrepared =
                                    row.dataset
                                        .spellPrepared ===
                                    "true";

                                row.classList.toggle(
                                    "d-none",
                                    preparedOnly.checked &&
                                    !isPrepared);
                            });


                        const visibleCount =
                            rows.filter(
                                row =>
                                    !row.classList
                                        .contains(
                                            "d-none"))
                                .length;


                        const filterEmpty =
                            levelContainer
                                .querySelector(
                                    "[data-spell-filter-empty]");


                        filterEmpty
                            ?.classList
                            .toggle(
                                "d-none",
                                !preparedOnly.checked ||
                                rows.length === 0 ||
                                visibleCount > 0);
                    });
        }


        /* ======================================================
           ADD / EDIT SPELL
           ====================================================== */

        const modal =
            document.getElementById(
                "spellModal");

        const form =
            document.getElementById(
                "spellForm");


        if (!modal ||
            !form) {
            return;
        }


        const title =
            document.getElementById(
                "spellModalLabel");

        const spellId =
            document.getElementById(
                "spellId");

        const name =
            document.getElementById(
                "spellName");

        const level =
            document.getElementById(
                "spellLevel");

        const school =
            document.getElementById(
                "spellSchool");

        const castingTime =
            document.getElementById(
                "spellCastingTime");

        const range =
            document.getElementById(
                "spellRange");

        const area =
            document.getElementById(
                "spellArea");

        const duration =
            document.getElementById(
                "spellDuration");

        const prepared =
            document.getElementById(
                "spellPrepared");

        const verbal =
            document.getElementById(
                "spellVerbal");

        const somatic =
            document.getElementById(
                "spellSomatic");

        const material =
            document.getElementById(
                "spellMaterial");

        const materialDescription =
            document.getElementById(
                "spellMaterialDescription");

        const materialContainer =
            document.getElementById(
                "spellMaterialDescriptionContainer");

        const concentration =
            document.getElementById(
                "spellConcentration");

        const ritual =
            document.getElementById(
                "spellRitual");

        const spellAttack =
            document.getElementById(
                "spellAttackRoll");

        const savingThrow =
            document.getElementById(
                "spellSavingThrow");

        const notes =
            document.getElementById(
                "spellNotes");

        const effectsContainer =
            document.getElementById(
                "spellEffects");

        const addEffectButton =
            document.getElementById(
                "addSpellEffect");

        const deleteButton =
            document.getElementById(
                "deleteSpell");


        function updateMaterialVisibility() {

            materialContainer
                ?.classList
                .toggle(
                    "d-none",
                    !material.checked);
        }


        material.addEventListener(
            "change",
            updateMaterialVisibility);


        function clearEffects() {

            effectsContainer
                .replaceChildren();
        }


        function addEffect(
            values = {}) {

            const row =
                document.createElement(
                    "div");

            row.className =
                "border rounded p-3 spell-effect-row";


            row.innerHTML = `
                <div class="d-flex
                    justify-content-between
                    align-items-center
                    mb-3">

                    <div class="fw-semibold"
                         data-effect-title>
                        Effect
                    </div>

                    <button type="button"
                            class="btn
                                btn-sm
                                btn-outline-danger"
                            data-remove-effect>
                        Remove
                    </button>

                </div>

                <div class="row g-3">

                    <div class="col-6 col-sm-3">

                        <label class="form-label">
                            Type
                        </label>

                        <select class="form-select"
                                data-field="EffectType">

                            <option value="Damage">
                                Damage
                            </option>

                            <option value="Healing">
                                Healing
                            </option>

                        </select>

                    </div>


                    <div class="col-6 col-sm-3">

                        <label class="form-label">
                            Expression
                        </label>

                        <input type="text"
                               maxlength="100"
                               class="form-control"
                               data-field="Expression"
                               placeholder="8d6"
                               required />

                    </div>


                    <div class="col-6 col-sm-3
                        spell-damage-type-field">

                        <label class="form-label">
                            Damage Type
                        </label>

                        <input type="text"
                               maxlength="100"
                               class="form-control"
                               data-field="DamageType"
                               placeholder="Fire" />

                    </div>


                    <div class="col-6 col-sm-3">

                        <label class="form-label">
                            Additional Bonus
                        </label>

                        <input type="number"
                               value="0"
                               class="form-control"
                               data-field="AdditionalBonus" />

                    </div>


                    <div class="col-12 col-sm-6
                        spell-upcast-field">

                        <label class="form-label">
                            Upcast / Slot Level
                        </label>

                        <input type="text"
                               maxlength="100"
                               class="form-control"
                               data-field="UpcastExpressionPerLevel"
                               placeholder="1d6" />

                    </div>


                    <div class="col-12 col-sm-6
                        spell-cantrip-scaling-field
                        d-none">

                        <label class="form-label">
                            Cantrip Scaling
                        </label>

                        <input type="text"
                               maxlength="100"
                               class="form-control"
                               data-field="CantripScalingExpression"
                               placeholder="1d10" />

                        <div class="form-text">
                            Added at character levels 5, 11, and 17.
                        </div>

                    </div>


                    <div class="col-12 col-sm-6
                        d-flex
                        align-items-end">

                        <div class="form-check mb-2">

                            <input type="checkbox"
                                   value="true"
                                   class="form-check-input"
                                   data-field="AddsSpellcastingAbilityModifier" />

                            <label class="form-check-label">
                                Add spellcasting ability modifier
                            </label>

                        </div>

                    </div>

                </div>
            `;


            effectsContainer.appendChild(
                row);


            const effectType =
                row.querySelector(
                    "[data-field='EffectType']");

            const expression =
                row.querySelector(
                    "[data-field='Expression']");

            const damageType =
                row.querySelector(
                    "[data-field='DamageType']");

            const additionalBonus =
                row.querySelector(
                    "[data-field='AdditionalBonus']");

            const upcast =
                row.querySelector(
                    "[data-field='UpcastExpressionPerLevel']");

            const cantripScaling =
                row.querySelector(
                    "[data-field='CantripScalingExpression']");

            const addsAbility =
                row.querySelector(
                    "[data-field='AddsSpellcastingAbilityModifier']");


            effectType.value =
                values.effectType ??
                "Damage";

            expression.value =
                values.expression ??
                "";

            damageType.value =
                values.damageType ??
                "";

            additionalBonus.value =
                values.additionalBonus ??
                0;

            upcast.value =
                values.upcastExpressionPerLevel ??
                "";

            cantripScaling.value =
                values.cantripScalingExpression ??
                "";

            addsAbility.checked =
                values.addsSpellcastingAbilityModifier ===
                true;


            function updateDamageTypeVisibility() {

                const isDamage =
                    effectType.value ===
                    "Damage";

                row.querySelector(
                    ".spell-damage-type-field")
                    .classList
                    .toggle(
                        "d-none",
                        !isDamage);

                damageType.disabled =
                    !isDamage;
            }


            effectType.addEventListener(
                "change",
                updateDamageTypeVisibility);


            row.querySelector(
                "[data-remove-effect]")
                .addEventListener(
                    "click",
                    () => {

                        row.remove();

                        reindexEffects();
                    });


            updateDamageTypeVisibility();
            updateEffectScalingVisibility();

            reindexEffects();
        }


        function reindexEffects() {

            const rows =
                Array.from(
                    effectsContainer
                        .querySelectorAll(
                            ".spell-effect-row"));


            rows.forEach(
                (row, index) => {

                    row.querySelector(
                        "[data-effect-title]")
                        .textContent =
                        `Effect ${index + 1}`;


                    [
                        "EffectType",
                        "Expression",
                        "DamageType",
                        "AdditionalBonus",
                        "UpcastExpressionPerLevel",
                        "CantripScalingExpression",
                        "AddsSpellcastingAbilityModifier"
                    ]
                        .forEach(
                            fieldName => {

                                const field =
                                    row.querySelector(
                                        `[data-field='${fieldName}']`);

                                if (field) {

                                    field.name =
                                        `effects[${index}].${fieldName}`;
                                }
                            });
                });
        }


        function updateEffectScalingVisibility() {

            const isCantrip =
                level.value ===
                "0";

            effectsContainer
                .querySelectorAll(
                    ".spell-effect-row")
                .forEach(
                    row => {

                        row.querySelector(
                            ".spell-upcast-field")
                            ?.classList
                            .toggle(
                                "d-none",
                                isCantrip);

                        row.querySelector(
                            ".spell-cantrip-scaling-field")
                            ?.classList
                            .toggle(
                                "d-none",
                                !isCantrip);
                    });
        }


        level.addEventListener(
            "change",
            updateEffectScalingVisibility);


        function resetForm() {

            spellId.value =
                "";

            name.value =
                "";

            level.value =
                "0";

            school.value =
                "";

            castingTime.value =
                "";

            range.value =
                "";

            area.value =
                "";

            duration.value =
                "";

            prepared.checked =
                true;

            verbal.checked =
                false;

            somatic.checked =
                false;

            material.checked =
                false;

            materialDescription.value =
                "";

            concentration.checked =
                false;

            ritual.checked =
                false;

            spellAttack.checked =
                false;

            savingThrow.value =
                "";

            notes.value =
                "";

            clearEffects();

            updateMaterialVisibility();

            title.textContent =
                "Add Spell";

            deleteButton.classList.add(
                "d-none");
        }


        modal.addEventListener(
            "show.bs.modal",
            event => {

                resetForm();


                const button =
                    event.relatedTarget;

                const configJson =
                    button?.dataset
                        .spellConfig ??
                    modal.dataset
                        .pendingSpellConfig;


                delete modal.dataset
                    .pendingSpellConfig;


                if (!configJson) {
                    return;
                }


                try {

                    const config =
                        JSON.parse(
                            configJson);


                    spellId.value =
                        config.id ?? "";

                    name.value =
                        config.name ?? "";

                    level.value =
                        config.level ?? 0;

                    school.value =
                        config.school ?? "";

                    castingTime.value =
                        config.castingTime ?? "";

                    range.value =
                        config.range ?? "";

                    area.value =
                        config.area ?? "";

                    duration.value =
                        config.duration ?? "";

                    prepared.checked =
                        config.isPrepared === true;

                    verbal.checked =
                        config.hasVerbalComponent ===
                        true;

                    somatic.checked =
                        config.hasSomaticComponent ===
                        true;

                    material.checked =
                        config.hasMaterialComponent ===
                        true;

                    materialDescription.value =
                        config.materialDescription ??
                        "";

                    concentration.checked =
                        config.requiresConcentration ===
                        true;

                    ritual.checked =
                        config.isRitual ===
                        true;

                    spellAttack.checked =
                        config.hasSpellAttackRoll ===
                        true;

                    savingThrow.value =
                        config.savingThrowAbility ??
                        "";

                    notes.value =
                        config.notes ?? "";


                    config.effects
                        ?.forEach(
                            effect => {

                                addEffect(
                                    effect);
                            });


                    updateMaterialVisibility();
                    updateEffectScalingVisibility();

                    title.textContent =
                        "Edit Spell";

                    deleteButton.classList.remove(
                        "d-none");
                }
                catch {

                    clearEffects();
                }
            });


        addEffectButton.addEventListener(
            "click",
            () => {

                addEffect();
            });


        form.addEventListener(
            "submit",
            () => {

                reindexEffects();
            });


        /* ======================================================
           SPELL DETAILS
           ====================================================== */

        const detailsModal =
            document.getElementById(
                "spellDetailsModal");

        const castModal =
            document.getElementById(
                "spellCastModal");

        let currentDetailsConfig =
            null;


        function parseSpellConfig(rawConfig) {

            if (!rawConfig) {
                return null;
            }

            try {

                return JSON.parse(
                    rawConfig);
            }
            catch {

                return null;
            }
        }


        function formatEffectExpression(
            effect,
            spellConfig,
            selectedSlotLevel = null) {

            let expression =
                effect.expression ?? "";

            const flatBonus =
                Number(
                    effect.flatBonus ?? 0);

            if (flatBonus > 0) {

                expression +=
                    ` + ${flatBonus}`;
            }
            else if (flatBonus < 0) {

                expression +=
                    ` - ${Math.abs(
                        flatBonus)}`;
            }


            if (spellConfig.level === 0 &&
                effect.cantripScalingExpression) {

                const steps =
                    Number(
                        spellConfig.cantripScalingSteps ?? 0);

                if (steps === 1) {

                    expression +=
                        ` + ${effect.cantripScalingExpression}`;
                }
                else if (steps > 1) {

                    expression +=
                        ` + ${steps} × ${effect.cantripScalingExpression}`;
                }
            }


            if (spellConfig.level > 0 &&
                selectedSlotLevel &&
                selectedSlotLevel >
                spellConfig.level &&
                effect.upcastExpressionPerLevel) {

                const upcastLevels =
                    selectedSlotLevel -
                    spellConfig.level;

                if (upcastLevels === 1) {

                    expression +=
                        ` + ${effect.upcastExpressionPerLevel}`;
                }
                else {

                    expression +=
                        ` + ${upcastLevels} × ${effect.upcastExpressionPerLevel}`;
                }
            }


            return `${expression} ${effect.effectLabel ?? ""}`
                .trim();
        }


        function showModalAfterClosing(
            closingElement,
            targetElement,
            config) {

            if (!closingElement ||
                !targetElement) {
                return;
            }


            targetElement.dataset
                .pendingSpellConfig =
                JSON.stringify(
                    config);


            const closingModal =
                bootstrap.Modal
                    .getOrCreateInstance(
                        closingElement);


            closingElement.addEventListener(
                "hidden.bs.modal",
                () => {

                    bootstrap.Modal
                        .getOrCreateInstance(
                            targetElement)
                        .show();
                },
                {
                    once: true
                });


            closingModal.hide();
        }


        if (detailsModal) {

            const detailsTitle =
                document.getElementById(
                    "spellDetailsModalLabel");

            const detailsSubtitle =
                document.getElementById(
                    "spellDetailsSubtitle");

            const detailsBadges =
                document.getElementById(
                    "spellDetailsBadges");

            const detailsEffects =
                document.getElementById(
                    "spellDetailsEffects");

            const detailsMaterialSection =
                document.getElementById(
                    "spellDetailsMaterialSection");

            const detailsMaterial =
                document.getElementById(
                    "spellDetailsMaterial");

            const detailsNotesSection =
                document.getElementById(
                    "spellDetailsNotesSection");

            const detailsNotes =
                document.getElementById(
                    "spellDetailsNotes");

            const detailsEdit =
                document.getElementById(
                    "spellDetailsEdit");

            const detailsCast =
                document.getElementById(
                    "spellDetailsCast");


            detailsModal.addEventListener(
                "show.bs.modal",
                event => {

                    const rawConfig =
                        event.relatedTarget
                            ?.dataset
                            ?.spellConfig ??
                        detailsModal.dataset
                            .pendingSpellConfig;


                    delete detailsModal.dataset
                        .pendingSpellConfig;


                    const config =
                        parseSpellConfig(
                            rawConfig);

                    if (!config) {
                        return;
                    }


                    currentDetailsConfig =
                        config;


                    detailsTitle.textContent =
                        config.name ??
                        "Spell Details";


                    detailsSubtitle.textContent =
                        [
                            config.levelLabel,
                            config.school
                        ]
                            .filter(
                                Boolean)
                            .join(
                                " · ");


                    detailsBadges
                        .replaceChildren();


                    const badgeValues =
                        [];


                    if (config.isPrepared) {

                        badgeValues.push(
                            "Prepared");
                    }

                    if (config.requiresConcentration) {

                        badgeValues.push(
                            "Concentration");
                    }

                    if (config.isRitual) {

                        badgeValues.push(
                            "Ritual");
                    }


                    badgeValues.forEach(
                        value => {

                            const badge =
                                document.createElement(
                                    "span");

                            badge.className =
                                "badge text-bg-secondary";

                            badge.textContent =
                                value;

                            detailsBadges
                                .appendChild(
                                    badge);
                        });


                    document
                        .getElementById(
                            "spellDetailsCastingTime")
                        .textContent =
                        config.castingTime ||
                        "—";


                    document
                        .getElementById(
                            "spellDetailsRangeArea")
                        .textContent =
                        config.rangeArea ||
                        "—";


                    document
                        .getElementById(
                            "spellDetailsDuration")
                        .textContent =
                        config.duration ||
                        "—";


                    document
                        .getElementById(
                            "spellDetailsResolution")
                        .textContent =
                        config.resolution ||
                        "—";


                    document
                        .getElementById(
                            "spellDetailsComponents")
                        .textContent =
                        config.components ||
                        "—";


                    detailsEffects
                        .replaceChildren();


                    if (!config.effects?.length) {

                        const empty =
                            document.createElement(
                                "div");

                        empty.className =
                            "text-muted";

                        empty.textContent =
                            "No damage or healing effect configured.";

                        detailsEffects
                            .appendChild(
                                empty);
                    }
                    else {

                        config.effects.forEach(
                            effect => {

                                const line =
                                    document.createElement(
                                        "div");

                                line.className =
                                    "spell-details-effect";

                                line.textContent =
                                    formatEffectExpression(
                                        effect,
                                        config);

                                detailsEffects
                                    .appendChild(
                                        line);
                            });
                    }


                    const hasMaterial =
                        Boolean(
                            config.hasMaterialComponent &&
                            config.materialDescription);


                    detailsMaterialSection
                        .classList
                        .toggle(
                            "d-none",
                            !hasMaterial);


                    detailsMaterial.textContent =
                        config.materialDescription ??
                        "";


                    const hasNotes =
                        Boolean(
                            config.notes?.trim());


                    detailsNotesSection
                        .classList
                        .toggle(
                            "d-none",
                            !hasNotes);


                    detailsNotes.textContent =
                        config.notes ??
                        "";
                });


            detailsEdit
                ?.addEventListener(
                    "click",
                    () => {

                        if (currentDetailsConfig) {

                            showModalAfterClosing(
                                detailsModal,
                                modal,
                                currentDetailsConfig);
                        }
                    });


            detailsCast
                ?.addEventListener(
                    "click",
                    () => {

                        if (currentDetailsConfig &&
                            castModal) {

                            showModalAfterClosing(
                                detailsModal,
                                castModal,
                                currentDetailsConfig);
                        }
                    });
        }


        /* ======================================================
           CAST SPELL
           ====================================================== */

        if (castModal) {

            const castForm =
                document.getElementById(
                    "spellCastForm");

            const castSpellId =
                document.getElementById(
                    "castSpellId");

            const castTitle =
                document.getElementById(
                    "spellCastModalLabel");

            const castSubtitle =
                document.getElementById(
                    "spellCastSubtitle");

            const castWarning =
                document.getElementById(
                    "spellCastConcentrationWarning");

            const castSlotSection =
                document.getElementById(
                    "spellCastSlotSection");

            const castSlots =
                document.getElementById(
                    "spellCastSlots");

            const ritualContainer =
                document.getElementById(
                    "spellCastRitualContainer");

            const ritualCheckbox =
                document.getElementById(
                    "spellCastAsRitual");

            const castEffects =
                document.getElementById(
                    "spellCastEffects");

            const confirmCast =
                document.getElementById(
                    "confirmSpellCast");


            let currentCastConfig =
                null;


            function getSelectedSlotLevel() {

                const selected =
                    castSlots.querySelector(
                        "input[name='slotLevel']:checked");

                return selected
                    ? Number(
                        selected.value)
                    : null;
            }


            function ordinal(
                levelValue) {

                if (levelValue === 1) {
                    return "1st";
                }

                if (levelValue === 2) {
                    return "2nd";
                }

                if (levelValue === 3) {
                    return "3rd";
                }

                return `${levelValue}th`;
            }


            function updateCastPreview() {

                if (!currentCastConfig) {
                    return;
                }


                const ritualCast =
                    ritualCheckbox.checked;


                const slotLevel =
                    ritualCast
                        ? null
                        : getSelectedSlotLevel();


                castEffects
                    .replaceChildren();


                if (!currentCastConfig.effects?.length) {

                    const empty =
                        document.createElement(
                            "div");

                    empty.className =
                        "text-muted";

                    empty.textContent =
                        "No damage or healing effect configured.";

                    castEffects
                        .appendChild(
                            empty);
                }
                else {

                    currentCastConfig
                        .effects
                        .forEach(
                            effect => {

                                const line =
                                    document.createElement(
                                        "div");

                                line.className =
                                    "spell-cast-effect";

                                line.textContent =
                                    formatEffectExpression(
                                        effect,
                                        currentCastConfig,
                                        slotLevel);

                                castEffects
                                    .appendChild(
                                        line);
                            });
                }


                castSlots
                    .querySelectorAll(
                        "input[name='slotLevel']")
                    .forEach(
                        input => {

                            input.disabled =
                                ritualCast ||
                                input.dataset.empty ===
                                "true";
                        });


                const canCastWithoutSlot =
                    currentCastConfig.level === 0 ||
                    ritualCast;


                const hasUsableSlot =
                    Boolean(
                        getSelectedSlotLevel());


                confirmCast.disabled =
                    !canCastWithoutSlot &&
                    !hasUsableSlot;
            }


            castModal.addEventListener(
                "show.bs.modal",
                event => {

                    const rawConfig =
                        event.relatedTarget
                            ?.dataset
                            ?.spellConfig ??
                        castModal.dataset
                            .pendingSpellConfig;


                    delete castModal.dataset
                        .pendingSpellConfig;


                    const config =
                        parseSpellConfig(
                            rawConfig);


                    if (!config) {
                        return;
                    }


                    currentCastConfig =
                        config;


                    castSpellId.value =
                        config.id ??
                        "";


                    castTitle.textContent =
                        `Cast ${config.name}`;


                    castSubtitle.textContent =
                        [
                            config.levelLabel,
                            config.castingTime,
                            config.rangeArea
                        ]
                            .filter(
                                value =>
                                    value &&
                                    value !== "—")
                            .join(
                                " · ");


                    const replacesConcentration =
                        Boolean(
                            config.requiresConcentration &&
                            config.activeConcentrationSpellId &&
                            config.activeConcentrationSpellId !==
                            config.id);


                    castWarning
                        .classList
                        .toggle(
                            "d-none",
                            !replacesConcentration);


                    castWarning.textContent =
                        replacesConcentration
                            ? `Casting this spell will end concentration on ${config.activeConcentrationSpellName}.`
                            : "";


                    castSlots
                        .replaceChildren();


                    const isCantrip =
                        config.level === 0;


                    castSlotSection
                        .classList
                        .toggle(
                            "d-none",
                            isCantrip);


                    if (!isCantrip) {

                        config.castSlots
                            ?.forEach(
                                slot => {

                                    const wrapper =
                                        document.createElement(
                                            "label");

                                    wrapper.className =
                                        "spell-cast-slot-option";


                                    const radio =
                                        document.createElement(
                                            "input");

                                    radio.type =
                                        "radio";

                                    radio.name =
                                        "slotLevel";

                                    radio.value =
                                        slot.level;

                                    radio.className =
                                        "form-check-input";

                                    radio.dataset.empty =
                                        slot.remainingSlots <= 0
                                            ? "true"
                                            : "false";

                                    radio.disabled =
                                        slot.remainingSlots <= 0;


                                    if (slot.remainingSlots > 0 &&
                                        !castSlots.querySelector(
                                            "input[name='slotLevel']:checked")) {

                                        radio.checked =
                                            true;
                                    }


                                    const text =
                                        document.createElement(
                                            "span");

                                    text.className =
                                        "spell-cast-slot-text";

                                    text.innerHTML =
                                        `<strong>${ordinal(
                                            slot.level)} Level</strong><span>${slot.remainingSlots}/${slot.maximumSlots} slots</span>`;


                                    wrapper.append(
                                        radio,
                                        text);

                                    castSlots.appendChild(
                                        wrapper);
                                });


                        if (!config.castSlots?.length) {

                            const empty =
                                document.createElement(
                                    "div");

                            empty.className =
                                "text-muted small";

                            empty.textContent =
                                "No eligible spell slots are configured.";

                            castSlots.appendChild(
                                empty);
                        }
                    }


                    ritualContainer
                        .classList
                        .toggle(
                            "d-none",
                            !config.isRitual ||
                            isCantrip);


                    ritualCheckbox.checked =
                        false;


                    updateCastPreview();
                });


            castSlots.addEventListener(
                "change",
                updateCastPreview);


            ritualCheckbox.addEventListener(
                "change",
                updateCastPreview);


            castForm
                ?.addEventListener(
                    "submit",
                    () => {

                        if (currentCastConfig?.level === 0 ||
                            ritualCheckbox.checked) {

                            castSlots
                                .querySelectorAll(
                                    "input[name='slotLevel']")
                                .forEach(
                                    input => {

                                        input.checked =
                                            false;
                                    });
                        }
                    });
        }
    });