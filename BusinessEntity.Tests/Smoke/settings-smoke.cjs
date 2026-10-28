// Проверяет изменения unversioned форм через общий Save, затем возвращает настройки и удаляет только своё видео.
const { chromium } = require(process.env.BE_PLAYWRIGHT_MODULE || "playwright");
const assert = require("node:assert/strict");
async function run() {
  const base = process.env.BE_SMOKE_URL || "http://localhost:7000";
  const browser = await chromium.launch({channel:"msedge",headless:true});
  const page = await browser.newPage({viewport:{width:1600,height:1100}}); page.setDefaultTimeout(30000);
  let originalCompany, videoId, videoFileName; const company = "Stage6 company " + Date.now();
  try {
    await page.goto(base+"/login",{waitUntil:"networkidle"});
    await page.locator("#auth-login").fill("admin"); await page.locator("#auth-password").fill("admin");
    await Promise.all([page.waitForNavigation({waitUntil:"networkidle"}),page.getByRole("button",{name:"Логин",exact:true}).click()]);
    await page.goto(base+"/space-selection",{waitUntil:"networkidle"});
    await page.locator("li.list-group-item").filter({hasText:"Документы"}).getByRole("button",{name:"Выбрать",exact:true}).click(); await page.waitForURL(base+"/");
    await page.goto(base+"/administration/system",{waitUntil:"networkidle"});
    const input=page.getByPlaceholder("Введите название компании",{exact:true}); originalCompany=await input.inputValue();
    await input.fill(company); await page.getByRole("button",{name:"Сохранить",exact:true}).click();
    await page.getByText("Параметры системы сохранены.",{exact:true}).waitFor(); await page.locator(".app-header__company").filter({hasText:company}).waitFor();
    await page.reload({waitUntil:"networkidle"}); assert.equal(await input.inputValue(),company);
    console.log(JSON.stringify({passed:"system-settings-save-reload-header-refresh"}));
    const bytes=Buffer.from("00000018667479706d703432000000006d70343269736f6d","hex");
    const response=await page.request.post(base+"/media-server-files/videos",{multipart:{file:{name:"settings-smoke-"+Date.now()+".mp4",mimeType:"video/mp4",buffer:bytes}}});
    assert.equal(response.status(),200); const video=await response.json(); videoId=video.id; videoFileName=video.fileName;
    await page.goto(base+"/multimedia",{waitUntil:"networkidle"});
    // Строка определяется уникальным именем собственного загруженного файла.
    const item=page.locator(".multimedia-video-item").filter({hasText:video.fileName});
    await item.locator(".multimedia-video-row__icon").click();
    const comment="Metadata comment " + Date.now(); await item.locator("textarea").fill(comment);
    await item.getByRole("button",{name:"Сохранить",exact:true}).click(); await page.getByText("Видео сохранено.",{exact:true}).waitFor();
    await page.reload({waitUntil:"networkidle"}); await item.locator(".multimedia-video-row__icon").click();
    assert.equal(await item.locator("textarea").inputValue(),comment);
    console.log(JSON.stringify({passed:"media-comment-only-save-reload"}));
  } catch(error){console.error(JSON.stringify({failedAt:page.url(),error:error.message}));process.exitCode=1;}
  finally {
    try {
      if(originalCompany!==undefined){await page.goto(base+"/administration/system",{waitUntil:"networkidle"});await page.getByPlaceholder("Введите название компании",{exact:true}).fill(originalCompany);await page.getByRole("button",{name:"Сохранить",exact:true}).click();await page.getByText("Параметры системы сохранены.",{exact:true}).waitFor();}
      if(videoId){
        await page.goto(base+"/multimedia",{waitUntil:"networkidle"});
        const own=page.locator('.multimedia-video-item').filter({hasText:videoFileName});
        await own.locator('.multimedia-video-row__icon').click();
        await own.getByRole('button',{name:'Удалить',exact:true}).click(); await own.waitFor({state:'detached'});
      }
    }catch(error){console.error("Settings fixture cleanup: "+error.message);process.exitCode=1;}
    await browser.close();
  }
}
run().catch(error=>{console.error(error.message);process.exitCode=1});
