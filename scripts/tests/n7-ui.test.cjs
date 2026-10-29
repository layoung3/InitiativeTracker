// Run with Node and Playwright available: node --test scripts/tests/n7-ui.test.cjs
// Entirely synthetic browser fixtures: no running app, database or network device.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { chromium } = require('playwright');
const root = resolve(__dirname, '../..');
const source = file => readFileSync(resolve(root, 'InitiativeTracker.Web/wwwroot', file), 'utf8');
let browser;
before(async () => {
    browser = await chromium.launch({ headless: true,
        ...(process.env.N7_CHROMIUM_PATH ? { executablePath: process.env.N7_CHROMIUM_PATH } : {}) });
});
after(async () => { await browser?.close(); });

async function fixture(html, width = 1280) {
    const page = await browser.newPage({ viewport: { width, height: 800 } });
    await page.route('http://n7.test/', route => route.fulfill({ contentType: 'text/html', body: html }));
    await page.goto('http://n7.test/');
    await page.evaluate(() => {
        window.hubEvents = {};
        window.signalR = {
            HubConnectionState: { Disconnected: 'Disconnected' },
            HubConnectionBuilder: class {
                withUrl() { return this; }
                withAutomaticReconnect() { return this; }
                build() {
                    return { state: 'Disconnected', start: async () => {},
                        on: (name, callback) => { window.hubEvents[name] = callback; },
                        onclose() {}, onreconnecting() {}, onreconnected() {},
                        invoke: async name => name === 'GetDisplayTheme' ? 'dark' : { combatants: [], isStarted: false } };
                }
            }
        };
    });
    return page;
}

test('visibility submit is a no-JS fallback; JS shows retry only on failure', async () => {
    const page = await fixture(`<form method="post" action="/visibility" data-visibility-autosave>
        <input type="checkbox" name="isHiddenFromPublic" value="true">
        <input type="checkbox" name="isHpHiddenFromPublic" value="true">
        <button type="submit">Save Visibility</button></form>`);
    try {
        const button = page.locator('button');
        assert(await button.isVisible(), 'fallback works before JavaScript enhances the form');
        let fail = true;
        let calls = 0;
        await page.route('http://n7.test/visibility', async route => {
            calls++;
            await new Promise(resolve => setTimeout(resolve, 150));
            await route.fulfill({ status: fail ? 500 : 200, contentType: 'application/json', body: fail ? '{}' : '{"saved":true}' });
        });
        await page.addScriptTag({ content: source('js/dm-live.js') });
        assert(await button.isHidden());
        await page.locator('[name=isHiddenFromPublic]').check();
        await page.getByRole('status').filter({ hasText: 'Saving' }).waitFor();
        assert(await button.isHidden());
        await page.getByRole('status').filter({ hasText: 'Not saved' }).waitFor();
        assert(await button.isVisible());
        assert.equal(await button.innerText(), 'Retry save');
        assert(await page.locator('[name=isHiddenFromPublic]').isChecked());
        fail = false;
        await button.click();
        await page.getByRole('status').filter({ hasText: /^Saved$/ }).waitFor();
        assert(await button.isHidden());
        assert.equal(calls, 2);
    } finally { await page.close(); }
});

for (const width of [1280, 390]) {
    test(`display exhaustion follows name; conditions and HP stay separate at ${width}px`, async () => {
        const page = await fixture(`<div class="initiative-display"><header class="initiative-header">
            <div id="roundDisplay"></div><div id="turnDisplay"></div></header>
            <div id="initiativeList" class="initiative-list"></div></div>`, width);
        try {
            await page.addStyleTag({ content: source('css/condition-badges.css') });
            await page.addStyleTag({ content: source('css/initiative-display.css') });
            await page.addScriptTag({ content: source('js/initiative-display.js') });
            await page.evaluate(() => document.dispatchEvent(new Event('DOMContentLoaded')));
            const render = async (exhaustionLevel, conditions = []) => page.evaluate(({ exhaustionLevel, conditions }) => {
                window.hubEvents.EncounterUpdated({ isStarted: true, round: 2, currentTurnNumber: 1, activeCombatantCount: 1,
                    combatants: [{ id: 'fixture', name: 'Test Hero', isActive: true, isCurrent: true, initiative: 15,
                        currentHp: 10, maxHp: 20, temporaryHp: 0, exhaustionLevel, conditions }] });
            }, { exhaustionLevel, conditions });
            await render(0);
            const hpBefore = await page.locator('.combatant-hp').boundingBox();
            await render(3, [{ name: 'Prone', styleKey: 'prone' }, { name: 'Poisoned', styleKey: 'poisoned' }]);
            const heading = page.locator('.combatant-name-heading');
            assert.deepEqual(await heading.locator(':scope > *').allTextContents(), ['Test Hero', 'Exhaustion 3']);
            assert.deepEqual(await page.locator('.condition-badges > *').allTextContents(), ['Prone', 'Poisoned']);
            const name = await page.locator('.combatant-name-text').boundingBox();
            const exhaustion = await heading.locator('.condition-badge').boundingBox();
            const conditions = await page.locator('.condition-badges').boundingBox();
            const hpAfter = await page.locator('.combatant-hp').boundingBox();
            assert(exhaustion.x >= name.x + name.width, 'exhaustion follows the name');
            assert(exhaustion.y < name.y + name.height && exhaustion.y + exhaustion.height > name.y, 'name and exhaustion share a line');
            assert(conditions.y > Math.max(name.y, exhaustion.y), 'conditions remain underneath');
            assert.equal(hpAfter.x, hpBefore.x, 'HP horizontal placement unchanged');
            assert.equal(hpAfter.width, hpBefore.width);
            await render(5);
            assert.equal(await heading.locator('.condition-badge').innerText(), 'Exhaustion 5');
            assert.equal(await page.locator('.condition-badges').count(), 0);
            await render(0, [{ name: 'Prone', styleKey: 'prone' }]);
            assert.equal(await heading.locator('.condition-badge').count(), 0);
            assert.deepEqual(await page.locator('.condition-badges > *').allTextContents(), ['Prone']);
        } finally { await page.close(); }
    });
}
