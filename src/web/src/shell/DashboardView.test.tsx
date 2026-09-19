import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";

import { App } from "@/shell/App";

const generatedAt = "2026-09-19T12:00:00Z";
const emptyDelivery = { pending: 0, overdue: 0, retrying: 0, terminal_failure: 0,
  accepted: 0, oldest_pending_at: null };
const emptyCounts = { total: 0, healthy: 0, failing: 0, untested: 0, paused: 0, overdue: 0 };
const overview = {
  generated_at: generatedAt,
  counts: { total: 6, healthy: 2, failing: 2, untested: 1, paused: 1, overdue: 2 },
  delivery: { pending: 2, overdue: 1, retrying: 1, terminal_failure: 1, accepted: 3,
    oldest_pending_at: "2026-09-19T11:30:00Z" },
  attention: [
    { project_key: "affected", type: "push", key: "missing", name: "Missing backup",
      state: "failing", mode: "job_completion", overdue: false, next_due_at: "2026-09-19T13:00:00Z",
      last_success_at: "2026-09-18T11:00:00Z", latest_result_at: "2026-09-19T11:00:00Z",
      latest_outcome: "failure", latest_reason: "report_missing", incident_id: "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901",
      incident_began_at: "2026-09-19T11:00:00Z", incident_reason: "report_missing",
      effective_maintenance_until: "2026-09-19T13:00:00Z" },
    { project_key: "affected", type: "http", key: "site", name: "Website",
      state: "failing", mode: null, overdue: false, next_due_at: "2026-09-19T12:05:00Z",
      last_success_at: "2026-09-19T10:00:00Z", latest_result_at: "2026-09-19T11:58:00Z",
      latest_outcome: "failure", latest_reason: "status_mismatch", incident_id: null,
      incident_began_at: null, incident_reason: null, effective_maintenance_until: null },
    { project_key: "affected", type: "http", key: "late", name: "Late check",
      state: "healthy", mode: null, overdue: true, next_due_at: "2026-09-19T11:55:00Z",
      last_success_at: "2026-09-19T11:00:00Z", latest_result_at: "2026-09-19T11:00:00Z",
      latest_outcome: "success", latest_reason: null, incident_id: null,
      incident_began_at: null, incident_reason: null, effective_maintenance_until: null },
    { project_key: "affected", type: "push", key: "silent", name: "Silent sender",
      state: "untested", mode: "state_report", overdue: true, next_due_at: "2026-09-19T11:30:00Z",
      last_success_at: null, latest_result_at: null, latest_outcome: null, latest_reason: null,
      incident_id: null, incident_began_at: null, incident_reason: null, effective_maintenance_until: null },
    { project_key: "affected", type: "push", key: "paused", name: "Paused sender",
      state: "paused", mode: "state_report", overdue: false, next_due_at: null,
      last_success_at: "2026-09-18T11:00:00Z", latest_result_at: null, latest_outcome: null,
      latest_reason: null, incident_id: null, incident_began_at: null, incident_reason: null,
      effective_maintenance_until: null },
  ],
  projects: [
    { key: "affected", name: "Affected", counts: { total: 5, healthy: 1, failing: 2,
      untested: 1, paused: 1, overdue: 2 }, delivery: { pending: 2, overdue: 1,
      retrying: 1, terminal_failure: 1, accepted: 3, oldest_pending_at: "2026-09-19T11:30:00Z" },
      maintenance_until: "2026-09-19T13:00:00Z" },
    { key: "healthy", name: "Healthy project", counts: { ...emptyCounts, total: 1, healthy: 1 },
      delivery: emptyDelivery, maintenance_until: null },
    { key: "empty", name: "Empty project", counts: emptyCounts,
      delivery: emptyDelivery, maintenance_until: null },
  ],
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status,
    headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json" } });
}

afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });

