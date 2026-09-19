import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";

import { App } from "@/shell/App";

const project = { id: "1eec27ae-08a4-4e34-b343-a2441988845e", key: "systems", name: "Systems",
  version: 1, created_at: "2026-09-01T00:00:00Z", updated_at: "2026-09-01T00:00:00Z", deleted_at: null };
const success = { id: "7e1ac7b0-60e5-4d8b-a773-7b5e930431c8", outcome: "success", reason: null,
  observed_at: "2026-09-19T09:00:00Z", received_at: "2026-09-19T09:00:01Z" };
const failure = { id: "7e1ac7b0-60e5-4d8b-a773-7b5e930431c9", outcome: "failure", reason: "status_mismatch",
  observed_at: "2026-09-19T11:00:00Z", received_at: "2026-09-19T11:00:01Z" };
const base = { id: "99b7299d-3c28-44e6-963a-5034ac9f1432", version: 1, mode: null,
  target_url: null, expected_status_code: null, text_condition: null, interval_seconds: 60,
  timeout_seconds: null, failure_threshold: null, failure_count: null, tolerance_seconds: null,
  last_received_at: null, next_due_at: "2026-09-19T12:05:00Z", overdue: false,
  latest_result: null, last_success: null, incident: null, direct_maintenance: null,
  effective_maintenance_until: null, instruction: null, runbook_url: null };
const attention = [
  { ...base, type: "http", key: "threshold", name: "Below threshold", state: "failing",
    failure_count: 1, failure_threshold: 3, latest_result: failure, last_success: success,
    instruction: "Contact the service owner", runbook_url: "https://docs.example.test/runbook" },
  { ...base, type: "push", key: "explicit", name: "Explicit failure", state: "failing",
    mode: "job_completion", latest_result: { ...failure, reason: "reported_failure" },
    incident: { id: "739e9223-dc11-4dfb-a8fb-73c84cc9c1d0", began_at: "2026-09-19T10:00:00Z",
      opened_at: "2026-09-19T10:01:00Z", age_seconds: 7200,
      original_reason: "reported_failure", latest_reason: "reported_failure" } },
  { ...base, type: "push", key: "missing", name: "Missing report", state: "failing",
    mode: "state_report", latest_result: { ...failure, reason: "report_missing" },
    incident: { id: "739e9223-dc11-4dfb-a8fb-73c84cc9c1d1", began_at: "2026-09-19T11:30:00Z",
      opened_at: "2026-09-19T11:31:00Z", age_seconds: 1800,
      original_reason: "report_missing", latest_reason: "report_missing" } },
  { ...base, type: "http", key: "late-check", name: "Late check", state: "healthy", overdue: true,
    next_due_at: "2026-09-19T11:55:00Z", latest_result: success, last_success: success },
  { ...base, type: "push", key: "late-report", name: "Late report", state: "untested", overdue: true,
    mode: "job_completion", next_due_at: "2026-09-19T11:30:00Z" },
  { ...base, type: "push", key: "paused", name: "Paused with incident", state: "paused",
    mode: "state_report", next_due_at: null, last_success: success,
    incident: { id: "739e9223-dc11-4dfb-a8fb-73c84cc9c1d2", began_at: "2026-09-19T08:00:00Z",
      opened_at: "2026-09-19T08:01:00Z", age_seconds: 14400,
      original_reason: "reported_failure", latest_reason: "reported_failure" },
    effective_maintenance_until: "2026-09-19T14:00:00Z" },
];
const report = { generated_at: "2026-09-19T12:00:00Z", project,
  counts: { total: 7, http: 3, push: 4, healthy: 2, failing: 3, untested: 1, paused: 1 },
  attention, healthy: [{ type: "http", id: "6a137624-bb58-4e6c-9b9e-9a8c9ca72f44",
    key: "good", name: "Healthy site", mode: null, last_success_at: success.observed_at,
    next_due_at: "2026-09-19T12:05:00Z", direct_maintenance: null,
    effective_maintenance_until: null }],
  project_maintenance: { started_at: "2026-09-19T11:00:00Z", ends_at: "2026-09-19T14:00:00Z" },
  email: { configured: true, host: "mail.example.test", port: 587, security: "starttls",
    sender_address: "alerts@example.test", public_base_url: null, has_password: true, recipients: ["team@example.test"],
    delivery: { project_key: "systems", pending_count: 2, retrying_count: 1,
      terminal_failure_count: 1, smtp_accepted_count: 3, oldest_pending_at: "2026-09-19T11:00:00Z" } } };

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status,
    headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json" } });
}

afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });

