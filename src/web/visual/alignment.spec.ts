import { expect, test, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const project = { id: "f0187842-6f73-4c54-8a8c-57ac7c117c39", key: "jobs", name: "Jobs",
  version: 1, created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null };
const push = { id: "4a572f67-2edb-46d3-86de-419608f16d83", project_key: "jobs",
  key: "backup", name: "Nightly backup", purpose: "Confirms nightly backup completion",
  mode: "job_completion", interval_seconds: 3600, tolerance_seconds: 300,
  instruction: "Inspect the backup log", runbook_url: "https://docs.example.test/runbooks/backup",
  state: "failing", last_received_at: "2026-09-19T12:00:00Z",
  next_deadline_at: "2099-09-19T13:05:00Z", latest_report_id: null, latest_success_id: null,
  open_incident_id: null, has_reporting_credential: false, version: 1,
  created_at: "2026-09-19T09:00:00Z", updated_at: "2026-09-19T12:00:00Z",
  paused_at: null, deleted_at: null };
const inventoryItem = { id: push.id, project_key: "jobs", project_name: "Jobs", key: push.key,
  name: push.name, purpose: push.purpose, type: "push", mode: push.mode, target_url: null,
  interval_seconds: push.interval_seconds, tolerance_seconds: push.tolerance_seconds,
  state: push.state, overdue: false, incident_open: false, maintenance_until: null,
  latest_observation_at: "2026-09-19T12:00:00Z", last_success_at: null,
  next_due_at: "2099-09-19T13:05:00Z" };
const http = { id: "f71f436f-fba2-40dc-af42-6b15ffb56a12", project_key: "jobs", key: "site",
  name: "Public website", purpose: "Checks the public endpoint", target_url: "https://status.example.test/health",
  has_target_query: true, expected_status_code: 200, text_condition: "required", text_fragment: "ready",
  interval_seconds: 60, timeout_seconds: 10, failure_threshold: 3,
  instruction: "Inspect the public endpoint", runbook_url: "https://docs.example.test/runbooks/site",
  state: "failing", consecutive_failures: 1, next_check_at: "2099-09-19T12:02:00Z",
  latest_result_id: null, latest_success_id: null, open_incident_id: null, headers: [], version: 1,
  created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z",
  paused_at: null, deleted_at: null };

let projectFixture: "normal" | "empty" | "failure" | "pending" = "normal";
let releaseProjectCreation: (() => void) | undefined;

async function fixtures(page: Page) {
  projectFixture = "normal";
  releaseProjectCreation = undefined;
  await page.addInitScript(() => {
    if (!localStorage.getItem("upaffe-theme")) localStorage.setItem("upaffe-theme", "light");
  });
  await page.route("http://127.0.0.1:4173/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/projects" && route.request().method() === "GET" && projectFixture === "failure") {
      await route.fulfill({ status: 503, json: { code: "unavailable", status: 503,
        title: "private diagnostic" } });
      return;
    }
    if (path === "/api/projects" && route.request().method() === "POST" && projectFixture === "pending") {
      await new Promise<void>((resolve) => { releaseProjectCreation = resolve; });
      await route.fulfill({ status: 201, json: project });
      return;
    }
    const body: unknown = path === "/api/bootstrap" ? { required: false, available: false }
      : path === "/api/session" ? { operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
        email: "operator@example.test", access_path: "browser_session",
        session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2099-09-23T12:00:00Z" }
      : path === "/api/projects" ? projectFixture === "empty" ? [] : [project]
      : path === "/api/projects/jobs" ? project
      : path === "/api/projects/jobs/report" ? { generated_at: "2026-09-19T12:00:00Z",
        project, counts: { total: 2, http: 1, push: 1, healthy: 0, failing: 2, untested: 0,
          paused: 0 }, attention: [], healthy: [], project_maintenance: null,
        email: { configured: true, host: "mail.example.test", port: 587, security: "starttls",
          sender_address: "notify@example.test", public_base_url: "https://status.example.test",
          has_password: false, recipients: ["ops@example.test"], delivery: { project_key: "jobs",
            pending_count: 0, retrying_count: 0, terminal_failure_count: 0, smtp_accepted_count: 1,
            oldest_pending_at: null } } }
      : path === "/api/monitors" ? { generated_at: "2026-09-19T12:00:00Z", items: [inventoryItem],
        total: 1, limit: 25, offset: 0, has_more: false }
      : path === "/api/overview" ? { generated_at: "2026-09-19T12:00:00Z",
        counts: { total: 2, healthy: 1, failing: 1, untested: 0, paused: 0, overdue: 0 },
        delivery: { pending: 0, overdue: 0, retrying: 0, terminal_failure: 0, accepted: 1,
          oldest_pending_at: null },
        attention: [{ project_key: "jobs", type: "push", key: "backup", name: "Nightly backup",
          purpose: "Confirms nightly backup completion", state: "failing", mode: "job_completion",
          overdue: false, next_due_at: "2099-09-19T13:05:00Z", last_success_at: null,
          latest_result_at: "2026-09-19T12:00:00Z", latest_outcome: "failure",
          latest_reason: "backup_failed", incident_id: null, incident_began_at: null,
          incident_reason: null, effective_maintenance_until: null }],
        projects: [{ key: "jobs", name: "Jobs", counts: { total: 2, healthy: 1, failing: 1,
          untested: 0, paused: 0, overdue: 0 }, delivery: { pending: 0, overdue: 0,
          retrying: 0, terminal_failure: 0, accepted: 1, oldest_pending_at: null },
          maintenance_until: null }] }
      : path === "/api/projects/jobs/push-monitors" ? [push]
      : path === "/api/projects/jobs/http-monitors" ? [http]
      : path === "/api/projects/jobs/http-monitors/site" ? http
      : path === "/api/projects/jobs/push-monitors/backup" ? push
      : path.endsWith("/checks") ? { items: [], next_before_sequence: null }
      : path.endsWith("/reports") ? { items: [], next_before_sequence: null }
      : path.endsWith("/incidents") ? { items: [], next_before_opening_sequence: null }
      : path.endsWith("/maintenance") ? { project_key: "jobs", scope_type: "push", version: 0,
        direct_active: false, effective_active: false, ends_at: null, effective_ends_at: null,
        active_scopes: [] }
      : path === "/api/projects/jobs/recipients" ? { project_key: "jobs", version: 1,
        recipients: ["ops@example.test"] }
      : path === "/api/email/settings" ? { version: 2, host: "mail.example.test", port: 587,
        security: "starttls", sender_address: "notify@example.test", sender_name: "Monitor",
        public_base_url: "https://status.example.test", username: "smtp-user", has_password: false,
        default_recipients: ["ops@example.test"] }
      : path === "/api/email/deliveries" ? { items: [], total: 0, limit: 20, offset: 0, has_more: false }
      : path === "/api/email/deliveries/summary" ? { pending_count: 0, retrying_count: 0,
        terminal_failure_count: 0, smtp_accepted_count: 1 }
      : path.endsWith("/email-summary") ? { project_key: "jobs", pending_count: 0,
        retrying_count: 0, terminal_failure_count: 0, smtp_accepted_count: 1 }
      : undefined;
    await route.fulfill(body === undefined ? { status: 404, json: { title: "Not found" } } : { json: body });
  });
}

