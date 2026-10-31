// Layout/security fixtures use actual repository CSS and JS, without physical devices.
const {test, before, after} = require('node:test');
const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const {resolve} = require('node:path');
const {chromium} = require('playwright');
const root = resolve(__dirname, '../..');
const source = file => readFileSync(resolve(root, 'InitiativeTracker.Web/wwwroot', file), 'utf8');
let browser;
before(async () => { browser = await chromium.launch({headless: true,
    ...(process.env.N7_CHROMIUM_PATH ? {executablePath: process.env.N7_CHROMIUM_PATH} : {})}); });
after(async () => {await browser?.close();});

for (const width of [390, 844, 1280]) {
    test(`branding, portrait, banner and touch controls fit ${width}px`, async () => {
        const page = await browser.newPage({viewport:{width, height:800}});
        try {
            await page.route('http://n9.test/**', route => {
                const path = new URL(route.request().url()).pathname;
                if (path.endsWith('.svg')) return route.fulfill({contentType:'image/svg+xml', body:source(path.slice(1))});
                return route.fulfill({contentType:'text/html', body:`<meta name="viewport" content="width=device-width,initial-scale=1">
                <style>body{margin:0;padding:16px;font:16px system-ui}*{box-sizing:border-box}button,input{font:inherit}input{width:100%}</style>
                <section class="tracker-waiting"><div class="tracker-brand"><div><span class="tracker-brand-name">A very long campaign and customer application name</span><span class="tracker-brand-tagline">Your table. Your story.</span></div></div>
                <h1>Waiting for your hub</h1><p role="status">Reconnecting automatically</p></section>
                <div class="tracker-media-grid"><div><img class="tracker-banner" src="/images/default-banner.svg" alt=""><div class="tracker-identity"><img src="/images/default-character.svg" class="tracker-portrait tracker-portrait--sheet" alt="Character portrait"><div>A character with a long fantasy name</div></div></div>
                <form><label>Portrait<input type="file" accept="image/png,image/jpeg"></label><button type="button">Upload portrait</button></form></div>`});
            });
            await page.goto('http://n9.test/');
            await page.addStyleTag({content:source('css/branding.css')});
            await page.locator('.tracker-portrait').evaluate(img => img.decode());
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
            const portrait = await page.locator('.tracker-portrait').boundingBox();
            assert.equal(portrait.width, portrait.height);
            assert((await page.getByRole('button', {name:'Upload portrait', exact:true}).boundingBox()).height >= 44);
            assert(await page.locator('.tracker-banner').evaluate(img => img.complete && img.naturalWidth === 1600));
            assert.equal(await page.locator('.tracker-portrait').getAttribute('src'), '/images/default-character.svg');
        } finally {await page.close();}
    });
}

test('pairing reports unavailable hub, retries automatically, then preserves expiry recovery', async () => {
    const page = await browser.newPage();
    try {
        await page.route('http://n9.test/', route => route.fulfill({contentType:'text/html',body:'<div id="pairingStatus" role="status"></div><form id="restartPairingForm" class="d-none"><button>Try Again</button></form><style>.d-none{display:none}</style>'}));
        await page.goto('http://n9.test/');
        await page.evaluate(() => {
            window.setInterval = callback => {window.retryPairing = callback;};
            window.fetch = async () => {throw new Error('offline fixture');};
        });
        await page.addScriptTag({content:source('js/display-pairing.js')});
        await page.getByRole('status').filter({hasText:'Retrying automatically'}).waitFor();
        await page.evaluate(async () => {window.fetch = async () => ({ok:true,json:async () => ({status:'pending'})}); await window.retryPairing();});
        assert.equal(await page.getByRole('status').innerText(), 'Waiting for DM approval…');
        await page.evaluate(async () => {window.fetch = async () => ({ok:true,json:async () => ({status:'expired'})}); await window.retryPairing();});
        assert(await page.getByRole('button', {name:'Try Again'}).isVisible());
        assert.equal(await page.getByRole('status').innerText(), 'Pairing request expired.');
    } finally {await page.close();}
});
