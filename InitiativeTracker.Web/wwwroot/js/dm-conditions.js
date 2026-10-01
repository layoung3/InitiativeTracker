document.addEventListener(
    "DOMContentLoaded",
    () => {

        const modalElement =
            document.getElementById(
                "dmConditionsModal");

        if (!modalElement) {
            return;
        }


        const modal =
            bootstrap.Modal
                .getOrCreateInstance(
                    modalElement);


        modalElement.addEventListener(
            "hidden.bs.modal",
            () => {

                const url =
                    new URL(
                        window.location.href);

                url.searchParams.delete(
                    "conditions");

                window.history.replaceState(
                    {},
                    "",
                    url);
            });


        modal.show();
    });