test.beforeEach(async ({ page }) => { await fixtures(page); });

async function openWithTheme(page: Page, path: string, theme: "light" | "dark") {
  await page.goto(path);
  await page.evaluate((value) => localStorage.setItem("upaffe-theme", value), theme);
  await page.reload();
}

async function expectNoAxeViolations(page: Page, context: string) {
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
    .analyze();
  expect(results.violations, context).toEqual([]);
}

test("complete desktop dashboard retains the product-family shell", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openWithTheme(page, "/dashboard", "light");
  await expect(page.getByRole("heading", { name: "Health dashboard" })).toBeVisible();
  await expect(page).toHaveScreenshot("dashboard-shell-light-desktop.png", { fullPage: true });
});

test("complete project overview retains monitoring semantics in dark appearance", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openWithTheme(page, "/projects/jobs", "dark");
  await expect(page.getByRole("heading", { name: "Jobs", level: 1 })).toBeVisible();
  await expect(page).toHaveScreenshot("project-overview-shell-dark-desktop.png", { fullPage: true });
});

test("narrow navigation is the same focus-managed shell", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 800 });
  await openWithTheme(page, "/projects/jobs/push-monitors/backup", "light");
  await page.getByRole("button", { name: "Open menu" }).click();
  await expect(page.getByRole("dialog", { name: "Navigation" })).toBeVisible();
  await expect(page).toHaveScreenshot("push-detail-drawer-light-phone.png", { fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
});

