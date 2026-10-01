document.addEventListener("DOMContentLoaded", () => {

    const combatEditors = document.getElementById("combatEditors");

    const conditionsModal = document.getElementById("conditionsModal");

    if (conditionsModal) {

        const modal =
            bootstrap.Modal
                .getOrCreateInstance(
                    conditionsModal);


        function updateConditionsModalQuery(
            isOpen) {

            const url =
                new URL(
                    window.location.href);

            if (isOpen) {

                url.searchParams.set(
                    "modal",
                    "conditions");
            }
            else if (
                url.searchParams.get(
                    "modal") ===
                "conditions"
            ) {

                url.searchParams.delete("modal");
            }

            window.history.replaceState(
                {},
                "",
                url);
        }


        conditionsModal.addEventListener(
            "shown.bs.modal",
            () => {

                updateConditionsModalQuery(true);
            });


        conditionsModal.addEventListener(
            "hidden.bs.modal",
            () => {

                updateConditionsModalQuery(false);
            });


        if (
            conditionsModal.dataset.autoOpen === "true"
        ) {

            modal.show();
        }
    }

    if (!combatEditors) {
        return;
    }


    function getEditorName(element) {

        switch (element.id) {

            case "hpEditor":
                return "hp";

            case "temporaryHpEditor":
                return "temp";

            default:
                return null;
        }
    }


    function updateEditorQuery(editor) {

        const url =
            new URL(window.location.href);

        if (editor) {
            url.searchParams.set(
                "editor",
                editor);
        }
        else {
            url.searchParams.delete(
                "editor");
        }

        window.history.replaceState(
            {},
            "",
            url);
    }


    combatEditors.addEventListener(
        "shown.bs.collapse",
        event => {

            const editor =
                getEditorName(
                    event.target);

            if (!editor) {
                return;
            }

            updateEditorQuery(
                editor);
        });


    combatEditors.addEventListener(
        "hidden.bs.collapse",
        () => {

            /*
             * Wait until Bootstrap finishes any accordion
             * transition before deciding whether another
             * editor is now open.
             */
            window.setTimeout(
                () => {

                    const openEditor =
                        combatEditors.querySelector(
                            ".collapse.show");

                    if (!openEditor) {
                        updateEditorQuery(
                            null);

                        return;
                    }

                    updateEditorQuery(
                        getEditorName(
                            openEditor));

                },
                0);
        });

});