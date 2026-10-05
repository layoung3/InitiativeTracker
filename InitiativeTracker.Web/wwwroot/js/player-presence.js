"use strict";

(() => {

    const heartbeatIntervalMs =
        30_000;

    let heartbeatPending =
        false;


    async function sendPlayerHeartbeat() {

        if (heartbeatPending ||
            document.visibilityState ===
            "hidden") {
            return;
        }


        heartbeatPending =
            true;

        try {

            const response =
                await fetch(
                    "/player/presence",
                    {
                        method:
                            "POST",

                        credentials:
                            "same-origin",

                        cache:
                            "no-store"
                    });


            if (response.status === 401 ||
                response.status === 403) {

                window.location.href =
                    "/join";
            }
        }
        catch (error) {

            /*
             * A temporary network interruption should not eject
             * the player. The next heartbeat will retry.
             */
            console.debug(
                "Player presence heartbeat failed.",
                error);
        }
        finally {

            heartbeatPending =
                false;
        }
    }


    window.setInterval(
        sendPlayerHeartbeat,
        heartbeatIntervalMs);


    document.addEventListener(
        "visibilitychange",
        () => {

            if (document.visibilityState ===
                "visible") {

                sendPlayerHeartbeat();
            }
        });


    sendPlayerHeartbeat();

})();