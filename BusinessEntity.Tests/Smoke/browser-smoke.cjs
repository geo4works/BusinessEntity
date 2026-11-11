// Browser smoke: запускать с установленным playwright и браузером Edge.
const { chromium } = require(process.env.BE_PLAYWRIGHT_MODULE || "playwright");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
process.env.BE_SMOKE_LIBRARY="1";const setup=require("./api-crud-smoke.cjs");delete process.env.BE_SMOKE_LIBRARY;

// Проверяет вход, дерево и запись обоих видов документа через интерфейс.
async function run() {
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const mainContext = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await mainContext.newPage();
  page.setDefaultTimeout(30000);
  const base = process.env.BE_SMOKE_URL || "http://localhost:7000";
  const label = process.env.BE_SMOKE_LABEL || "oidc-regression";
  const spaceName=process.env.BE_SMOKE_SPACE;assert(spaceName,"BE_SMOKE_SPACE must identify an owned QA space");
  const suffix = Date.now();
  const result = { label, spaceName, steps: [], documents: {} };
  // Добавляет подтверждённый шаг без вывода токенов и пароля.
  const passed = name => { result.steps.push(name); console.log(JSON.stringify({ passed: name })); };
  try {
    await setup.login(page);
    passed("login");
    await page.goto(base + "/space-selection", { waitUntil: "networkidle" });
    const space = page.locator("li.list-group-item").filter({ hasText: spaceName });
    await space.getByRole("button", { name: "Выбрать", exact: true }).click();
    await page.waitForURL(base + "/");
    const tree = page.locator("#main-tree-container");
    await tree.getByText(spaceName, { exact: true }).waitFor();
    passed("space-and-tree");

    const before = await tree.locator(".tree-node-title").allTextContents();
    await tree.getByText(spaceName, { exact: true }).click({ button: "right" });
    await page.getByText("Создать документ", { exact: true }).click();
    await page.waitForFunction(previous => [...document.querySelectorAll("#main-tree-container .tree-node-title")]
      .some(x => !previous.includes(x.textContent)), before);
    const after = await tree.locator(".tree-node-title").allTextContents();
    const created = after.find(x => !before.includes(x));
    assert(created, "Созданный документ появился в дереве");
    await tree.getByText(created.trim(), { exact: true }).click();
    await page.waitForURL(/\/document\/[a-f0-9-]+/);
    result.documents.document = page.url();
    await page.getByRole("button", { name: "Edit", exact: true }).click();
    const title = "Smoke Document " + label + " " + suffix;
    const body = "Проверка сохранения документа " + suffix;
    await page.getByPlaceholder("Заголовок", { exact: true }).fill(title);
    await page.getByPlaceholder("Текст документа", { exact: true }).fill(body);
    await page.getByRole("button", { name: "Сохранить", exact: true }).click();
    await page.getByRole("button", { name: "Edit", exact: true }).waitFor();
    await page.reload({ waitUntil: "networkidle" });
    await page.getByRole("heading", { name: title, exact: true }).waitFor();
    assert((await page.locator(".document-layout__content").innerText()).includes(body));
    passed("document-create-save-reload");

    await tree.getByText(spaceName, { exact: true }).click({ button: "right" });
    await page.getByText("Создать рич-документ", { exact: true }).click();
    await page.waitForURL(/\/rich-document\/[a-f0-9-]+/);
    result.documents.article = page.url().split("?")[0];
    const richTitle = "Smoke Article " + label + " " + suffix;
    const richBody = "Проверка сохранения статьи " + suffix;
    await page.getByRole("textbox", { name: "Название rich-text документа", exact: true }).fill(richTitle);
    await page.locator('[contenteditable="true"]').first().fill(richBody);
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await page.getByText(/Сохранено чанков:/).first().waitFor();
    await page.getByRole("button", { name: "Перейти в режим чтения", exact: true }).click();
    await page.getByRole("button", { name: "Edit", exact: true }).first().waitFor();
    await page.goto(result.documents.article, { waitUntil: "networkidle" });
    await page.getByText(richTitle, { exact: true }).first().waitFor();
    await page.getByText(richBody, { exact: true }).first().waitFor();
    passed("article-create-save-reload");

    await page.goto(result.documents.document, { waitUntil: "networkidle" });
    const comment = "Smoke comment " + label + " " + suffix;
    await page.getByPlaceholder("Написать комментарий", { exact: true }).first().fill(comment);
    await page.getByRole("button", { name: "Отправить", exact: true }).first().click();
    await page.getByText(comment, { exact: true }).first().waitFor();
    await page.reload({ waitUntil: "networkidle" });
    await page.getByText(comment, { exact: true }).first().waitFor();
    passed("comment-create-reload");

    const secondWindow = await page.context().newPage();
    secondWindow.setDefaultTimeout(30000);
    await page.goto(base + "/profile", { waitUntil: "networkidle" });
    await secondWindow.goto(base + "/profile", { waitUntil: "networkidle" });
    const profileInput=p=>p.getByLabel("Отображаемое имя",{exact:true});
    const savedState=p=>p.locator('.miniapp-save-state[data-dirty="false"]').getByText("Рабочий набор сохранён.",{exact:true});
    const originalName=await profileInput(page).inputValue();
    const firstName=originalName+' QA '+suffix,secondName=originalName+' QA2 '+suffix;
    async function saveProfile(p,name){await profileInput(p).fill(name);await profileInput(p).press('Tab');await p.locator('.miniapp-save-state[data-dirty="true"]').waitFor();await p.getByRole("button",{name:"Сохранить",exact:true}).click();await savedState(p).waitFor();}
    try{
      await saveProfile(page,firstName);
      assert.equal(await secondWindow.locator('.miniapp-save-state[data-dirty="true"]').count(),0);
      assert.equal(await savedState(secondWindow).count(),0);
      assert.equal(await profileInput(secondWindow).inputValue(),originalName);
      await saveProfile(secondWindow,secondName);
      assert.equal(await profileInput(page).inputValue(),firstName);
      assert.equal(await savedState(page).count(),1);
      passed("profile-save-two-windows-state-isolated");
    }finally{
      await saveProfile(page,originalName);
      await secondWindow.close();
    }

    await page.goto(base + "/administration", { waitUntil: "networkidle" });
    await page.getByRole("heading", { name: "Администрирование", exact: true }).waitFor();
    await page.getByRole("link", { name: "Пользователи", exact: true }).click();
    await page.locator(".users-admin__list button").first().waitFor();
    passed("administration-users");
    const loggerResponse = await page.request.get("http://localhost:5080");
    assert.equal(loggerResponse.status(), 200);
    passed("web-logger-http");
    console.log(JSON.stringify({ result }));
    if (process.env.BE_SMOKE_RESULT_FILE) fs.writeFileSync(process.env.BE_SMOKE_RESULT_FILE, JSON.stringify(result));
  } catch (error) {
    const output = process.env.BE_SMOKE_OUTPUT;
    if (output) {
      fs.mkdirSync(output, { recursive: true });
      await page.screenshot({ path: path.join(output, label + "-failure.png"), fullPage: true });
    }
    console.error(JSON.stringify({ label, failedAt: page.url(), completed: result.steps, error: error.message, alerts:await page.getByRole("alert").allTextContents(),saveState:await page.locator(".miniapp-save-state").allTextContents() }));
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}
run().catch(error => { console.error(error.message); process.exitCode = 1; });
