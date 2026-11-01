'use strict';
// Only run against a disposable loopback host. These tests mutate its encounter.
// Run separately AFTER *.test.cjs; never alongside the existing shared-host tests.
const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const base=process.env.N9_APP_URL;
const assets=path.resolve(__dirname,'../../InitiativeTracker.Web/wwwroot');
const output=process.env.SATELLITE_SCREENSHOTS;
const sizes=[[800,480],[1024,600],[480,800],[600,1024],[1280,800],[1920,1080]];
let browser;
before(async()=>{browser=await chromium.launch({headless:true,executablePath:process.env.N7_CHROMIUM_PATH||undefined});if(output)fs.mkdirSync(output,{recursive:true});});
after(async()=>{await browser?.close();});
async function screenshot(page,name){if(output)await page.screenshot({path:path.join(output,name+'.png')});}
async function measurements(page,compact,settle=true){
    // Flush pending style/layout and image decode before measuring a rendered frame.
    if(settle)await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
    const result=await page.evaluate(()=>{
        const rect=e=>{const b=e.getBoundingClientRect();return {x:b.x,y:b.y,right:b.right,bottom:b.bottom,width:b.width,height:b.height};};
        const list=document.querySelector('#initiativeList'),header=document.querySelector('.initiative-header');
        const rows=[...document.querySelectorAll('.initiative-row')];
        const current=document.querySelector('.initiative-row.is-current');
        return {width:innerWidth,height:innerHeight,scrollY,pageHeight:document.documentElement.scrollHeight,
            pageWidth:document.documentElement.scrollWidth,list:rect(list),header:rect(header),
            listWidth:list.clientWidth,listScrollWidth:list.scrollWidth,
            current:current&&rect(current),currentColor:current&&getComputedStyle(current).borderColor,
            normalColor:rows.find(r=>!r.classList.contains('is-current'))&&getComputedStyle(rows.find(r=>!r.classList.contains('is-current'))).borderColor,
            rows:rows.map(row=>({box:rect(row),name:rect(row.querySelector('.combatant-name-text')),
                font:parseFloat(getComputedStyle(row.querySelector('.combatant-name-text')).fontSize),
                text:row.querySelector('.combatant-name-text').textContent.trim(),
                textLines:(()=>{const range=document.createRange();range.selectNodeContents(row.querySelector('.combatant-name-text'));return [...range.getClientRects()].map(r=>({x:r.x,right:r.right,y:r.y,bottom:r.bottom}));})(),
                textWidth:row.querySelector('.combatant-name-text').scrollWidth,
                nameWidth:row.querySelector('.combatant-name-text').clientWidth,
                initiative:rect(row.querySelector('.combatant-initiative')),
                hp:row.querySelector('.combatant-hp')&&rect(row.querySelector('.combatant-hp')),
                heading:rect(row.querySelector('.combatant-name-heading')),
                conditions:row.querySelector('.condition-badges')&&rect(row.querySelector('.condition-badges')),
                portrait:rect(row.querySelector('.tracker-display-portrait'))})),
            visible:rows.filter(r=>{const b=r.getBoundingClientRect(),l=list.getBoundingClientRect();return b.top>=l.top&&b.bottom<=l.bottom;}).length,
            headerValues:[...header.querySelectorAll('.display-status-value')].map(e=>parseFloat(getComputedStyle(e).fontSize))};
    });
    assert.equal(result.scrollY,0,'document never scrolls');
    assert.equal(result.pageHeight,result.height,'display and optional authorization navigation fit viewport');
    assert(result.pageWidth<=result.width,'no document horizontal overflow');
    assert(result.listScrollWidth<=result.listWidth+1,'no list horizontal overflow '+JSON.stringify(result));
    assert(result.header.height<=110 && result.list.height>=result.height*0.65,'header leaves room for useful order');
    if(compact)assert(result.header.height<=66,'compact header is at most 66 CSS px');
    for(const row of result.rows){
        assert(row.name.x>=row.portrait.right-1,'name does not overlap portrait');
        assert(row.name.right<=row.box.right,'name stays inside row');
        assert(row.initiative.right<=row.portrait.x,'initiative has its own column');
        if(row.hp)assert(row.hp.x>=row.heading.right-1||row.hp.y>=row.heading.bottom,'HP remains separate');
        if(row.conditions)assert(row.conditions.y>=row.heading.bottom-1,'conditions stay below name and exhaustion');
        assert(row.font>=(compact?23.99:21),'names retain readable type size '+JSON.stringify({compact,row}));
        if(compact)for(const line of row.textLines){
            assert(line.x>=row.heading.x-1&&line.right<=row.heading.right+1,'every name line stays inside its heading');
            assert(line.y>=row.box.y+2&&line.bottom<=row.box.bottom-2,'name glyphs stay inside row padding');
            if(row.conditions)assert(line.bottom<=row.conditions.y+1,'condition badges do not overlap name glyphs');
        }
    }
    if(result.current){
        assert(result.current.y>=result.list.y-1&&result.current.bottom<=result.list.bottom+1,'active row visible after automatic list scroll');
        if(result.normalColor)assert.notEqual(result.currentColor,result.normalColor,'active border distinct from other rows');
    }
    return result;
}
const states=count=>({isStarted:count>0,round:12,currentTurnNumber:Math.min(2,count),activeCombatantCount:count,
    combatants:Array.from({length:count},(_,i)=>({id:String(i),name:i===2?'Alexandria the Unreasonably Long Named Guardian of the Northern Realms':i===3?'W'.repeat(72):`Hero ${i+1}`,initiative:23-i,isActive:true,isCurrent:i===Math.min(1,count-1),
        portraitUrl:'/images/default-character.svg',currentHp:i%3===0?null:18,maxHp:i%3===0?null:25,temporaryHp:i%3===0?null:4,exhaustionLevel:i===2?3:0,
        conditions:i===2?[{name:'Poisoned',styleKey:'poisoned'},{name:'Prone',styleKey:'prone'},{name:'Restrained',styleKey:'restrained'}]:[]}))});
