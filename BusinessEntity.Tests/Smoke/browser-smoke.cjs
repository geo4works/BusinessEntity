// Browser smoke: запускать с установленным playwright и браузером Edge.
const { chromium } = require(process.env.BE_PLAYWRIGHT_MODULE || "playwright");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

// Проверяет вход, дерево и запись обоих видов документа через интерфейс.
async function run() {
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.setDefaultTimeout(30000);
  const base = process.env.BE_SMOKE_URL || "http://localhost:7000";
  const label = process.env.BE_SMOKE_LABEL || "stage0";
  const suffix = Date.now();
  const result = { label, steps: [], documents: {} };
  // Добавляет подтверждённый шаг без вывода токенов и пароля.
  const passed = name => { result.steps.push(name); console.log(JSON.stringify({ passed: name })); };
  try {
    const response = await page.goto(base + "/login", { waitUntil: "networkidle" });
    assert.equal(response.status(), 200);
    await page.locator("#auth-login").fill(process.env.BE_SMOKE_USER || "admin");
    await page.locator("#auth-password").fill(process.env.BE_SMOKE_PASSWORD || "admin");
    await Promise.all([
      page.waitForNavigation({ waitUntil: "networkidle" }),
      page.getByRole("button", { name: "Логин", exact: true }).click()
    ]);
    assert(!page.url().includes("loginError"));
    passed("login");
    await page.goto(base + "/space-selection", { waitUntil: "networkidle" });
    const space = page.locator("li.list-group-item").filter({ hasText: "Документы" });
    await space.getByRole("button", { name: "Выбрать", exact: true }).click();
    await page.waitForURL(base + "/");
    const tree = page.locator("#main-tree-container");
    await tree.getByText("Документы", { exact: true }).waitFor();
    passed("space-and-tree");

    const before = await tree.locator(".tree-node-title").allTextContents();
    await tree.getByText("Документы", { exact: true }).click({ button: "right" });
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

    await tree.getByText("Документы", { exact: true }).click({ button: "right" });
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

    await page.goto(base + "/administration", { waitUntil: "networkidle" });
    await page.getByRole("heading", { name: "Администрирование", exact: true }).waitFor();
    await page.getByRole("button", { name: "Пользователи", exact: true }).click();
    await page.locator(".users-admin__list button").first().waitFor();
    passed("administration-users");
    const loggerResponse = await page.request.get("http://localhost:5080");
    assert.equal(loggerResponse.status(), 200);
    passed("web-logger-http");
    console.log(JSON.stringify({ result }));
  } catch (error) {
    const output = process.env.BE_SMOKE_OUTPUT;
    if (output) {
      fs.mkdirSync(output, { recursive: true });
      await page.screenshot({ path: path.join(output, label + "-failure.png"), fullPage: true });
    }
    console.error(JSON.stringify({ label, failedAt: page.url(), completed: result.steps, error: error.message }));
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}
run().catch(error => { console.error(error.message); process.exitCode = 1; });