test("inventory and project creation stay distinct at 200 percent zoom-equivalent width", async ({ page }) => {
  await page.setViewportSize({ width: 720, height: 900 });
  await openWithTheme(page, "/projects", "dark");
  await expect(page.getByRole("heading", { name: "Projects", level: 1 })).toBeVisible();
  await expect(page).toHaveScreenshot("project-inventory-dark-zoom.png", { fullPage: true });
  await page.getByRole("button", { name: "New project" }).click();
  await expect(page.getByRole("heading", { name: "New project", level: 1 })).toBeVisible();
  await expect(page).toHaveScreenshot("new-project-dark-zoom.png", { fullPage: true });
});

test("menus and destructive dialogs are reviewed within the complete shell", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openWithTheme(page, "/projects", "light");
  await page.getByRole("button", { name: "Account: operator@example.test" }).click();
  await expect(page.getByRole("menu")).toBeVisible();
  await expect(page).toHaveScreenshot("account-menu-light-desktop.png", { fullPage: true });
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Delete" }).click();
  await expect(page.getByRole("dialog", { name: "Delete project" })).toBeVisible();
  await expect(page).toHaveScreenshot("project-delete-dialog-light-desktop.png", { fullPage: true });
});

test("empty, failure, and pending states remain explicit", async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 900 });
  projectFixture = "empty";
  await page.goto("/projects");
  await expect(page.getByText("No live projects yet. Create the first project.")).toBeVisible();
  await expect(page).toHaveScreenshot("project-empty-light-tablet.png", { fullPage: true });

  projectFixture = "failure";
  await page.reload();
  await expect(page.getByRole("alert")).toContainText("HTTP 503");
  await expect(page).toHaveScreenshot("project-failure-light-tablet.png", { fullPage: true });

  projectFixture = "pending";
  await page.goto("/projects/new");
  await page.getByLabel("Immutable key").fill("jobs");
  await page.getByLabel("Display name").fill("Jobs");
  await page.getByRole("button", { name: "Create project" }).click();
  await expect(page.getByRole("button", { name: "Creating…" })).toHaveAttribute("aria-busy", "true");
  await expect(page).toHaveScreenshot("new-project-pending-light-tablet.png", { fullPage: true });
  releaseProjectCreation?.();
});

