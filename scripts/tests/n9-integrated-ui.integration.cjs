"use strict";
// Actual composite Razor responses captured after secure enrollment in TestServer.
// Local response replay checks layout; it does not establish browser TLS trust.
const {test} = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const {chromium} = require("playwright");
const htmlRoot = process.env.N9_INTEGRATION_HTML_DIRECTORY;
const sourceRoot = process.env.N9_INTEGRATION_SOURCE_DIRECTORY;
if (!htmlRoot || !sourceRoot) throw new Error("Provide disposable captured HTML and composite source directories");
for (const width of [390,844,1280]) for (const name of ["editor","character","branding","secure-access"]) {
    test(`integrated ${name} Razor response fits ${width}px`, async () => {
        const browser = await chromium.launch({headless:true, executablePath:process.env.N7_CHROMIUM_PATH || undefined});
        try {
            const page = await browser.newPage({viewport:{width,height:900}});
            const assets = path.resolve(sourceRoot,"InitiativeTracker.Web/wwwroot");
            await page.route("**/*", async route => {
                const url = new URL(route.request().url());
                if (url.pathname === "/integration-page") return route.fulfill({contentType:"text/html",body:await fs.readFile(path.join(htmlRoot,name+".html"),"utf8")});
                if (url.pathname.startsWith("/media/")) return route.fulfill({contentType:"image/svg+xml",body:await fs.readFile(path.join(assets,url.pathname.endsWith("banner")?"images/default-banner.svg":"images/default-character.svg"))});
                if (url.pathname === "/branding/logo") return route.fulfill({contentType:"image/png",body:await fs.readFile(path.join(assets,"images/colophon.png"))});
                // Production Razor uses the SDK's fingerprinted resource names.
                const localPath = url.pathname.replace(/\.[a-z0-9]{10}(?=\.(css|js|svg|png|woff2)$)/i,"");
                const candidate = localPath === "/InitiativeTracker.Web.styles.css"
                    ? path.join(sourceRoot,"InitiativeTracker.Web/obj/Release/net10.0/scopedcss/bundle/InitiativeTracker.Web.styles.css")
                    : path.resolve(assets,"."+localPath);
                if (candidate.startsWith(assets+path.sep) || localPath === "/InitiativeTracker.Web.styles.css") {
                    try {
                        const types = {".css":"text/css",".js":"application/javascript",".svg":"image/svg+xml",".png":"image/png",".woff2":"font/woff2"};
                        return route.fulfill({contentType:types[path.extname(candidate)]||"application/octet-stream",body:await fs.readFile(candidate)});
                    } catch { /* Fail closed to external/network/protocol requests. */ }
                }
                return route.fulfill({status:404,body:"Disposable layout fixture"});
            });
            await page.goto("http://fixture.invalid/integration-page");
            await page.waitForTimeout(250);
            assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
            // The advanced security navigation is deliberately hidden from normal pages.
            assert.equal(await page.locator("nav[aria-label='Private feature authorization']").count(), 0);
            if (name === "editor" || name === "branding") {
                assert.match(await page.locator("body").innerText(),/HTTPS alone is insufficient/);
                assert.equal(await page.locator(".tracker-media-grid").evaluate(element=>getComputedStyle(element).display),"grid");
            }
            assert.equal(await page.locator("img[data-tracker-media]").evaluateAll(images=>images.every(image=>image.complete&&image.naturalWidth>0)),true);
            if (process.env.N9_SCREENSHOTS) {
                await fs.mkdir(process.env.N9_SCREENSHOTS,{recursive:true});
                await page.screenshot({path:path.join(process.env.N9_SCREENSHOTS,`${name}-${width}.png`),fullPage:true});
            }
        } finally {await browser.close();}
    });
}
