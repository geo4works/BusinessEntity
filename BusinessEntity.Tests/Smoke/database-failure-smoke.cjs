// Запускать только против отдельного тестового экземпляра и его PostgreSQL в tmpfs.
const { chromium } = require(process.env.BE_PLAYWRIGHT_MODULE || "playwright");
const { execFileSync } = require("node:child_process");
const assert = require("node:assert/strict");
const database = "be-arch-test-db";

// Отключает только тестовую БД после загрузки редактора, затем проверяет явный отказ и черновик.
async function run() {
  const base = process.env.BE_FAULT_URL;
  if (base !== "http://localhost:7001") throw new Error("Разрешён только изолированный тестовый порт 7001");
  const id = execFileSync("docker.exe", ["exec", database, "psql", "-U", "adm01", "-d", "be_stage1", "-tAc",
    'SELECT "Id" FROM "BusinessEntities" WHERE "EntityType" = 300 LIMIT 1'], { encoding: "utf8" }).trim();
  assert.match(id, /^[a-f0-9-]{36}$/);
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const page = await browser.newPage();
  page.setDefaultTimeout(30000);
  let databasePaused = false;
  try {
    await page.goto(base + "/login", { waitUntil: "networkidle" });
    await page.locator("#auth-login").fill("admin"); await page.locator("#auth-password").fill("admin");
    await Promise.all([page.waitForNavigation({ waitUntil: "networkidle" }),
      page.getByRole("button", { name: "Логин", exact: true }).click()]);
    assert(!page.url().includes("loginError"));
    await page.goto(base + "/space-selection", { waitUntil: "networkidle" });
    await page.locator("li.list-group-item").filter({ hasText: "Документы" })
      .getByRole("button", { name: "Выбрать", exact: true }).click();
    await page.waitForURL(base + "/");
    await page.goto(base + "/document/" + id, { waitUntil: "networkidle" });
    await page.getByRole("button", { name: "Edit", exact: true }).click();
    const draft = "Несохранённый черновик при недоступной БД " + Date.now();
    await page.getByPlaceholder("Текст документа", { exact: true }).fill(draft);
    execFileSync("docker.exe", ["pause", database], { stdio: "pipe" });
    databasePaused = true;
    await page.getByRole("button", { name: "Сохранить", exact: true }).click();
    await page.locator(".document-save-error").getByText("Хранилище недоступно. Повторите операцию позже.", { exact: true }).waitFor();
    assert.equal(await page.getByPlaceholder("Текст документа", { exact: true }).inputValue(), draft);
    assert.equal(await page.locator("#blazor-error-ui").isVisible(), false);
    console.log(JSON.stringify({ passed: "database-unavailable-explicit-error-draft-retained", database }));
  } finally {
    await browser.close();
    // PostgreSQL теста восстанавливается независимо от результата UI-проверки.
    if (databasePaused) execFileSync("docker.exe", ["unpause", database], { stdio: "pipe" });
  }
}
run().catch(error => { console.error(error.message); process.exitCode = 1; });