test("all authenticated routes reflow across themes and viewport sizes", async ({ page }) => {
  test.setTimeout(120_000);
  const routes = [
    ["/dashboard", "Health dashboard"], ["/projects", "Projects"],
    ["/projects/new", "New project"],
    ["/projects/jobs", "Jobs"], ["/monitors", "Monitors"],
    ["/projects/jobs/http-monitors", "Jobs"], ["/projects/jobs/push-monitors", "Jobs"],
    ["/projects/jobs/http-monitors/site", "Public website"],
    ["/projects/jobs/push-monitors/backup", "Nightly backup"],
    ["/settings/email", "Instance email"],
    ["/projects/jobs/settings/email", "Email and maintenance"],
  ] as const;
  await page.goto("/dashboard");
  for (const theme of ["light", "dark"]) {
    await page.evaluate((value) => localStorage.setItem("upaffe-theme", value), theme);
    for (const width of [360, 720, 768, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      for (const [path, heading] of routes) {
        await page.goto(path);
        await expect(page.getByRole("heading", { name: heading, level: 1 })).toBeVisible();
        const size = await page.evaluate(() => ({ content: document.documentElement.scrollWidth,
          viewport: document.documentElement.clientWidth }));
        expect(size.content, `${theme} ${width}px ${path}`).toBeLessThanOrEqual(size.viewport);
      }
    }
  }
});

test("authenticated routes pass automated WCAG 2A and 2AA checks", async ({ page }) => {
  test.setTimeout(180_000);
  const routes = [
    ["/dashboard", "Health dashboard"], ["/projects", "Projects"],
    ["/projects/new", "New project"], ["/projects/jobs", "Jobs"],
    ["/monitors", "Monitors"], ["/projects/jobs/http-monitors", "Jobs"],
    ["/projects/jobs/push-monitors", "Jobs"],
    ["/projects/jobs/http-monitors/site", "Public website"],
    ["/projects/jobs/push-monitors/backup", "Nightly backup"],
    ["/settings/email", "Instance email"],
    ["/projects/jobs/settings/email", "Email and maintenance"],
  ] as const;
  for (const [index, [path, heading]] of routes.entries()) {
    const width = index % 2 === 0 ? 360 : 1440;
    const theme = index % 2 === 0 ? "light" : "dark";
    await page.setViewportSize({ width, height: 900 });
    await openWithTheme(page, path, theme);
    await expect(page.getByRole("heading", { name: heading, level: 1 })).toBeVisible();
    await expectNoAxeViolations(page, `${theme} ${width}px ${path}`);
  }
});

test("focus-sensitive shell and form interactions work from the keyboard", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/dashboard");
  const account = page.getByRole("button", { name: "Account: operator@example.test" });
  await account.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("menu")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(account).toBeFocused();

  await page.goto("/projects/jobs");
  const switcher = page.getByRole("button", { name: "Switch project" });
  await expect(switcher).toContainText("Jobs");
  await switcher.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("menu")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(switcher).toBeFocused();

  await page.setViewportSize({ width: 360, height: 800 });
  await page.reload();
  await page.waitForLoadState("networkidle");
  const menu = page.getByRole("button", { name: "Open menu" });
  await expect(menu).toBeVisible();
  await menu.focus();
  await expect(menu).toBeFocused();
  await menu.press("Enter");
  await expect(page.getByRole("dialog", { name: "Navigation" })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(menu).toBeFocused();

  await page.goto("/projects/new");
  await page.getByLabel("Immutable key").fill("unfinished");
  await page.keyboard.press("Escape");
  const discard = page.getByRole("dialog", { name: "Discard new project?" });
  await expect(discard).toBeVisible();
  await expectNoAxeViolations(page, "unsaved project dialog");
  await page.keyboard.press("Escape");
  await expect(page.getByLabel("Immutable key")).toBeFocused();
});

test("bootstrap, sign-in, and connection failures remain accessible", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 800 });
  await page.route("http://127.0.0.1:4173/api/bootstrap", (route) => route.fulfill({ json: { required: true,
    available: true } }));
  await page.goto("/dashboard");
  await expect(page.getByRole("heading", { name: "Establish the operator" })).toBeVisible();
  await page.unroute("http://127.0.0.1:4173/api/bootstrap");
  await page.route("http://127.0.0.1:4173/api/session", (route) => route.fulfill({ status: 401,
    json: { title: "Unauthorized" } }));
  await page.reload();
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await expect(page.getByLabel("Password")).toHaveAttribute("type", "password");
  await page.unroute("http://127.0.0.1:4173/api/session");
  await page.route("http://127.0.0.1:4173/api/bootstrap", (route) => route.fulfill({ status: 502,
    json: { title: "Unavailable" } }));
  await page.reload();
  await expect(page.getByRole("heading", { name: "Instance unavailable" })).toBeVisible();
  await expect(page.getByRole("alert")).toBeVisible();
  await expect(page.getByRole("button", { name: "Try again" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
});
