"use strict";
(() => {
    const current = new WeakMap();
    const ownUrl = value => {
        try {
            const url = new URL(value, location.href);
            return url.origin === location.origin && (url.pathname.startsWith("/media/") || url.pathname === "/branding/logo") ? url : null;
        } catch {return null;}
    };
    function attach(image) {
        if (current.has(image)) return;
        const url = ownUrl(image.getAttribute("src"));
        if (!url) return;
        current.set(image, url.pathname);
        const fallback = () => {
            const original = current.get(image);
            if (!ownUrl(image.getAttribute("src"))) return;
            image.src = original.endsWith("/banner") ? "/images/default-banner.svg"
                : original === "/branding/logo" ? "/images/colophon.png" : "/images/default-character.svg";
        };
        image.addEventListener("error", fallback);
        if (image.complete && image.naturalWidth === 0) fallback();
    }
    function scan() {document.querySelectorAll("img[data-tracker-media]").forEach(attach);}
    async function refresh() {
        scan();
        document.querySelectorAll("img[data-tracker-media]").forEach(image => {
            const path = current.get(image);
            if (path) image.src = path + "?v=" + Date.now();
        });
        try {
            const response = await fetch("/branding/info", {cache:"no-store"});
            if (!response.ok) return;
            const info = await response.json();
            if (typeof info.name === "string" && info.name.length <= 80) {
                document.querySelectorAll(".tracker-brand-name").forEach(item => {item.textContent = info.name;});
                document.querySelectorAll("#displayBrandTemplate").forEach(template => {
                    template.content.querySelectorAll(".tracker-brand-name").forEach(item => {item.textContent = info.name;});
                });
                document.querySelectorAll(".tracker-brand-logo").forEach(item => {item.alt = info.name + " branding";});
            }
            if (typeof info.showApplicationLogo === "boolean") {
                document.querySelectorAll("[data-optional-brand]").forEach(item => {item.hidden = !info.showApplicationLogo;});
            }
        } catch { /* The current encounter stays usable while the connection recovers. */ }
    }
    scan();
    new MutationObserver(scan).observe(document.body, {childList:true,subtree:true});
    window.trackerMedia = {refresh};
    if (document.body.dataset.mediaLive !== "true") return;
    async function start() {
        if (!window.signalR) {
            const script = document.createElement("script");
            script.src = "/lib/microsoft/signalr/dist/browser/signalr.min.js";
            script.onload = start;
            document.head.appendChild(script);
            return;
        }
        const connection = new signalR.HubConnectionBuilder().withUrl("/hubs/encounter").withAutomaticReconnect().build();
        connection.on("MediaUpdated", refresh);
        connection.onreconnected(refresh);
        connection.onclose(() => setTimeout(connect, 5000));
        async function connect() {try {await connection.start(); await refresh();} catch {setTimeout(connect,5000);}}
        await connect();
    }
    start();
})();
