// Actual Razor pages from an explicitly supplied disposable local test host.
// N9_APP_URL must never point to a working hub; these checks create pairing requests.
const {test,before,after} = require('node:test');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
const {mkdirSync,existsSync,realpathSync} = require('node:fs');
const {join,resolve,sep} = require('node:path');
const {tmpdir} = require('node:os');
const base = process.env.N9_APP_URL;
const tlsSandbox=process.env.N9_TLS_SANDBOX;
let browser;
const launchOptions=()=>({headless:true,
    ...(process.env.N7_CHROMIUM_PATH ? {executablePath:process.env.N7_CHROMIUM_PATH} : {}),
    ...(tlsSandbox ? {env:{...process.env,HOME:join(tlsSandbox,'browser-home'),XDG_CONFIG_HOME:join(tlsSandbox,'config'),XDG_DATA_HOME:join(tlsSandbox,'data')},
        args:['--host-resolver-rules=MAP hub.owner-domain.org 127.0.0.1','--no-proxy-server']} : {})});
async function authorizeMember(page,dm) {
    await page.goto(base+'/secure-access');
    await page.getByRole('button',{name:'Request secure approval',exact:true}).click();
    const reference=(await page.locator('p').filter({hasText:/Browser reference .*; identity/}).innerText()).match(/Browser reference ([^;]+);/)[1];
    await dm.goto(base+'/secure-access');
    await dm.locator('form[action*=Approve]').filter({hasText:reference}).getByRole('button',{name:'Approve exact request',exact:true}).click();
    await page.reload();
    // Approved member requests now auto-finish in their own browser. This also
    // exercises the original secure-DM approval path without manual button entry.
    await page.getByRole('heading',{name:/Authorized as/}).waitFor({timeout:20000});
    return await page.locator('form[action*=Revoke] input[name=id]').first().inputValue();
}
before(async () => {
    if (!base) return;
    const origin = new URL(base);
    if(tlsSandbox) {
        const sandbox=realpathSync(tlsSandbox);
        assert(sandbox.startsWith(realpathSync(tmpdir())+sep),'disposable process trust only');
        assert(existsSync(join(sandbox,'disposable-n9-browser-fixture')));
        assert.equal(origin.origin,'https://hub.owner-domain.org:5445');
        // A fresh child process without the fixture root must reject this CA.
        const options=launchOptions();
        options.env={...options.env,HOME:join(sandbox,'untrusted-home'),XDG_CONFIG_HOME:join(sandbox,'untrusted-config'),XDG_DATA_HOME:join(sandbox,'untrusted-data')};
        mkdirSync(options.env.HOME,{recursive:true});
        const untrusted=await chromium.launch(options);
        try {
            const page=await untrusted.newPage({ignoreHTTPSErrors:false});
            await assert.rejects(()=>page.goto(base+'/setup'),/ERR_CERT_AUTHORITY_INVALID/);
        } finally {await untrusted.close();}
    } else assert(['127.0.0.1','localhost','[::1]'].includes(origin.hostname),'use a disposable loopback test host only');
    browser=await chromium.launch(launchOptions());
});
after(async () => {await browser?.close();});
for (const width of [390,844,1280]) {
    for (const path of ['/setup','/join','/display/pair']) {
        test(`actual ${path} shows original branding and fits ${width}px`, {skip:!base}, async () => {
            const page = await browser.newPage({viewport:{width,height:800}});
            try {
                const response = await page.goto(base + path);
                assert.equal(response.status(),200);
                if(tlsSandbox) assert.match((await response.securityDetails()).issuer,/Disposable Process Only Root/);
                const logo = page.locator('img.tracker-brand-logo');
                await logo.evaluate(image => image.decode());
                assert.deepEqual(await logo.evaluate(image => [image.naturalWidth,image.naturalHeight]),[300,300]);
                assert.equal(await logo.getAttribute('src'),'/branding/logo');
                assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
                if (process.env.N9_SCREENSHOTS) {
                    mkdirSync(process.env.N9_SCREENSHOTS,{recursive:true});
                    await page.screenshot({path:join(process.env.N9_SCREENSHOTS,path.slice(1).replaceAll('/','-')+'-'+width+'.png'),fullPage:true});
                }
            } finally {await page.close();}
        });
    }
}