for(const [width,height]of sizes)test(`satellite layout ${width}x${height}: empty, short, long, badges and updates`,async()=>{
    const page=await browser.newPage({viewport:{width,height},reducedMotion:'reduce',hasTouch:true});
    try{
        await page.route('http://satellite.test/**',async route=>{
            const url=new URL(route.request().url());
            if(url.pathname==='/')return route.fulfill({contentType:'text/html',body:`<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div class="app-shell"><main class="app-main"><div class="initiative-display"><header class="initiative-header"><div id="roundDisplay" class="initiative-header-status"></div><div class="initiative-title">Initiative</div><div class="initiative-header-actions"><div id="turnDisplay" class="initiative-header-status initiative-header-status--end"></div><button class="display-theme-button" aria-label="Fullscreen">⛶</button><button class="display-theme-button" aria-label="Appearance">◐</button></div></header><main id="initiativeList" class="initiative-list" tabindex="0"></main><div id="displayConnectionStatus" class="display-connection-status"><span id="displayConnectionText">Connecting</span></div></div></main></div>`});
            if(url.pathname==='/images/default-character.svg')return route.fulfill({contentType:'image/svg+xml',body:fs.readFileSync(path.join(assets,url.pathname))});
            return route.abort();
        });
        await page.goto('http://satellite.test/');
        for(const file of ['lib/bootstrap/dist/css/bootstrap.min.css','css/site.css','css/branding.css','css/condition-badges.css','css/initiative-display.css'])await page.addStyleTag({content:fs.readFileSync(path.join(assets,file),'utf8').replace(/^\uFEFF/,'')});
        await page.evaluate(()=>{window.events={};window.signalR={HubConnectionState:{Disconnected:'Disconnected'},HubConnectionBuilder:class{withUrl(){return this;}withAutomaticReconnect(){return this;}build(){return{state:'Disconnected',start:async()=>{},on:(n,c)=>window.events[n]=c,onclose(){},onreconnecting(){},onreconnected(){},invoke:async n=>n==='GetDisplayTheme'?'dark':{combatants:[],isStarted:false}};}}};});
        await page.addScriptTag({content:fs.readFileSync(path.join(assets,'js/initiative-display.js'),'utf8')});
        await page.evaluate(()=>document.dispatchEvent(new Event('DOMContentLoaded')));
        const compact=width<768||(width<=1100&&height<=650);
        for(const count of [0,3,8,24]){
            await page.evaluate(state=>window.events.EncounterUpdated(state),states(count));
            const metrics=await measurements(page,compact);
            if(count===8){
                await page.locator('img').evaluateAll(images=>Promise.all(images.map(image=>image.decode())));
                await screenshot(page,`fixture-${width}x${height}-long-names`);
                if(output)fs.writeFileSync(path.join(output,`metrics-${width}x${height}.json`),JSON.stringify(metrics,null,2));
            }
            if(count===24){
                const updated=states(count);updated.currentTurnNumber=21;updated.combatants.forEach((c,i)=>c.isCurrent=i===20);
                await page.evaluate(state=>window.events.EncounterUpdated(state),updated);
                await measurements(page,compact);
                assert.equal(await page.locator('[aria-current=true]').count(),1);
                await screenshot(page,`fixture-${width}x${height}-active-21`);
                await page.locator('#initiativeList').evaluate(el=>el.scrollTo({top:0,behavior:'instant'}));
                assert.equal(await page.locator('.initiative-header').evaluate(el=>el.getBoundingClientRect().top),0);
            }
        }
        for(const active of [2,3]){
            const state=states(8);state.currentTurnNumber=active+1;state.combatants.forEach((c,i)=>c.isCurrent=i===active);
            await page.evaluate(state=>window.events.EncounterUpdated(state),state);await measurements(page,compact);
        }
        // Shared HTTPS layouts prepend this navigation; it must consume bounded space.
        await page.evaluate(()=>{const nav=document.createElement('nav');nav.className='container py-2 small';nav.setAttribute('aria-label','Private feature authorization');nav.innerHTML='<a href="/secure-access">Secure authorization for private images and character changes</a>';document.querySelector('.app-main').prepend(nav);});
        await page.evaluate(state=>window.events.EncounterUpdated(state),states(8));
        await measurements(page,compact);
        assert(await page.locator('.initiative-header').evaluate(el=>el.getBoundingClientRect().top>0));
        // Theme tokens must keep primary text legible on every supported surface.
        for(const theme of ['dark','light','parchment','arcane','infernal','frost','forest','blood-moon']){
            await page.evaluate(theme=>window.events.DisplayThemeUpdated(theme),theme);
            const colors=await page.evaluate(()=>{const s=getComputedStyle(document.documentElement);return ['--display-text','--display-surface-strong'].map(k=>s.getPropertyValue(k).trim());});
            const luminance=hex=>{const c=hex.match(/\w\w/g).map(x=>parseInt(x,16)/255).map(x=>x<=.04045?x/12.92:((x+.055)/1.055)**2.4);return c[0]*.2126+c[1]*.7152+c[2]*.0722;};
            const l=colors.map(luminance).sort((a,b)=>b-a);assert((l[0]+.05)/(l[1]+.05)>=4.5,theme+' primary contrast');
        }
    }finally{await page.close();}
});

