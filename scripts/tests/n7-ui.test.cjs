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

for (const [width, height, reducedMotion] of [[1280, 800, 'no-preference'], [390, 844, 'reduce'], [844, 390, 'no-preference']]) {
    test(`long encounter keeps public header visible through turns and manual scroll at ${width}x${height}`, async () => {
        const page = await fixture(`<div class="app-shell"><main class="app-main">
            <div class="initiative-display"><header class="initiative-header">
                <div id="roundDisplay" class="initiative-header-status"></div>
                <div class="initiative-title">Initiative</div>
                <div class="initiative-header-actions"><div id="turnDisplay" class="initiative-header-status"></div></div>
            </header><main id="initiativeList" class="initiative-list" tabindex="0" aria-label="Initiative order"></main>
            </div></main></div>`, width);
        try {
            await page.setViewportSize({ width, height });
            await page.emulateMedia({ reducedMotion });
            await page.addStyleTag({ content: source('lib/bootstrap/dist/css/bootstrap.min.css') });
            await page.addStyleTag({ content: source('css/site.css') });
            await page.addStyleTag({ content: source('css/condition-badges.css') });
            await page.addStyleTag({ content: source('css/initiative-display.css') });
            await page.addScriptTag({ content: source('js/initiative-display.js') });
            await page.evaluate(() => document.dispatchEvent(new Event('DOMContentLoaded')));
            const renderTurn = async active => page.evaluate(active => {
                window.hubEvents.EncounterUpdated({ isStarted: true, round: 3, currentTurnNumber: active + 1,
                    activeCombatantCount: 40, combatants: Array.from({ length: 40 }, (_, index) => ({
                        id: String(index), name: `Combatant ${index + 1}`, isActive: true, isCurrent: index === active,
                        initiative: 40 - index, currentHp: 10, maxHp: 20, temporaryHp: 0, exhaustionLevel: 2,
                        conditions: [{ name: 'Prone', styleKey: 'prone' }, { name: 'Poisoned', styleKey: 'poisoned' }]
                    })) });
            }, active);
            const assertHeader = async () => {
                const metrics = await page.evaluate(() => ({
                    headerTop: document.querySelector('.initiative-header').getBoundingClientRect().top,
                    headerBottom: document.querySelector('.initiative-header').getBoundingClientRect().bottom,
                    scrollY: window.scrollY, pageHeight: document.documentElement.scrollHeight, height: innerHeight
                }));
                assert(Math.abs(metrics.headerTop) <= 1, 'round/initiative/turn header stays at viewport top');
                assert(metrics.headerBottom < metrics.height, 'list has usable viewport space');
                assert.equal(metrics.scrollY, 0, 'automatic scrolling never moves the document');
                assert.equal(metrics.pageHeight, metrics.height, 'only the list scrolls');
            };
            for (const active of [0, 1, 2, 8, 16, 25, 39, 0, 20, 38]) {
                await renderTurn(active);
                await page.waitForFunction(() => {
                    const row = document.querySelector('.initiative-row.is-current').getBoundingClientRect();
                    const list = document.querySelector('#initiativeList').getBoundingClientRect();
                    return row.top >= list.top - 1 && row.bottom <= list.bottom + 1;
                });
                await assertHeader();
                assert.match(await page.locator('#roundDisplay').innerText(), /3/);
                assert.match(await page.locator('#turnDisplay').innerText(), new RegExp(String(active + 1)));
            }
            const list = page.locator('#initiativeList');
            await list.evaluate(element => element.scrollTo({ top: 0, behavior: 'instant' }));
            await list.hover();
            await page.mouse.wheel(0, 1000);
            await page.waitForFunction(() => document.querySelector('#initiativeList').scrollTop > 0);
            await assertHeader();
            await list.focus();
            await page.keyboard.press('End');
            // Wait for the browser's native keyboard scroll before the next synthetic hub event.
            await page.waitForFunction(() => {
                const element = document.querySelector('#initiativeList');
                return element.scrollTop + element.clientHeight >= element.scrollHeight - 1;
            });
            await assertHeader();
            await renderTurn(0);
            await page.waitForFunction(() => document.querySelector('.initiative-row.is-current').getBoundingClientRect().top >= document.querySelector('#initiativeList').getBoundingClientRect().top - 1);
            await assertHeader();
        } finally { await page.close(); }
    });
}

for (const width of [1280, 390]) {
    test(`DM exhaustion stays beside editable and active names at ${width}px`, async () => {
        const page = await fixture(`<div class="container-fluid dm-page"><div class="row">
            <div class="col-md-4"><div class="dm-combatant-name-heading" id="setup-heading">
                <input class="form-control form-control-sm" value="Player character">
                <span class="condition-badge dm-exhaustion-badge">Exhaustion 3</span>
            </div></div></div>
            <div class="dm-combatant-name-heading" id="active-heading">
                <span class="fw-bold fs-5">Player character</span>
                <span class="condition-badge dm-exhaustion-badge">Exhaustion 5</span>
            </div><div class="condition-badges"><span class="condition-badge">Prone</span></div>
        </div>`, width);
        try {
            for (const file of ['lib/bootstrap/dist/css/bootstrap.min.css', 'css/site.css', 'css/dm.css', 'css/condition-badges.css'])
                await page.addStyleTag({ content: source(file) });
            for (const id of ['setup-heading', 'active-heading']) {
                const heading = page.locator('#' + id);
                const name = await heading.locator(':scope > :first-child').boundingBox();
                const badge = await heading.locator('.dm-exhaustion-badge').boundingBox();
                assert(badge.x >= name.x + name.width, 'badge immediately follows the name');
                assert(badge.y < name.y + name.height && badge.y + badge.height > name.y, 'name and exhaustion share a line');
                assert(badge.x + badge.width <= width, 'badge remains within viewport');
            }
            assert.equal(await page.locator('.condition-badges .dm-exhaustion-badge').count(), 0);
        } finally { await page.close(); }
    });
}
