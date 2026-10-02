document.addEventListener(
    "DOMContentLoaded",
    () => {

        const modal =
            document.getElementById(
                "characterAttackModal");

        const form =
            document.getElementById(
                "characterAttackForm");

        if (!modal ||
            !form) {
            return;
        }


        const title =
            document.getElementById(
                "characterAttackModalLabel");

        const attackId =
            document.getElementById(
                "characterAttackId");

        const attackName =
            document.getElementById(
                "characterAttackName");

        const ability =
            document.getElementById(
                "characterAttackAbility");

        const addsAbilityToHit =
            document.getElementById(
                "characterAttackAddsAbility");

        const usesProficiency =
            document.getElementById(
                "characterAttackUsesProficiency");

        const attackBonus =
            document.getElementById(
                "characterAttackBonus");

        const normalRange =
            document.getElementById(
                "characterAttackNormalRange");

        const longRange =
            document.getElementById(
                "characterAttackLongRange");

        const notes =
            document.getElementById(
                "characterAttackNotes");

        const damageContainer =
            document.getElementById(
                "characterAttackDamageComponents");

        const addDamageButton =
            document.getElementById(
                "addCharacterAttackDamage");

        const deleteButton =
            document.getElementById(
                "deleteCharacterAttack");


        function clearDamage() {

            damageContainer
                .replaceChildren();
        }


        function addDamage(
            values = {}) {

            const row =
                document.createElement(
                    "div");

            row.className =
                "border rounded p-3 character-attack-damage-row";


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
                            Damage
                        </label>

                        <input type="text"
                               maxlength="50"
                               class="form-control"
                               data-field="DamageExpression"
                               placeholder="1d8 or 1"
                               required />

                    </div>

                    <div class="col-6">

                        <label class="form-label">
                            Type
                        </label>

                        <input type="text"
                               maxlength="100"
                               class="form-control"
                               data-field="DamageType"
                               placeholder="Slashing"
                               required />

                    </div>

                    <div class="col-6">

                        <label class="form-label">
                            Additional Bonus
                        </label>

                        <input type="number"
                               value="0"
                               class="form-control"
                               data-field="AdditionalDamageBonus" />

                    </div>

                    <div class="col-6
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
                "[data-field='DamageExpression']")
                .value =
                values.damageExpression ?? "";


            row.querySelector(
                "[data-field='DamageType']")
                .value =
                values.damageType ?? "";


            row.querySelector(
                "[data-field='AdditionalDamageBonus']")
                .value =
                values.additionalDamageBonus ?? 0;


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

                        reindexDamage();
                    });


            reindexDamage();
        }


        function reindexDamage() {

            const rows =
                Array.from(
                    damageContainer.querySelectorAll(
                        ".character-attack-damage-row"));


            rows.forEach(
                (row, index) => {

                    row.querySelector(
                        "[data-damage-title]")
                        .textContent =
                        `Damage ${index + 1}`;


                    row.querySelector(
                        "[data-field='DamageExpression']")
                        .name =
                        `damageComponents[${index}].DamageExpression`;


                    row.querySelector(
                        "[data-field='DamageType']")
                        .name =
                        `damageComponents[${index}].DamageType`;


                    row.querySelector(
                        "[data-field='AdditionalDamageBonus']")
                        .name =
                        `damageComponents[${index}].AdditionalDamageBonus`;


                    row.querySelector(
                        "[data-field='AddsAbilityModifier']")
                        .name =
                        `damageComponents[${index}].AddsAbilityModifier`;
                });
        }


        function resetForm() {

            attackId.value =
                "";

            attackName.value =
                "";

            ability.value =
                "Strength";

            addsAbilityToHit.checked =
                true;

            usesProficiency.checked =
                true;

            attackBonus.value =
                "0";

            normalRange.value =
                "";

            longRange.value =
                "";

            notes.value =
                "";

            clearDamage();

            title.textContent =
                "Add Attack";

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
                        .attackConfig;


                if (!configJson) {

                    addDamage(
                        {
                            addsAbilityModifier:
                                true
                        });

                    return;
                }


                try {

                    const config =
                        JSON.parse(
                            configJson);


                    attackId.value =
                        config.id ?? "";

                    attackName.value =
                        config.name ?? "";

                    ability.value =
                        config.attackAbility ??
                        "Strength";

                    addsAbilityToHit.checked =
                        config.addsAbilityModifierToHit ===
                        true;

                    usesProficiency.checked =
                        config.usesProficiency ===
                        true;

                    attackBonus.value =
                        config.attackBonus ?? 0;

                    normalRange.value =
                        config.normalRange ?? "";

                    longRange.value =
                        config.longRange ?? "";

                    notes.value =
                        config.notes ?? "";


                    config.damageComponents
                        ?.forEach(
                            damage => {

                                addDamage(
                                    damage);
                            });


                    title.textContent =
                        "Edit Attack";

                    deleteButton.classList.remove(
                        "d-none");
                }
                catch {

                    addDamage(
                        {
                            addsAbilityModifier:
                                true
                        });
                }
            });


        addDamageButton.addEventListener(
            "click",
            () => {

                addDamage();
            });


        form.addEventListener(
            "submit",
            () => {

                reindexDamage();
            });
    });