it("prioritizes distinct health problems and keeps healthy and empty projects visible", async () => {
  window.history.replaceState({}, "", "/");
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
      email: "operator@example.test", access_path: "browser_session",
      session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2026-09-23T12:00:00Z" });
    if (path === "/api/overview") return json(overview);
    if (path === "/api/email/settings") return json({ version: 1, host: "mail.example.test", port: 587,
      security: "starttls", sender_address: "notify@example.test", sender_name: "Monitor",
      public_base_url: null, username: null, has_password: false, default_recipients: [] });
    if (path === "/api/email/deliveries/summary") return json({ pending_count: 0, retrying_count: 0,
      terminal_failure_count: 1, smtp_accepted_count: 0 });
    if (path === "/api/email/deliveries") return json({ items: [{ id: "e9687622-7095-4369-93a9-2632b350e456",
      incident_id: "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901", kind: "alert", recipient: "ops@example.test",
      project_key: "affected", monitor_type: "push", monitor_key: "missing", state: "terminal_failure",
      attempt_count: 5, last_error_code: "smtp_timeout", created_at: generatedAt }],
      total: 1, limit: 20, offset: 0, has_more: false });
    throw new Error(`Unexpected ${path}`);
  }));
  render(<App />);
  expect(await screen.findByRole("heading", { name: "Health dashboard" })).toBeInTheDocument();
  expect(await screen.findByText(/Incident open: missing report/)).toBeInTheDocument();
  expect(screen.getByText(/Incident began 1h ago/)).toBeInTheDocument();
  expect(screen.getByText(/Failed check; no incident open: HTTP status mismatch/)).toBeInTheDocument();
  expect(screen.getByText(/Check execution overdue/)).toBeInTheDocument();
  expect(screen.getByText(/Report deadline passed; missing-report evaluation has not yet been recorded/)).toBeInTheDocument();
  expect(screen.getByText(/Monitoring paused/)).toBeInTheDocument();
  expect(screen.getByText(/Notifications suppressed by maintenance until/)).toBeInTheDocument();
  expect(screen.getByText(/3 accepted by SMTP; inbox receipt is not confirmed/)).toBeInTheDocument();
  expect(screen.getByText(/1 terminal failures · 1 retrying · 1 overdue · 2 pending/)).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Missing backup" })).toHaveAttribute("href", "/projects/affected/push-monitors/missing");
  expect(screen.getByRole("link", { name: "Website" })).toHaveAttribute("href", "/projects/affected/http-monitors/site");
  expect(screen.getByRole("link", { name: "Healthy project" })).toHaveAttribute("href", "/projects/healthy");
  expect(screen.getByRole("link", { name: "Empty project" })).toHaveAttribute("href", "/projects/empty");
  expect(screen.queryByText("secret-query-value")).not.toBeInTheDocument();
  expect(screen.queryByText("untrusted diagnostic")).not.toBeInTheDocument();
  expect(screen.queryByRole("heading", { name: "Create a project" })).not.toBeInTheDocument();
  const user = userEvent.setup();
  await user.click(screen.getByRole("link", { name: "Open email status and settings" }));
  expect(window.location.pathname + window.location.search).toBe("/settings/email?return=%2Fdashboard");
  expect(await screen.findByRole("region", { name: "Instance email delivery" })).toHaveTextContent("Failure code smtp_timeout");
  expect(screen.getByRole("link", { name: /Inspect incident and recipient status/ })).toHaveAttribute("href",
    "/projects/affected/push-monitors/missing?incident=dcf2cf4b-1070-45b7-bd39-dfd12f1fb901");
  await user.click(screen.getByRole("button", { name: "Back to investigation" }));
  expect(window.location.pathname).toBe("/dashboard");
  expect(await screen.findByRole("heading", { name: "Health dashboard" })).toBeInTheDocument();
});

it("shows loading, a safe failure, and a working retry", async () => {
  window.history.replaceState({}, "", "/dashboard");
  let attempts = 0;
  let failFirst!: (response: Response) => void;
  const first = new Promise<Response>((resolve) => { failFirst = resolve; });
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
      email: "operator@example.test", access_path: "browser_session",
      session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2026-09-23T12:00:00Z" });
    if (path === "/api/overview") return ++attempts === 1
      ? first
      : json({ ...overview, attention: [], projects: [], counts: emptyCounts, delivery: emptyDelivery });
    throw new Error(`Unexpected ${path}`);
  }));
  const user = userEvent.setup();
  render(<App />);
  expect(await screen.findByText("Loading instance health…")).toBeInTheDocument();
  failFirst(json({ code: "unavailable", status: 503, title: "untrusted diagnostic" }, 503));
  expect(await screen.findByRole("alert")).toHaveTextContent("HTTP 503");
  expect(screen.queryByText("untrusted diagnostic")).not.toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Try again" }));
  await waitFor(() => expect(screen.getByText("No monitors need attention in this snapshot.")).toBeInTheDocument());
  expect(screen.getByText("No projects yet. Create one from Projects.")).toBeInTheDocument();
});
