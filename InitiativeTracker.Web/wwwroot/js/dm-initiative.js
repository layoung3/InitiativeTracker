"use strict";

const initiativeList =
    document.getElementById("initiativeList");

const reorderForm =
    document.getElementById("reorderForm");

if (initiativeList && reorderForm) {

    Sortable.create(
        initiativeList,
        {
            animation: 150,

            handle: ".drag-handle",

            draggable: ".initiative-dm-row",

            ghostClass: "sortable-ghost",

            chosenClass: "sortable-chosen",

            delay: 150,

            delayOnTouchOnly: true,

            onEnd: async event => {

                if (event.oldIndex === event.newIndex) {
                    return;
                }

                await saveInitiativeOrder();
            }
        });
}

async function saveInitiativeOrder() {

    const rows =
        initiativeList.querySelectorAll(
            ".initiative-dm-row");

    const formData =
        new FormData(reorderForm);

    for (const row of rows) {

        const id =
            row.dataset.combatantId;

        if (id) {
            formData.append(
                "orderedIds",
                id);
        }
    }

    try {

        const response =
            await fetch(
                reorderForm.action,
                {
                    method: "POST",
                    body: formData
                });

        if (!response.ok) {
            throw new Error(
                "The initiative order could not be saved.");
        }

        enableUndoButton();
    }
    catch (error) {

        console.error(
            "Unable to reorder initiative.",
            error);

        // Restore the authoritative server order.
        window.location.reload();
    }
}

function enableUndoButton() {

    const undoButton =
        document.getElementById("undoButton");

    if (!undoButton) {
        return;
    }

    undoButton.disabled = false;
}