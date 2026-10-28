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
  let createdGroupName;
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
    await admin.getByRole("link", { name: "Пользователи", exact: true }).click();
    await admin.getByRole("link", { name: "Группы", exact: true }).click();
    await admin.getByRole("button", { name: "Создать группу", exact: true }).click();
    await admin.getByText(/^Группа .+ создана\.$/).waitFor();
    const groupNameInput = admin.getByLabel("Имя группы", { exact: true });
    createdGroupName = await groupNameInput.inputValue();
    await groupNameInput.fill(guestGroup); await groupNameInput.press("Tab");
    await admin.getByRole("button", { name: "Сохранить группу", exact: true }).click();
    await admin.getByText("Группа сохранена.", { exact: true }).waitFor();
    await admin.getByRole("button", { name: guestGroup, exact: true }).waitFor();
    await admin.getByRole("link", { name: "Состав групп", exact: true }).click();
    const [setupGroupId] = await admin.locator(".users-admin__membership-selector select").selectOption({ label: guestGroup + " (0)" });
    await admin.locator(`.users-admin__membership-table[data-group-id="${setupGroupId}"]`).waitFor();
    await admin.locator(".users-admin__membership-table tbody tr").filter({ hasText: "anonymous" }).getByRole("checkbox").check();
    await admin.getByRole("button", { name: "Сохранить состав", exact: true }).click();
    await admin.getByText("Состав группы сохранен.", { exact: true }).waitFor();
    await admin.locator(".users-admin__membership-selector select option").filter({ hasText: guestGroup + " (1)" }).waitFor({ state: "attached" });
    await admin.getByRole("link", { name: "Права доступа", exact: true }).click();
    await admin.locator(".users-admin__access-space select").selectOption({ label: "Документы" });
    await admin.locator(".users-admin__access-picker").filter({ hasText: "Группа" }).locator("select").selectOption({ label: guestGroup });
    await admin.locator(".users-admin__access-picker").filter({ hasText: "Роль" }).locator("select").selectOption({ label: "Гость" });
    await admin.locator(".users-admin__access").getByRole("button", { name: "Добавить", exact: true }).click();
    await admin.getByText("Назначение роли сохранено.", { exact: true }).waitFor();
    const assignment = admin.locator(".users-admin__access-table tbody tr").filter({ hasText: guestGroup });
    await assignment.waitFor();
    assert((await assignment.innerText()).includes("Документы"));
    assert((await assignment.innerText()).includes("Гость"));
    await page.goto(base + "/space-selection", { waitUntil: "networkidle" });
    // Единственное разрешённое пространство выбирается автоматически; ожидаем сам переход.
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
      await admin.screenshot({ path: path.join(process.env.BE_SMOKE_OUTPUT, "access-admin-failure.png"), fullPage: true });
    }
    console.error(JSON.stringify({ failedAt: page.url(), adminAt: admin.url(), error: error.message })); process.exitCode = 1;
  } finally {
    try {
      await admin.goto(base + "/administration", { waitUntil: "networkidle" });
      await admin.getByRole("link", { name: "Пользователи", exact: true }).click();
      await admin.getByRole("link", { name: "Группы", exact: true }).click();
      await admin.locator(".users-admin__groups-table").waitFor();
      let group = admin.getByRole("button", { name: guestGroup, exact: true });
      if (!await group.count() && createdGroupName) group = admin.getByRole("button", { name: createdGroupName, exact: true });
      if (await group.count()) {
        const cleanupName = (await group.innerText()).trim();
        await admin.getByRole("link", { name: "Состав групп", exact: true }).click();
        const membership = admin.locator(".users-admin__membership-selector select");
        await membership.waitFor();
        const groupId = await membership.locator("option").evaluateAll((options, name) =>
          options.find(option => option.textContent.startsWith(name + " ("))?.value, cleanupName);
        assert(groupId, "Тестовая группа доступна для очистки состава");
        await membership.selectOption(groupId);
        await admin.locator(`.users-admin__membership-table[data-group-id="${groupId}"]`).waitFor();
        for (const checkbox of await admin.locator(".users-admin__membership-table input[type=checkbox]").all())
          if (await checkbox.isChecked()) await checkbox.uncheck();
        await admin.getByRole("button", { name: "Сохранить состав", exact: true }).click();
        await admin.getByText("Состав группы сохранен.", { exact: true }).waitFor();
        await membership.locator("option").filter({ hasText: cleanupName + " (0)" }).waitFor({ state: "attached" });
        await admin.getByRole("link", { name: "Группы", exact: true }).click();
        await admin.locator(".users-admin__groups-table").waitFor();
        await group.click();
        // Дожидаемся именно выбранной формы, иначе Delete может относиться к прежней группе.
        await admin.waitForFunction(name => Array.from(document.querySelectorAll("label"))
          .find(label => label.textContent.trim() === "Имя группы")?.querySelector("input")?.value === name, cleanupName);
        await admin.getByRole("button", { name: "Удалить группу", exact: true }).click();
        await admin.getByRole("button", { name: "Подтвердить удаление", exact: true }).click();
        await group.waitFor({ state: "detached" });
      }
    } catch (error) { console.error("Guest fixture cleanup: " + error.message); process.exitCode = 1; }
    await browser.close();
  }
}
run().catch(error => { console.error(error.message); process.exitCode = 1; });