test('physical setup display pairing uses loopback-safe relative URL', {skip:!base}, async () => {
    const page=await browser.newPage();
    try {
        const response=await page.goto(base+'/setup');
        assert.equal(response.status(),200);
        const href=await page.getByRole('link',{name:'Display Pairing',exact:true}).getAttribute('href');
        assert.match(href,/^\/display\/connect\?token=[^&]+$/);
        // The browser stays at the local origin even when the QR advertises a
        // different LAN/hotspot address for external displays.
        assert.equal(new URL(href,page.url()).origin,new URL(base).origin);
        assert(await page.locator('img[alt="Connect a display device"]').getAttribute('src'));
    } finally {await page.close();}
});

test('physical HTTPS authorization screen retains direct Setup escape', {skip:!base || !tlsSandbox}, async () => {
    const page=await browser.newPage();
    try {
        const response=await page.goto(base+'/secure-access/physical');
        assert.equal(response.status(),200);
        const setup=page.getByRole('navigation',{name:'Hub navigation'}).getByRole('link',{name:'Hub Setup'});
        assert.equal(await setup.getAttribute('href'),'/setup');
        const next=await Promise.all([page.waitForNavigation(),setup.click()]);
        assert.equal(next[0].status(),200);
        assert.equal(new URL(page.url()).pathname,'/setup');
    } finally {await page.close();}
});

test('physical setup network status stays on the hub and remains read-only', {skip:!base}, async () => {
    const page=await browser.newPage();
    try {
        const response=await page.goto(base+'/setup');
        assert.equal(response.status(),200);
        const link=page.getByRole('link',{name:'Open Network Status',exact:true});
        assert.equal(await link.getAttribute('href'),'/setup/network-status');
        const target=await Promise.all([page.waitForNavigation(),link.click()]);
        assert.equal(target[0].status(),200);
        assert.equal(new URL(page.url()).pathname,'/setup/network-status');
        await page.getByRole('heading',{name:'Hub network status',exact:true}).waitFor();
        assert.equal(await page.locator('form').count(),0,'physical readout must have no network mutation forms');
        assert.equal(await page.getByRole('link',{name:'Back to Hub Setup'}).getAttribute('href'),'/setup');
    } finally {await page.close();}
});

test('physical HTTPS overlay denies then approves exact browser without transcribing a code', {skip:!base || !tlsSandbox}, async () => {
    const requester=await browser.newPage();
    const hub=await browser.newPage();
    try {
        await hub.goto(base+'/setup');
        await requester.goto(base+'/secure-access');
        const begin=async () => {
            await requester.locator('[name=recovery]').check();
            await requester.getByRole('button',{name:'Request physical DM proof',exact:true}).click();
            await requester.locator('[data-browser-reference]').waitFor();
            return (await requester.locator('[data-browser-reference]').getAttribute('data-browser-reference'));
        };
        const reference=await begin();
        const overlay=hub.locator('#physicalApprovalOverlay');
        await overlay.waitFor({state:'visible',timeout:20000});
        assert.equal((await hub.locator('#physicalApprovalReference').innerText()).trim(),reference);
        await overlay.getByRole('button',{name:'Deny'}).click();
        await overlay.waitFor({state:'hidden'});
        await requester.reload();
        assert.equal(await requester.locator('[data-browser-reference]').count(),0);

        const second=await begin();
        await overlay.waitFor({state:'visible',timeout:20000});
        assert.equal((await hub.locator('#physicalApprovalReference').innerText()).trim(),second);
        await overlay.getByRole('button',{name:'Approve',exact:true}).click();
        await requester.getByRole('heading',{name:'Authorized as DM',exact:true}).waitFor({timeout:20000});
        await overlay.waitFor({state:'hidden',timeout:15000});
    } finally {
        await hub.close(); await requester.close();
    }
});

