import { expect, test, type Page } from "@playwright/test";

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

async function fixtures(page: Page) {
  await page.addInitScript(() => {
    if (!localStorage.getItem("upaffe-theme")) localStorage.setItem("upaffe-theme", "light");
  });
  await page.route("http://127.0.0.1:4173/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    const body: unknown = path === "/api/bootstrap" ? { required: false, available: false }
      : path === "/api/session" ? { operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
        email: "operator@example.test", access_path: "browser_session",
        session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2099-09-23T12:00:00Z" }
      : path === "/api/projects" ? [project]
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

test("sidebar and overview retain their compact desktop layout", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/dashboard");
  await expect(page.getByRole("heading", { name: "Health dashboard" })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Primary" })).toHaveScreenshot("sidebar.png");
  await expect(page.getByRole("region", { name: "Health counts" })).toHaveScreenshot("health-counts.png");
});

test("monitor status and configuration remain legible on a narrow screen", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto("/projects/jobs/push-monitors/backup");
  await expect(page.getByRole("heading", { name: "Nightly backup", level: 1 })).toBeVisible();
  await expect(page.getByRole("region", { name: "Current status" })).toHaveScreenshot("push-status-mobile.png");
  await expect(page.getByRole("region", { name: "Configuration" })).toHaveScreenshot("push-form-mobile.png");
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
});

test("email form keeps labeled fields and one-time password boundary", async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 900 });
  await page.goto("/settings/email");
  await expect(page.getByRole("heading", { name: "Instance email", level: 1 })).toBeVisible();
  await expect(page.getByRole("region", { name: "SMTP settings" })).toHaveScreenshot("smtp-settings.png");
  await expect(page.getByRole("textbox", { name: "New SMTP password" })).toHaveValue("");
});

test("all authenticated routes reflow across themes and viewport sizes", async ({ page }) => {
  test.setTimeout(120_000);
  const routes = [
    ["/dashboard", "Health dashboard"], ["/projects", "Projects"],
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
