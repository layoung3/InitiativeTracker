document.addEventListener("DOMContentLoaded", () => {

    const combatEditors =
        document.getElementById("combatEditors");

    const conditionsModal =
        document.getElementById(
            "conditionsModal");

    if (
        conditionsModal &&
        conditionsModal.dataset.autoOpen === "true"
    ) {
        bootstrap.Modal
            .getOrCreateInstance(
                conditionsModal)
            .show();
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