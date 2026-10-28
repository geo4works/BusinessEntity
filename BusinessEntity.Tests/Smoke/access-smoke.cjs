// Проверяет публичные read-only проекции после успешного storage-smoke на тех же fixtures.
const { chromium } = require(process.env.BE_PLAYWRIGHT_MODULE || "playwright");
const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
async function run() {
  const fixtures = JSON.parse(fs.readFileSync(process.env.BE_SMOKE_RESULT_FILE, "utf8"));
  const base = process.env.BE_SMOKE_URL || "http://localhost:7000";
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } }); page.setDefaultTimeout(30000);
  const admin = await browser.newPage({ viewport: { width: 1600, height: 1100 } }); admin.setDefaultTimeout(30000);
  const guestGroup = "Smoke guests " + Date.now();
  try {
    // Настройка тестовых fixtures выполняется через тот же интерфейс, что доступен администратору.
    await admin.goto(base + "/login", { waitUntil: "networkidle" });
    await admin.locator("#auth-login").fill("admin"); await admin.locator("#auth-password").fill("admin");
    await Promise.all([admin.waitForNavigation({ waitUntil: "networkidle" }), admin.getByRole("button", { name: "Логин", exact: true }).click()]);
    await admin.goto(base + "/space-selection", { waitUntil: "networkidle" });
    await admin.locator("li.list-group-item").filter({ hasText: "Документы" }).getByRole("button", { name: "Выбрать", exact: true }).click(); await admin.waitForURL(base + "/");
    for (const url of [fixtures.documents.document, fixtures.documents.article]) {
      await admin.goto(url, { waitUntil: "networkidle" });
      await admin.getByLabel("Общий", { exact: true }).check();
      await admin.reload({ waitUntil: "networkidle" });
      assert(await admin.getByLabel("Общий", { exact: true }).isChecked());
    }
    await admin.goto(base + "/administration", { waitUntil: "networkidle" });
    await admin.getByRole("button", { name: "Пользователи", exact: true }).click();
    await admin.getByRole("tab", { name: "Группы", exact: true }).click();
    await admin.getByRole("button", { name: "Создать группу", exact: true }).click();
    await admin.getByLabel("Имя группы", { exact: true }).fill(guestGroup);
    await admin.getByRole("button", { name: "Сохранить группу", exact: true }).click();
    await admin.getByRole("button", { name: guestGroup, exact: true }).waitFor();
    await admin.getByRole("tab", { name: "Состав групп", exact: true }).click();
    await admin.locator(".users-admin__membership-selector select").selectOption({ label: guestGroup + " (0)" });
    await admin.locator(".users-admin__membership-table tbody tr").filter({ hasText: "anonymous" }).getByRole("checkbox").check();
    await admin.getByRole("button", { name: "Сохранить состав", exact: true }).click();
    await admin.getByRole("tab", { name: "Права доступа", exact: true }).click();
    await admin.locator(".users-admin__access-space select").selectOption({ label: "Документы" });
    await admin.locator(".users-admin__access-picker").filter({ hasText: "Группа" }).locator("select").selectOption({ label: guestGroup });
    await admin.locator(".users-admin__access-picker").filter({ hasText: "Роль" }).locator("select").selectOption({ label: "Гость" });
    await admin.locator(".users-admin__access").getByRole("button", { name: "Добавить", exact: true }).click();
    await admin.locator(".users-admin__access-table tbody tr").filter({ hasText: guestGroup }).waitFor();
    await page.goto(base + "/space-selection", { waitUntil: "networkidle" });
    // При одном доступном пространстве штатный выбор происходит автоматически.
    if (new URL(page.url()).pathname === "/space-selection")
      await page.locator("li.list-group-item").filter({ hasText: "Документы" }).getByRole("button", { name: "Перейти", exact: true }).click();
    await page.waitForURL(/\/(?:rich-)?document\/[a-f0-9-]+/);
    await page.goto(fixtures.documents.document, { waitUntil: "networkidle" });
    await page.locator(".document-layout__content").getByText(/^Проверка сохранения документа /).waitFor();
    assert.equal(await page.getByRole("button", { name: "Edit", exact: true }).count(), 0);
    assert.equal(await page.locator(".entity-version-history").count(), 0);
    console.log(JSON.stringify({ passed: "anonymous-common-document-latest-read-only" }));
    await page.goto(fixtures.documents.article, { waitUntil: "networkidle" });
    await page.getByText(/^Проверка сохранения статьи /).first().waitFor();
    assert.equal(await page.getByText(/^Imported archive content /).count(), 0);
    assert(await page.getByRole("button", { name: "Edit", exact: true }).isDisabled());
    assert(await page.getByRole("button", { name: "Опубликовать", exact: true }).isDisabled());
    assert.equal(await page.getByRole("button", { name: "Восстановить версию", exact: true }).count(), 0);
    console.log(JSON.stringify({ passed: "anonymous-article-published-version-only" }));
    const id = fixtures.documents.article.split("/").pop();
    await page.goto(base + "/print/" + id, { waitUntil: "networkidle" });
    await page.getByText(/^Проверка сохранения статьи /).first().waitFor();
    assert.equal(await page.getByText(/^Imported archive content /).count(), 0);
    console.log(JSON.stringify({ passed: "anonymous-print-published-version-only" }));
  } catch (error) {
    if (process.env.BE_SMOKE_OUTPUT) {
      fs.mkdirSync(process.env.BE_SMOKE_OUTPUT, { recursive: true });
      await page.screenshot({ path: path.join(process.env.BE_SMOKE_OUTPUT, "access-failure.png"), fullPage: true });
    }
    console.error(JSON.stringify({ failedAt: page.url(), error: error.message })); process.exitCode = 1;
  } finally {
    try {
      await admin.goto(base + "/administration", { waitUntil: "networkidle" });
      await admin.getByRole("button", { name: "Пользователи", exact: true }).click();
      await admin.getByRole("tab", { name: "Группы", exact: true }).click();
      const group = admin.getByRole("button", { name: guestGroup, exact: true });
      if (await group.count()) {
        await group.click(); await admin.getByRole("button", { name: "Удалить группу", exact: true }).click();
        await admin.getByRole("button", { name: "Подтвердить удаление", exact: true }).click();
        await group.waitFor({ state: "detached" });
      }
    } catch (error) { console.error("Guest fixture cleanup: " + error.message); process.exitCode = 1; }
    await browser.close();
  }
}
run().catch(error => { console.error(error.message); process.exitCode = 1; });