test('physical overlay approves a joined player and a paired display in their own browsers', {skip:!base || !tlsSandbox}, async () => {
    const hub=await browser.newPage();
    const player=await browser.newPage();
    const dm=await browser.newPage();
    const display=await browser.newPage();
    try {
        await hub.goto(base+'/setup');
        const join=await hub.locator('.setup-grid section').nth(1).locator('strong').innerText();
        await dm.goto(base+'/secure-access');
        await dm.locator('[name=recovery]').check();
        await dm.getByRole('button',{name:'Request physical DM proof',exact:true}).click();
        const modal=hub.locator('#physicalApprovalOverlay');
        await modal.waitFor({state:'visible',timeout:20000});
        await modal.getByRole('button',{name:'Approve',exact:true}).click();
        await dm.getByRole('heading',{name:'Authorized as DM',exact:true}).waitFor({timeout:20000});

        await player.goto(base+'/join');
        await player.locator('[name=JoinCode]').fill(join);
        await player.locator('[name=PlayerName]').fill('Physical browser join fixture');
        await player.getByRole('button',{name:'Create Player Profile',exact:true}).click();
        await player.getByRole('link',{name:'Continue to My Characters'}).click();
        await player.goto(base+'/secure-access');
        await player.getByRole('button',{name:'Request secure approval',exact:true}).click();
        const playerRef=await player.locator('[data-browser-reference]').getAttribute('data-browser-reference');
        await modal.waitFor({state:'visible',timeout:20000});
        assert.equal((await hub.locator('#physicalApprovalReference').innerText()).trim(),playerRef);
        assert.match(await hub.locator('#physicalApprovalPurpose').innerText(),/Player authorization/);
        const playerIdentity=(await player.locator('[data-browser-reference]').innerText()).match(/[0-9a-f]{8}-[0-9a-f-]{27,}/i)?.[0];
        assert(playerIdentity,'requester must display the exact player identity');
        assert((await hub.locator('#physicalApprovalIdentity').innerText()).includes(playerIdentity),
            'physical overlay must show the same player identity as the requesting browser');
        await modal.getByRole('button',{name:'Approve',exact:true}).click();
        await player.getByRole('heading',{name:'Authorized as Player',exact:true}).waitFor({timeout:20000});
        await player.locator('form[action*=Remember] [name=label]').fill('Player phone fixture');
        await player.getByRole('button',{name:'Remember this browser for 30 days',exact:true}).click();
        await player.getByText('Player phone fixture').waitFor();

        await display.goto(base+'/display/pair');
        const pair=await display.locator('.display-2').innerText();
        await dm.goto(base+'/dm/devices');
        await dm.locator('.list-group-item').filter({hasText:pair.trim()})
            .getByRole('button',{name:'Approve',exact:true}).click();
        await display.waitForURL('**/secure-access/wait*');
        await display.goto(base+'/secure-access');
        await display.getByRole('button',{name:'Request secure approval',exact:true}).click();
        const displayRef=await display.locator('[data-browser-reference]').getAttribute('data-browser-reference');
        await modal.waitFor({state:'visible',timeout:20000});
        assert.equal((await hub.locator('#physicalApprovalReference').innerText()).trim(),displayRef);
        assert.match(await hub.locator('#physicalApprovalPurpose').innerText(),/Display authorization/);
        const displayIdentity=(await display.locator('[data-browser-reference]').innerText()).match(/[0-9a-f]{8}-[0-9a-f-]{27,}/i)?.[0];
        assert(displayIdentity,'requester must display the exact display identity');
        assert((await hub.locator('#physicalApprovalIdentity').innerText()).includes(displayIdentity),
            'physical overlay must show the same display identity as the requesting browser');
        await modal.getByRole('button',{name:'Approve',exact:true}).click();
        await display.getByRole('heading',{name:'Authorized as Display',exact:true}).waitFor({timeout:20000});
        await display.locator('form[action*=Remember] [name=label]').fill('Table display fixture');
        await display.getByRole('button',{name:'Remember this browser for 30 days',exact:true}).click();
        await display.getByText('Table display fixture').waitFor();
        await dm.goto(base+'/secure-access');
        const remembered=dm.locator('form[action*=ForgetRemembered]').filter({hasText:'Player phone fixture'});
        await remembered.getByRole('button',{name:'Forget browser',exact:true}).click();
        await player.goto(base+'/secure-access');
        assert.equal(await player.getByRole('heading',{name:'Authorized as Player',exact:true}).count(),0,
            'revoked remembered player cannot silently regain authority');
    } finally {
        await hub.close();await player.close();await dm.close();await display.close();
    }
});

