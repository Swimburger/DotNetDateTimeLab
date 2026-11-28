import { expect, test } from "@playwright/test";

async function openLab(page) {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: ".NET Date and Time Tester", exact: true })).toBeVisible({ timeout: 60_000 });
  await expect(page.getByRole("button", { name: "Analyze with .NET" })).toBeVisible();
  await expect(page.locator("details.options")).not.toHaveAttribute("open", "");
  await expect(page.getByRole("heading", { name: ".NET Date and Time Tester" })).toHaveCSS("outline-style", "none");
}

test("explains the default ambiguous DST preset with both possible instants", async ({ page }) => {
  await openLab(page);
  await expect(page.getByRole("heading", { name: "This wall time occurs twice" })).toBeVisible();
  const candidates = page.getByRole("table", { name: "Possible UTC instants for this wall time" });
  await expect(candidates.getByRole("row")).toHaveCount(3);
  await expect(candidates).toContainText("2026-11-01T05:30:00.0000000Z");
  await expect(candidates).toContainText("2026-11-01T06:30:00.0000000Z");
  await page.getByText("C# for this result").click();
  await expect(page.getByRole("button", { name: "Copy C#" })).toBeVisible();
});

test("switches between invalid, offset, and date-only semantics", async ({ page }) => {
  await openLab(page);
  const preset = page.getByLabel("Try an example");

  await preset.selectOption("dst-gap");
  await expect(page.getByRole("heading", { name: "This wall time does not exist" })).toBeVisible();
  await expect(page.getByRole("status")).toContainText("invalid");

  await preset.selectOption("offset");
  await expect(page.getByRole("status")).toContainText("one unambiguous instant");
  await expect(page.getByRole("rowheader", { name: "Unix milliseconds", exact: true })).toBeVisible();

  await preset.selectOption("date-only");
  await expect(page.getByRole("status")).toContainText("No time, offset, time zone, or instant");
  await expect(page.getByLabel("Source time zone")).toBeDisabled();
});

test("validates only after analysis and clears the stale result while editing", async ({ page }) => {
  await openLab(page);
  const input = page.getByLabel("Date or time value");
  await input.fill("not a date");
  await expect(input).toHaveAttribute("aria-invalid", "false");
  await expect(page.getByText("Ready to inspect.")).toBeVisible();

  await page.getByRole("button", { name: "Analyze with .NET" }).click();
  await expect(input).toHaveAttribute("aria-invalid", "true");
  await expect(page.getByRole("status")).toContainText("Parsing failed");
  await expect(input).toHaveAttribute("aria-describedby", /input-value-error/);
});

test("uses semantic structure and remains operable on a narrow viewport", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await openLab(page);
  await expect(page.locator("main")).toHaveCount(1);
  await expect(page.locator("table caption")).toHaveCount(3);
  await page.getByRole("button", { name: "Analyze with .NET" }).focus();
  await expect(page.getByRole("button", { name: "Analyze with .NET" })).toBeFocused();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
  await expect(page.locator(".site-footer")).toContainText("processed locally");
});

test("serves the published app with security and cache headers", async ({ request }) => {
  const root = await request.get("/");
  expect(root.status()).toBe(200);
  expect(root.headers()["cache-control"]).toContain("must-revalidate");
  expect(root.headers()["x-content-type-options"]).toBe("nosniff");
  expect(root.headers()["referrer-policy"]).toBe("strict-origin-when-cross-origin");

  const deepLink = await request.get("/reference/date-time", { headers: { Accept: "text/html" } });
  expect(deepLink.status()).toBe(200);
});

test("runs with the platform shortcut and explains why parsing succeeded", async ({ page }) => {
  await openLab(page);
  const input = page.getByLabel("Date or time value");
  await input.fill("2026-12-31 23:45");
  await input.press(process.platform === "darwin" ? "Meta+Enter" : "Control+Enter");
  await expect(page.getByRole("heading", { name: "Why parsing succeeded" })).toBeVisible();
  const explanation = page.locator(".parse-explanation");
  await expect(explanation.getByText("DateTime.ParseExact", { exact: true })).toBeVisible();
  await expect(explanation).toContainText("yyyy-MM-dd HH:mm");
});

test("copies a fragment share link and restores all settings", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await openLab(page);
  await page.getByLabel("Try an example").selectOption("ambiguous-culture-gb");
  await page.getByRole("button", { name: "Copy link" }).click();
  await expect(page.locator("#action-status")).toContainText("Copied a private-by-default share link");
  const link = await page.evaluate(() => navigator.clipboard.readText());
  expect(link).toContain("#state=");
  expect(new URL(link).search).toBe("");

  const sharedPage = await context.newPage();
  await sharedPage.goto(link);
  await expect(sharedPage.getByLabel("Date or time value")).toHaveValue("04/05/2026 09:00");
  await sharedPage.getByText("Parsing options").click();
  await expect(sharedPage.getByLabel("Culture")).toHaveValue("en-GB");
  await expect(sharedPage.locator("#action-status")).toContainText("Loaded settings from the shared link");
  await sharedPage.getByRole("button", { name: "Reset" }).click();
  await expect(sharedPage).toHaveURL(url => url.hash === "");
  await sharedPage.close();
});

test("remembers only after opt in and can forget saved work", async ({ page }) => {
  await openLab(page);
  const remember = page.getByLabel("Remember on this device");
  await remember.check();
  await page.getByLabel("Date or time value").fill("2027-01-02 03:04");
  await page.getByRole("button", { name: "Analyze with .NET" }).click();
  await page.reload();
  await expect(page.getByLabel("Date or time value")).toHaveValue("2027-01-02 03:04");
  await expect(remember).toBeChecked();

  await page.getByRole("button", { name: "Forget saved work" }).click();
  await page.reload();
  await expect(page.getByLabel("Date or time value")).toHaveValue("2026-11-01 01:30");
  await expect(remember).not.toBeChecked();
});

test("keeps the shared Swimburger shell token contract", async ({ page }) => {
  await openLab(page);
  const tokens = await page.evaluate(() => {
    const style = getComputedStyle(document.documentElement);
    return {
      darkest: style.getPropertyValue("--brand-darkest").trim(),
      dark: style.getPropertyValue("--brand-dark").trim(),
      mid: style.getPropertyValue("--brand-mid").trim(),
      accent: style.getPropertyValue("--brand-accent").trim(),
      page: style.getPropertyValue("--page").trim(),
    };
  });
  expect(tokens).toEqual({ darkest: "#4a0636", dark: "#6c0a51", mid: "#a02c56", accent: "#ffc966", page: "#17141a" });
  await expect(page.locator(".site-header")).toHaveCSS("background-image", /linear-gradient/);
});
