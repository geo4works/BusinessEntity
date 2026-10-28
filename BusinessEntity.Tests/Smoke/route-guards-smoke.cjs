// Проверяет прямые URL, браузерную историю и серверный запрет администрирования для обычной учётной записи.
const { chromium } = require(process.env.BE_PLAYWRIGHT_MODULE || "playwright");
const fs = require("node:fs"); const path = require("node:path"); const assert = require("node:assert/strict");
async function run() {
  const base = process.env.BE_SMOKE_URL || "http://localhost:7000";
  const browser = await chromium.launch({channel:"msedge",headless:true});
  const admin = await browser.newPage({viewport:{width:1800,height:1100}}); admin.setDefaultTimeout(30000);
  let createdName, createdExternalId; const name = "smoke-guard-" + Date.now(); const password = "Be-smoke-guard-" + Date.now();
  let userPage;
  async function login(page, user, pass) {
    await page.goto(base+"/login",{waitUntil:"networkidle"});
    await page.locator("#auth-login").fill(user); await page.locator("#auth-password").fill(pass);
    await Promise.all([page.waitForNavigation({waitUntil:"networkidle"}),page.getByRole("button",{name:"Логин",exact:true}).click()]);
  }
  async function selectSpace(page) {
    await page.goto(base+"/space-selection",{waitUntil:"networkidle"});
    await page.locator("li.list-group-item").filter({hasText:"Документы"}).getByRole("button",{name:"Выбрать",exact:true}).click();
    await page.waitForURL(base+"/");
  }
  try {
    await login(admin,"admin","admin"); await selectSpace(admin);
    await admin.goto(base+"/administration",{waitUntil:"networkidle"});
    await admin.getByRole("link",{name:"Пользователи",exact:true}).click(); await admin.waitForURL(base+"/administration/users");
    await admin.getByRole("link",{name:"Роли",exact:true}).click(); await admin.waitForURL(base+"/administration/users/roles");
    await admin.reload({waitUntil:"networkidle"}); await admin.locator(".users-admin__roles-table").waitFor();
    await admin.goBack({waitUntil:"networkidle"}); await admin.waitForURL(base+"/administration/users");
    await admin.locator(".users-admin__list").waitFor();
    console.log(JSON.stringify({passed:"admin-section-url-reload-and-history"}));
    await admin.locator(".users-admin__sidebar-actions").getByRole("button",{name:"Создать",exact:true}).click();
    await admin.getByText(/^Пользователь .+ создан в Authentik\.$/).waitFor();
    createdName = await admin.getByLabel("Логин в аутентик",{exact:true}).inputValue();
    createdExternalId = await admin.getByLabel("Код в аутентик",{exact:true}).inputValue();
    await admin.getByLabel("Логин в аутентик",{exact:true}).fill(name);
    await admin.getByLabel("Отображаемое имя",{exact:true}).fill(name);
    await admin.getByLabel("Новый пароль",{exact:true}).fill(password);
    await admin.getByLabel("Новый пароль",{exact:true}).press("Tab");
    await admin.locator(".users-admin__form-actions").getByRole("button",{name:"Сохранить",exact:true}).click();
    await admin.getByText("Пользователь сохранен. Пароль изменен.",{exact:true}).waitFor();
    createdName = name;
    userPage = await browser.newPage({viewport:{width:1600,height:1000}}); userPage.setDefaultTimeout(30000);
    await login(userPage,name,password); await selectSpace(userPage);
    assert.equal(await userPage.locator("nav.flex-column").getByRole("link",{name:"Администрирование",exact:true}).count(),0);
    for (const route of ["/administration","/administration/users","/administration/system","/administration/spaces","/diagnostics","/logging"]) {
      await userPage.goto(base+route,{waitUntil:"networkidle"});
      await userPage.getByText("Недостаточно прав для операции.",{exact:true}).first().waitFor();
      assert.equal(await userPage.locator(".users-admin,.users-admin__roles-table").count(),0);
    }
    console.log(JSON.stringify({passed:"ordinary-user-direct-admin-routes-denied"}));
    for(const route of ["/test-change-parent","/test-remove-relation"]){
      await admin.goto(base+route,{waitUntil:"networkidle"});
      assert.equal(await admin.getByRole("button",{name:/Test|Тест/}).count(),0);
    }
    await admin.goto(base+"/",{waitUntil:"networkidle"});
    assert.equal(await admin.getByText("Дата и время",{exact:true}).count(),0);
    assert.equal(await admin.locator(".right-panel [role=tablist]").count(),0);
    console.log(JSON.stringify({passed:"clock-and-test-pages-removed-right-panel-sections"}));
  } catch(error) {
    if(process.env.BE_SMOKE_OUTPUT){fs.mkdirSync(process.env.BE_SMOKE_OUTPUT,{recursive:true});await admin.screenshot({path:path.join(process.env.BE_SMOKE_OUTPUT,"route-guards-admin-failure.png"),fullPage:true});if(userPage)await userPage.screenshot({path:path.join(process.env.BE_SMOKE_OUTPUT,"route-guards-user-failure.png"),fullPage:true});}
    console.error(JSON.stringify({failedAt:admin.url(),error:error.message}));process.exitCode=1;
  } finally {
    try {
      if(createdName){
        // Завершаем собственную тестовую сессию до удаления её учётной записи.
        if(userPage && !userPage.isClosed()) await userPage.close();
        await admin.goto(base+"/administration/users",{waitUntil:"networkidle"});await admin.locator(".users-admin__list").waitFor();
        const row=createdExternalId ? admin.locator(".users-admin__list button").filter({has:admin.locator(".users-admin__user-code",{hasText:createdExternalId})}) : admin.locator(".users-admin__list button").filter({hasText:createdName}); await row.click();
        if(createdExternalId) await admin.waitForFunction(value=>Array.from(document.querySelectorAll(".users-admin__form label"))
          .find(label=>label.textContent.includes("Код в аутентик"))?.querySelector("input")?.value===value,createdExternalId);
        await admin.locator(".users-admin__form-actions").getByRole("button",{name:"Удалить",exact:true}).click();
        await admin.getByText("Подтвердите удаление пользователя.",{exact:true}).first().waitFor();
        await admin.locator(".users-admin__form-actions").getByRole("button",{name:"Подтвердить удаление",exact:true}).click();await row.waitFor({state:"detached"});
      }
    } catch(error){console.error("User fixture cleanup: "+error.message);process.exitCode=1;}
    await browser.close();
  }
}
run().catch(e=>{console.error(e.message);process.exitCode=1});