test('actual upload, encounter pairing and realtime portrait/banner replacement preserve the table', {skip:!base}, async () => {
    const dm = await browser.newPage({viewport:{width:1280,height:800}});
    const player = await browser.newPage({viewport:{width:390,height:844}});
    const display = await browser.newPage({viewport:{width:1280,height:800}});
    try {
        await dm.goto(base+'/setup');
        const code=await dm.locator('.setup-grid section').nth(1).locator('strong').innerText();
        if(tlsSandbox) {
            await dm.goto(base+'/secure-access');
            await dm.locator('[name=recovery]').check();
            const beginResponse=dm.waitForResponse(response=>response.url().includes('handler=BeginDm')&&response.request().method()==='POST');
            await dm.getByRole('button',{name:'Request physical DM proof',exact:true}).click();
            const begun=await beginResponse;
            assert.equal((await begun.request().allHeaders()).origin,new URL(base).origin,'browser form must retain a same-origin Origin header');
            assert.equal(begun.status(),302);
            await dm.locator('[data-browser-reference]').waitFor();
            await dm.goto(base+'/secure-access/physical');
            const proof=await dm.locator('[data-physical-code]').getAttribute('data-physical-code');
            await dm.goto(base+'/secure-access');
            await dm.locator('details').filter({hasText:'Manual code fallback'}).locator('summary').click();
            await dm.locator('[name=Code]').fill(proof);
            await Promise.all([dm.waitForNavigation({waitUntil:'load'}),dm.getByRole('button',{name:'Authorize this browser',exact:true}).click()]);
            await dm.getByRole('heading',{name:'Authorized as DM',exact:true}).waitFor();
        } else {
            const pin=await dm.locator('.setup-grid section').nth(2).locator('strong').innerText();
            await dm.goto(base+'/dm/login'); await dm.locator('[name=Pin]').fill(pin);
            await dm.getByRole('button',{name:'Continue',exact:true}).click(); await dm.waitForURL('**/dm');
        }
        await player.goto(base+'/join'); await player.locator('[name=JoinCode]').fill(code);
        await player.locator('[name=PlayerName]').fill('N9 browser fixture');
        await player.getByRole('button',{name:'Create Player Profile',exact:true}).click();
        await player.getByRole('link',{name:'Continue to My Characters'}).click();
        let playerGrant;
        if(tlsSandbox) {
            assert.equal(await player.evaluate(()=>fetch('/appearance/application/00000000-0000-0000-0000-000000000000').then(r=>r.status)),403);
            playerGrant=await authorizeMember(player,dm);
            await player.goto(base+'/player');
        }
        await player.locator('form[action*=CreateCharacter] [name=name]').fill('N9 Long Named Fantasy Hero');
        await player.locator('form[action*=CreateCharacter]').getByRole('button',{name:'Create',exact:true}).click();
        await player.locator('.player-character-card').first().waitFor();
        await player.waitForFunction(()=>document.querySelector('.player-character-card img.tracker-portrait')?.naturalWidth===320);
        if(process.env.N9_SCREENSHOTS)await player.screenshot({path:join(process.env.N9_SCREENSHOTS,'player-neutral-default-390.png'),fullPage:true});
        await player.getByRole('button',{name:'Add to encounter',exact:true}).click();
        const portraitPath=await player.locator('.player-character-card img.tracker-portrait').first().getAttribute('src');
        const characterId=portraitPath.split('/')[3];
        await display.goto(base+'/display/pair');
        const pairingCode=(await display.locator('.display-2').innerText()).trim();
        await dm.goto(base+'/dm/devices');
        const pending=dm.locator('.list-group-item').filter({hasText:pairingCode});
        await pending.getByRole('button',{name:'Approve',exact:true}).click();
        if(tlsSandbox) await display.waitForURL('**/secure-access/wait*');
        else await display.waitForURL('**/display');
        if(tlsSandbox) {await authorizeMember(display,dm);await display.goto(base+'/display');}
        await display.locator('.initiative-row').filter({hasText:'N9 Long Named Fantasy Hero'}).waitFor();
        // Browser-generated synthetic pixels: no original user image is modified.
        const makePng=async color=>Buffer.from(await player.evaluate(color=>{
            const canvas=document.createElement('canvas');canvas.width=100;canvas.height=60;
            const context=canvas.getContext('2d');context.fillStyle=color;context.fillRect(0,0,100,60);
            return canvas.toDataURL('image/png').split(',')[1];
        },color),'base64');
        await player.goto(base+'/appearance/character/'+characterId);
        const upload=async(kind,color)=>{
            await player.locator('#upload-'+kind).setInputFiles({name:kind+'.png',mimeType:'image/png',buffer:await makePng(color)});
            await player.getByRole('button',{name:'Upload '+kind,exact:true}).click();
            await player.getByRole('status').filter({hasText:'Appearance saved.'}).waitFor();
        };
        await upload('portrait','#e22d38');
        const image=display.locator('.initiative-row img.tracker-display-portrait');
        await display.waitForFunction(()=>document.querySelector('.tracker-display-portrait')?.src.includes('?v='));
        await image.evaluate(image=>image.decode());
        const pixel=async()=>image.evaluate(image=>{const canvas=document.createElement('canvas');canvas.width=1;canvas.height=1;canvas.getContext('2d').drawImage(image,0,0,1,1);return [...canvas.getContext('2d').getImageData(0,0,1,1).data];});
        await display.waitForFunction(()=>{const image=document.querySelector('.tracker-display-portrait');if(!image?.complete||!image.naturalWidth)return false;const c=document.createElement('canvas');c.width=1;c.height=1;c.getContext('2d').drawImage(image,0,0,1,1);return c.getContext('2d').getImageData(0,0,1,1).data[0]>200;});
        assert((await pixel())[0]>200);
        await upload('portrait','#2465dc');
        await display.waitForFunction(()=>{const image=document.querySelector('.tracker-display-portrait');if(!image?.complete||!image.naturalWidth)return false;const c=document.createElement('canvas');c.width=1;c.height=1;c.getContext('2d').drawImage(image,0,0,1,1);return c.getContext('2d').getImageData(0,0,1,1).data[2]>200;});
        assert((await pixel())[2]>200);
        await upload('banner','#394e66');
        for(const width of [390,844,1280]){
            await player.setViewportSize({width,height:800});
            assert(await player.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
            if(process.env.N9_SCREENSHOTS)await player.screenshot({path:join(process.env.N9_SCREENSHOTS,'appearance-'+width+'.png'),fullPage:true});
        }
        await player.goto(base+'/player/character/'+characterId);
        await player.waitForFunction(()=>{const image=document.querySelector('.character-header .tracker-banner');return image?.src.includes('?v=')&&image.complete&&image.naturalWidth===100;});
        for(const width of [390,844,1280]){
            await player.setViewportSize({width,height:800});
            assert(await player.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
            if(process.env.N9_SCREENSHOTS)await player.screenshot({path:join(process.env.N9_SCREENSHOTS,'character-'+width+'.png'),fullPage:true});
        }
        await dm.goto(base+'/appearance/application/00000000-0000-0000-0000-000000000000');
        await dm.locator('#upload-banner').setInputFiles({name:'campaign.png',mimeType:'image/png',buffer:await makePng('#74543a')});
        await dm.getByRole('button',{name:'Upload banner',exact:true}).click();
        await dm.getByRole('status').filter({hasText:'Appearance saved.'}).waitFor();
        await display.waitForFunction(()=>{const image=document.querySelector('.tracker-campaign-backdrop');return image?.src.includes('?v=')&&image.complete&&image.naturalWidth===100;});
        if(tlsSandbox) {
            await dm.locator('#upload-logo').setInputFiles({name:'customer-logo.png',mimeType:'image/png',buffer:await makePng('#446688')});
            await dm.getByRole('button',{name:'Upload logo',exact:true}).click();
            await dm.getByRole('status').filter({hasText:'Appearance saved.'}).waitFor();
            const publicPage=await browser.newPage();
            try {
                await publicPage.goto(base+'/join');
                await publicPage.locator('img.tracker-brand-logo').evaluate(image=>image.decode());
                assert.equal(await publicPage.locator('img.tracker-brand-logo').evaluate(image=>image.naturalWidth),100);
                await dm.getByRole('button',{name:'Restore default logo',exact:true}).click();
                await dm.getByRole('status').filter({hasText:'Appearance saved.'}).waitFor();
                await publicPage.reload();
                await publicPage.locator('img.tracker-brand-logo').evaluate(image=>image.decode());
                assert.equal(await publicPage.locator('img.tracker-brand-logo').evaluate(image=>image.naturalWidth),300);
            } finally {await publicPage.close();}
        }
        assert.equal(new URL(display.url()).pathname,'/display','pairing survives all media changes');
        assert.equal(await display.locator('.initiative-row').count(),1);
        for(const width of [390,844,1280]){
            await display.setViewportSize({width,height:800});
            assert(await display.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
            if(process.env.N9_SCREENSHOTS)await display.screenshot({path:join(process.env.N9_SCREENSHOTS,'display-media-'+width+'.png'),fullPage:true});
        }
        if(tlsSandbox) {
            // Same live TLS/SignalR flow: malformed input preserves displayed bytes.
            await player.goto(base+'/appearance/character/'+characterId);
            await player.locator('#upload-portrait').setInputFiles({name:'bad.png',mimeType:'image/png',buffer:Buffer.from('<svg onload="alert(1)"/>')});
            const bad=player.waitForResponse(response=>response.url().includes('handler=Upload')&&response.request().method()==='POST');
            await player.getByRole('button',{name:'Upload portrait',exact:true}).click();
            assert.equal((await bad).status(),400);
            assert((await pixel())[2]>200);
            // Revoke only this player's secure grant through the actual DM form.
            await dm.goto(base+'/secure-access');
            await dm.locator('form[action*=Revoke]').filter({has:dm.locator('input[name=id][value="'+playerGrant+'"]')}).getByRole('button',{name:'Revoke',exact:true}).click();
            assert.equal(await player.evaluate(path=>fetch(path).then(r=>r.status),'/media/character/'+characterId+'/portrait'),403);
            assert.equal(await player.evaluate(()=>{const form=document.querySelector('form[action*=Upload]');return fetch(form.action,{method:'POST',body:new FormData(form)}).then(r=>r.status);}),403);
            assert.equal(await display.evaluate(path=>fetch(path).then(r=>r.status),'/media/character/'+characterId+'/portrait'),200);
            assert.equal(new URL(display.url()).pathname,'/display');
            assert.equal(await display.locator('.initiative-row').count(),1);
        }
        await dm.goto(base+'/dm');
        for(const width of [390,844,1280]){
            await dm.setViewportSize({width,height:800});
            assert(await dm.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
            if(process.env.N9_SCREENSHOTS)await dm.screenshot({path:join(process.env.N9_SCREENSHOTS,'dm-media-'+width+'.png'),fullPage:true});
        }
    } finally {await dm.close();await player.close();await display.close();}
});


test('concurrent player and quick-connect display requests remain isolated', {skip:!base || !tlsSandbox}, async () => {
    const hub = await browser.newPage();
    const dm = await browser.newPage();
    const first = await browser.newPage();
    const second = await browser.newPage();
    const display = await browser.newPage();
    try {
        await hub.goto(base + '/setup');
        const joinCode = await hub.locator('.setup-grid section').nth(1).locator('strong').innerText();
        const displayQrPath = await hub.getByRole('link', {name:'Display Pairing', exact:true}).getAttribute('href');
        assert.match(displayQrPath, /^\/display\/connect\?token=/);
        await dm.goto(base + '/secure-access');
        await dm.locator('[name=recovery]').check();
        await dm.getByRole('button', {name:'Request physical DM proof', exact:true}).click();
        const overlay = hub.locator('#physicalApprovalOverlay');
        await overlay.waitFor({state:'visible', timeout:20000});
        await overlay.getByRole('button', {name:'Approve', exact:true}).click();
        await dm.getByRole('heading', {name:'Authorized as DM', exact:true}).waitFor({timeout:20000});

        // Prepare ordinary join and pairing BEFORE creating pending secure
        // approval requests. This synthetic fixture uses loopback for every
        // browser, unlike remote phones; an existing pending request would
        // otherwise open the physical-only overlay on a Join form and intercept
        // unrelated clicks. Production remote clients never receive that overlay.
        async function joinPlayer(page, name) {
            await page.goto(base + '/join');
            await page.locator('[name=JoinCode]').fill(joinCode);
            await page.locator('[name=PlayerName]').fill(name);
            await page.getByRole('button', {name:'Create Player Profile', exact:true}).click();
            // Keep the one-time recovery key visible until all three identities
            // exist; the Continue link now deliberately starts auto-enrollment.
            await page.getByRole('link', {name:'Continue to My Characters'}).waitFor();
        }
        await joinPlayer(first, 'Queue player A');
        await joinPlayer(second, 'Queue player B');

        // The exact Setup QR destination enters DM-approved pairing;
        // the QR invitation alone never grants a Display session.
        await display.goto(base + displayQrPath);
        await display.waitForURL('**/display/pair');
        await display.getByRole('heading', {name:'Pair Display', exact:true}).waitFor();
        const pairCode = (await display.locator('.display-2').innerText()).trim();
        await dm.goto(base + '/dm/devices');
        await dm.locator('.list-group-item').filter({hasText:pairCode})
            .getByRole('button', {name:'Approve', exact:true}).click();
        await display.waitForURL('**/secure-access/wait*');

        // Both players have completed normal profile creation, and display
        // pairing is DM-approved. Queue the short-lived secure requests together.
        async function queueMember(page) {
            await page.goto(base + '/secure-access');
            await page.getByRole('button', {name:'Request secure approval', exact:true}).click();
            return page.locator('[data-browser-reference]').getAttribute('data-browser-reference');
        }
        const firstRef = await queueMember(first);
        const secondRef = await queueMember(second);
        const displayRef = await queueMember(display);
        assert.equal(new Set([firstRef, secondRef, displayRef]).size, 3,
            'three independent browsers must have unique references');

        // A multi-request operator must be able to select an exact browser
        // without relying on whichever pending item the compact overlay shows
        // first. The standalone physical page exposes each pending request.
        const decideExact = async (reference, role, action) => {
            await hub.goto(base + '/secure-access/physical');
            const section = hub.locator('section[data-request]').filter({hasText:reference});
            await section.waitFor({state:'visible', timeout:20000});
            assert.match(await section.innerText(), new RegExp(role + ' authorization'));
            const buttonName = action === 'Approve' ? 'Approve browser' : 'Deny request';
            await section.getByRole('button', {name:buttonName, exact:true}).click();
        };
        await decideExact(firstRef, 'Player', 'Deny');
        await first.goto(base + '/secure-access');
        assert.equal(await first.locator('#finishMemberApproval').count(), 0,
            'denied browser must not retain a redeemable request');
        assert.equal(await first.getByRole('heading', {name:/Authorized as/}).count(), 0,
            'denied browser must remain unauthorized');

        await decideExact(secondRef, 'Player', 'Approve');
        await second.getByRole('heading', {name:'Authorized as Player', exact:true}).waitFor({timeout:20000});
        await display.reload();
        assert.equal(await display.getByRole('heading', {name:/Authorized as/}).count(), 0,
            'unapproved display must remain unauthorized');

        await decideExact(displayRef, 'Display', 'Approve');
        await display.getByRole('heading', {name:'Authorized as Display', exact:true}).waitFor({timeout:20000});
        await first.goto(base + '/secure-access');
        assert.equal(await first.getByRole('heading', {name:/Authorized as/}).count(), 0,
            'approval of other browsers must not grant the denied player access');
    } finally {
        await hub.close(); await dm.close(); await first.close(); await second.close(); await display.close();
    }
});


test('normal DM login and player QR journey wait for hub approval automatically', {skip:!base || !tlsSandbox}, async () => {
    const hub = await browser.newPage();
    const dm = await browser.newPage();
    const player = await browser.newPage();
    const rejected = await browser.newPage();
    try {
        await hub.goto(base + '/setup');
        const pin = await hub.locator('.setup-grid section').nth(2).locator('strong').innerText();
        const joinCode = await hub.locator('.setup-grid section').nth(1).locator('strong').innerText();
        const modal = hub.locator('#physicalApprovalOverlay');

        await dm.goto(base + '/dm/login');
        await dm.locator('[name=Pin]').fill(pin);
        await dm.getByRole('button', {name:'Continue', exact:true}).click();
        await dm.getByRole('heading', {name:'Waiting for approval on hub', exact:true}).waitFor({timeout:20000});
        const dmRef = await dm.locator('[data-browser-reference]').getAttribute('data-browser-reference');
        assert(dmRef, 'DM normal sign-in must queue approval automatically');
        assert.equal(new URL(dm.url()).pathname, '/secure-access/wait');
        await modal.waitFor({state:'visible', timeout:20000});
        assert.equal((await hub.locator('#physicalApprovalReference').innerText()).trim(), dmRef);
        await modal.getByRole('button', {name:'Approve', exact:true}).click();
        await dm.waitForURL('**/dm', {timeout:20000});

        async function joinNew(page, name) {
            await page.goto(base + '/join');
            await page.locator('[name=JoinCode]').fill(joinCode);
            await page.locator('[name=PlayerName]').fill(name);
            await page.getByRole('button', {name:'Create Player Profile', exact:true}).click();
            await page.getByText('Save your recovery key').waitFor();
            await page.getByRole('link', {name:'Continue to My Characters', exact:true}).click();
            await page.getByRole('heading', {name:'Waiting for approval on hub', exact:true}).waitFor({timeout:20000});
            assert.equal(new URL(page.url()).pathname, '/secure-access/wait');
            return page.locator('[data-browser-reference]').getAttribute('data-browser-reference');
        }
        const playerRef = await joinNew(player, 'Auto-approval player fixture');
        await modal.waitFor({state:'visible', timeout:20000});
        assert.equal((await hub.locator('#physicalApprovalReference').innerText()).trim(), playerRef);
        assert.match(await hub.locator('#physicalApprovalPurpose').innerText(), /Player authorization/);
        await modal.getByRole('button', {name:'Approve', exact:true}).click();
        await player.waitForURL('**/player', {timeout:20000});

        const deniedRef = await joinNew(rejected, 'Auto-denied player fixture');
        await modal.waitFor({state:'visible', timeout:20000});
        assert.equal((await hub.locator('#physicalApprovalReference').innerText()).trim(), deniedRef);
        await modal.getByRole('button', {name:'Deny', exact:true}).click();
        await rejected.getByRole('heading', {name:'Approval not completed', exact:true}).waitFor({timeout:20000});
        await rejected.waitForTimeout(3500);
        assert.equal(await rejected.getByRole('heading', {name:'Approval not completed', exact:true}).count(), 1,
            'denied request must not automatically resubmit');
        assert.equal(await modal.isVisible(), false, 'denied request must not appear again without retry');
    } finally {
        await hub.close(); await dm.close(); await player.close(); await rejected.close();
    }
});