test('actual paired satellite: initial Razor, privacy, SignalR turns, touch appearance and revocation',{skip:!base},async()=>{
    assert(['127.0.0.1','localhost','[::1]'].includes(new URL(base).hostname),'disposable loopback app only');
    const dm=await browser.newPage();const context=await browser.newContext({viewport:{width:800,height:480},reducedMotion:'reduce',hasTouch:true});const display=await context.newPage();
    try{
        await dm.goto(base+'/setup');
        const pin=await dm.locator('.setup-grid section').nth(2).locator('strong').innerText();
        await dm.goto(base+'/dm/login');await dm.locator('[name=Pin]').fill(pin);await dm.getByRole('button',{name:'Continue',exact:true}).click();await dm.waitForURL('**/dm');
        const post=async(handler,data={})=>{
            const html=await (await dm.request.get(base+'/dm')).text();
            const token=html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];
            const response=await dm.request.post(base+'/dm?handler='+handler,{form:{__RequestVerificationToken:token,...data},maxRedirects:0});
            assert.equal(response.status(),302,handler+' accepted');
        };
        await post('Reset');await post('SetDisplayTheme',{theme:'dark'});
        await display.goto(base+'/display/pair');const code=(await display.locator('.display-2').innerText()).trim();
        await dm.goto(base+'/dm/devices');const request=dm.locator('.list-group-item').filter({hasText:code});
        const previousDeviceIds=await dm.locator('form[action*=Forget] input[name=id]').evaluateAll(inputs=>inputs.map(input=>input.value));
        await request.getByRole('button',{name:'Approve',exact:true}).click();
        const pairedId=(await dm.locator('form[action*=Forget] input[name=id]').evaluateAll(inputs=>inputs.map(input=>input.value))).find(id=>!previousDeviceIds.includes(id));
        assert(pairedId,'newly approved display ID');await display.waitForURL('**/display');
        await display.locator('.empty-display').waitFor();await screenshot(display,'actual-800x480-empty');
        await post('AddCombatant',{CombatantName:'Hidden fixture NPC',Initiative:'-20',CurrentHp:'44',MaxHp:'55',Quantity:'1',InitiallyHidden:'true'});
        assert.equal(await display.locator('.initiative-row').count(),0,'hidden NPC stays absent');
        await post('AddCombatant',{CombatantName:'Aria Moonwhisper',Initiative:'23',CurrentHp:'18',MaxHp:'25',Quantity:'1'});
        await display.locator('.initiative-row').waitFor();const ariaId=await display.locator('.initiative-row').getAttribute('data-combatant-id');
        await post('SetTemporaryHp',{id:ariaId,temporaryHp:'4'});
        await post('ToggleCombatantCondition',{combatantId:ariaId,conditionName:'Poisoned'});
        await post('AddCombatant',{CombatantName:'Alexandria the Unreasonably Long Named Guardian of the Northern Realms',Initiative:'19',CurrentHp:'33',MaxHp:'66',Quantity:'1',InitiallyHpHidden:'true'});
        await post('AddCombatant',{CombatantName:'W'.repeat(72),Initiative:'17',CurrentHp:'9',MaxHp:'12',Quantity:'1'});
        await post('AddCombatant',{CombatantName:'Skeleton',Initiative:'12',CurrentHp:'10',MaxHp:'10',Quantity:'21'});
        await display.waitForFunction(()=>document.querySelectorAll('.initiative-row').length===24);
        assert.equal(await display.locator('.initiative-row').filter({hasText:'Alexandria'}).locator('.combatant-hp').count(),0,'hidden HP and temporary HP stay absent');
        await post('StartCombat');await display.locator('[aria-current=true]').waitFor();
        // One initial and one live rendering of the same state; scripts disabled on initial page.
        const initial=await browser.newContext({storageState:await context.storageState(),javaScriptEnabled:false,viewport:{width:800,height:480}});
        try{
            const page=await initial.newPage();await page.goto(base+'/display');
            assert.equal(await page.locator('.combatant-initiative').count(),24);
            assert.equal(await page.locator('.condition-badges').first().innerText(),'Poisoned');
            assert.equal(await page.locator('[aria-current=true]').count(),1);
            await measurements(page,true,false);await screenshot(page,'actual-800x480-initial-no-js');
        }finally{await initial.close();}
        const receipt=[];
        for(const [width,height]of sizes){
            await display.setViewportSize({width,height});
            await display.reload();await display.locator('.display-connection-status.is-connected').waitFor();
            await display.locator('img.tracker-display-portrait').evaluateAll(images=>Promise.all(images.map(image=>image.decode())));
            const metrics=await measurements(display,width<768||(width<=1100&&height<=650));
            receipt.push({width,height,visible:metrics.visible,header:metrics.header});
            await screenshot(display,`actual-${width}x${height}-order`);
            if(process.env.SATELLITE_BASELINE_DIRECTORY){
                // Optional local before/after evidence from the verified master assets.
                const before=await context.newPage();await before.setViewportSize({width,height});
                try{
                    await before.route(/\/(css|js)\/initiative-display[^/]*\.(css|js)(\?|$)/,route=>{
                        const css=new URL(route.request().url()).pathname.endsWith('.css');
                        return route.fulfill({contentType:css?'text/css':'application/javascript',body:fs.readFileSync(path.join(process.env.SATELLITE_BASELINE_DIRECTORY,css?'initiative-display.css':'initiative-display.js'))});
                    });
                    await before.goto(base+'/display');await before.locator('.display-connection-status.is-connected').waitFor({state:'attached'});
                    await before.waitForFunction(()=>document.querySelectorAll('.initiative-row').length===24);
                    await before.locator('img.tracker-display-portrait').evaluateAll(images=>Promise.all(images.map(image=>image.decode())));
                    await screenshot(before,`before-${width}x${height}-order`);
                    if(width>=1280){
                        assert.deepEqual(await before.locator('.initiative-header').boundingBox(),await display.locator('.initiative-header').boundingBox(),'desktop header unchanged');
                        assert.equal(await before.locator('.combatant-name-text').first().evaluate(el=>getComputedStyle(el).fontSize),await display.locator('.combatant-name-text').first().evaluate(el=>getComputedStyle(el).fontSize),'desktop name size unchanged');
                    }
                }finally{await before.close();}
            }
        }
        await display.setViewportSize({width:800,height:480});
        await display.locator('.initiative-row').first().locator('img').evaluate(el=>el.dataset.reuseProbe='same-node');
        await post('NextTurn');await display.waitForFunction(()=>document.querySelector('#turnDisplay').textContent.includes('2'));
        assert.equal(await display.locator('.initiative-row').first().locator('img').getAttribute('data-reuse-probe'),'same-node','live rows retain portrait element');
        for(let i=0;i<18;i++)await post('NextTurn');
        await display.waitForFunction(()=>document.querySelector('#turnDisplay').textContent.includes('20'));
        await measurements(display,true);await screenshot(display,'actual-800x480-active-20');
        const list=display.locator('#initiativeList');
        await list.evaluate(el=>el.scrollTo({top:0,behavior:'instant'}));
        const touch=await context.newCDPSession(display);
        await display.bringToFront();
        await touch.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:400,y:350}]});
        for(const y of [310,270,230,190,150]){
            await touch.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:400,y}]});
            await display.waitForTimeout(20);
        }
        await display.waitForTimeout(150);
        await touch.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        await display.waitForFunction(()=>document.querySelector('#initiativeList').scrollTop>0);
        await touch.detach();
        assert.equal(await display.evaluate(()=>scrollY),0,'touch scrolling never moves the document');
        assert.equal(await display.locator('.initiative-header').evaluate(el=>el.getBoundingClientRect().top),0);
        // The next encounter event returns the active row after manual touch browsing.
        await post('NextTurn');await display.waitForFunction(()=>document.querySelector('#turnDisplay').textContent.includes('21'));
        await post('SetDisplayTheme',{theme:'light'});await display.waitForFunction(()=>document.documentElement.dataset.displayTheme==='light');
        await measurements(display,true);await screenshot(display,'actual-800x480-light');
        await display.getByRole('button',{name:'Display appearance',exact:true}).tap();await display.locator('#displayAppearancePanel.show').waitFor();
        await display.getByRole('button',{name:'Close',exact:true}).tap();
        assert.equal(new URL(display.url()).pathname,'/display','pairing survives gameplay/theme changes');
        if(output)fs.writeFileSync(path.join(output,'actual-receipt.json'),JSON.stringify(receipt,null,2));
        await dm.goto(base+'/dm/devices');
        dm.once('dialog',dialog=>dialog.accept());
        await dm.locator('form[action*=Forget]').filter({has:dm.locator('input[name=id][value="'+pairedId+'"]')}).getByRole('button',{name:'Forget',exact:true}).click();
        await display.waitForURL('**/display/pair');
    }finally{await dm.close();await context.close();}
});
