// Actual Razor pages from an explicitly supplied disposable local test host.
// N9_APP_URL must never point to a working hub; these checks create pairing requests.
const {test,before,after} = require('node:test');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
const {mkdirSync} = require('node:fs');
const {join} = require('node:path');
const base = process.env.N9_APP_URL;
let browser;
before(async () => {
    if (!base) return;
    const origin = new URL(base);
    assert(['127.0.0.1','localhost','[::1]'].includes(origin.hostname), 'use a disposable loopback test host only');
    browser = await chromium.launch({headless:true,
        ...(process.env.N7_CHROMIUM_PATH ? {executablePath:process.env.N7_CHROMIUM_PATH} : {})});
});
after(async () => {await browser?.close();});
for (const width of [390,844,1280]) {
    for (const path of ['/setup','/join','/display/pair']) {
        test(`actual ${path} shows original branding and fits ${width}px`, {skip:!base}, async () => {
            const page = await browser.newPage({viewport:{width,height:800}});
            try {
                const response = await page.goto(base + path);
                assert.equal(response.status(),200);
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

test('actual upload, encounter pairing and realtime portrait/banner replacement preserve the table', {skip:!base}, async () => {
    const dm = await browser.newPage({viewport:{width:1280,height:800}});
    const player = await browser.newPage({viewport:{width:390,height:844}});
    const display = await browser.newPage({viewport:{width:1280,height:800}});
    try {
        await dm.goto(base+'/setup');
        const code=await dm.locator('.setup-grid section').nth(1).locator('strong').innerText();
        const pin=await dm.locator('.setup-grid section').nth(2).locator('strong').innerText();
        await dm.goto(base+'/dm/login'); await dm.locator('[name=Pin]').fill(pin);
        await dm.getByRole('button',{name:'Continue',exact:true}).click(); await dm.waitForURL('**/dm');
        await player.goto(base+'/join'); await player.locator('[name=JoinCode]').fill(code);
        await player.locator('[name=PlayerName]').fill('N9 browser fixture');
        await player.getByRole('button',{name:'Create Player Profile',exact:true}).click();
        await player.getByRole('link',{name:'Continue to My Characters'}).click();
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
        await display.waitForURL('**/display');
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
        assert.equal(new URL(display.url()).pathname,'/display','pairing survives all media changes');
        assert.equal(await display.locator('.initiative-row').count(),1);
        for(const width of [390,844,1280]){
            await display.setViewportSize({width,height:800});
            assert(await display.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
            if(process.env.N9_SCREENSHOTS)await display.screenshot({path:join(process.env.N9_SCREENSHOTS,'display-media-'+width+'.png'),fullPage:true});
        }
        await dm.goto(base+'/dm');
        for(const width of [390,844,1280]){
            await dm.setViewportSize({width,height:800});
            assert(await dm.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
            if(process.env.N9_SCREENSHOTS)await dm.screenshot({path:join(process.env.N9_SCREENSHOTS,'dm-media-'+width+'.png'),fullPage:true});
        }
    } finally {await dm.close();await player.close();await display.close();}
});