it("triages both monitor types without changing health for maintenance or delivery trouble", async () => {
  window.history.replaceState({}, "", "/projects/systems");
  const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
      email: "operator@example.test", access_path: "browser_session",
      session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2026-09-23T12:00:00Z" });
    if (path === "/api/projects") return json([project]);
    if (path === "/api/projects/systems/report") return json(report);
    if (path === "/api/projects/systems/http-monitors") return json([]);
    if (path === "/api/projects/systems/recipients") return json({ project_key: "systems", version: 1, recipients: ["ops@example.test"] });
    if (path === "/api/projects/systems/maintenance") return json({ project_key: "systems", scope_type: "project",
      version: 1, direct_active: true, effective_active: true, ends_at: "2026-09-19T14:00:00Z",
      effective_ends_at: "2026-09-19T14:00:00Z", active_scopes: ["project"] });
    if (path === "/api/projects/systems/email-summary") return json(report.email.delivery);
    if (path === "/api/email/deliveries") return json({ items: [], total: 0, limit: 20, offset: 0, has_more: false });
    throw new Error(`Unexpected ${path}`);
  });
  vi.stubGlobal("fetch", fetch);
  render(<App />);
  expect(await screen.findByRole("heading", { name: "Needs attention" })).toBeInTheDocument();
  const cards = within(screen.getByRole("region", { name: "Needs attention" })).getAllByRole("article");
  expect(cards).toHaveLength(6);
  expect(cards[0]).toHaveTextContent("1 of 3 failures before an incident opens");
  expect(cards[0]).toHaveTextContent("Latest failure:");
  expect(cards[0]).toHaveTextContent("Last success:");
  expect(cards[0]).toHaveTextContent("Contact the service owner");
  expect(within(cards[0]).getByRole("link", { name: "Open runbook" })).toHaveAttribute("href", "https://docs.example.test/runbook");
  expect(cards[1]).toHaveTextContent("Incident open: reported failure");
  expect(cards[2]).toHaveTextContent("Incident open: missing report");
  expect(cards[3]).toHaveTextContent("Check execution overdue");
  expect(cards[4]).toHaveTextContent("Report deadline passed; missing-report evaluation has not yet been recorded");
  expect(cards[5]).toHaveTextContent("Monitoring paused; incident remains open");
  expect(cards[5]).toHaveTextContent("Notifications suppressed until");
  expect(screen.getByText(/1 terminal failures · 1 retrying · 2 pending/)).toBeInTheDocument();
  expect(screen.getByText(/3 accepted by SMTP; inbox receipt is not confirmed/)).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Healthy site" })).toHaveAttribute("href", "/projects/systems/http-monitors/good");
  expect(screen.getByRole("link", { name: "Explicit failure" })).toHaveAttribute("href", "/projects/systems/push-monitors/explicit");
  expect(screen.getByRole("link", { name: "Manage HTTP monitors" })).toHaveAttribute("href", "/projects/systems/http-monitors");
  expect(screen.getByRole("link", { name: "Manage push monitors" })).toHaveAttribute("href", "/projects/systems/push-monitors");
  expect(screen.getByRole("link", { name: "Recipients and maintenance" })).toHaveAttribute("href", "/projects/systems/settings/email?return=%2Fprojects%2Fsystems");
  expect(screen.queryByText("mail.example.test")).not.toBeInTheDocument();
  const user = userEvent.setup();
  await user.click(screen.getByRole("link", { name: "Manage recipients and delivery" }));
  expect(window.location.pathname + window.location.search).toBe("/projects/systems/settings/email?return=%2Fprojects%2Fsystems");
  expect(await screen.findByRole("region", { name: "Email delivery" })).toHaveTextContent("terminal failures 1");
  expect(await screen.findByRole("region", { name: "Project maintenance" })).toHaveTextContent("Checks, reports, and incident history continue");
  await user.click(screen.getByRole("button", { name: "Back to investigation" }));
  expect(window.location.pathname).toBe("/projects/systems");
  expect(await screen.findByRole("heading", { name: "Needs attention" })).toBeInTheDocument();
  await user.click(screen.getByRole("link", { name: "Manage HTTP monitors" }));
  expect(window.location.pathname).toBe("/projects/systems/http-monitors");
  expect(await screen.findByRole("heading", { name: "Create an HTTP monitor" })).toBeInTheDocument();
});

it("shows an empty project and retries an unavailable report without rendering untrusted diagnostics", async () => {
  window.history.replaceState({}, "", "/projects/systems");
  let attempts = 0;
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
      email: "operator@example.test", access_path: "browser_session",
      session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2026-09-23T12:00:00Z" });
    if (path === "/api/projects") return json([project]);
    if (path === "/api/projects/systems/report") return ++attempts === 1
      ? json({ code: "unavailable", status: 503, title: "private diagnostic" }, 503)
      : json({ ...report, counts: { total: 0, http: 0, push: 0, healthy: 0, failing: 0, untested: 0, paused: 0 },
        attention: [], healthy: [], project_maintenance: null });
    throw new Error(`Unexpected ${path}`);
  }));
  render(<App />);
  expect(await screen.findByRole("alert")).toHaveTextContent("HTTP 503");
  expect(screen.queryByText("private diagnostic")).not.toBeInTheDocument();
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "Try again" }));
  expect(await screen.findByText("This project has no monitors. Use a management link above to create one.")).toBeInTheDocument();
});
