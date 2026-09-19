import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";

import { App } from "@/shell/App";
import { parseRoute } from "@/shell/routes";

const incidentId = "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901";
const project = { id: "f0187842-6f73-4c54-8a8c-57ac7c117c39", key: "jobs", name: "Jobs",
  version: 1, created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null };
const monitor = { id: "4a572f67-2edb-46d3-86de-419608f16d83", project_key: "jobs",
  key: "backup", name: "Backup", mode: "job_completion", interval_seconds: 3600,
  tolerance_seconds: 300, instruction: null, runbook_url: null, state: "failing",
  last_received_at: "2026-09-19T12:00:00Z", next_deadline_at: "2026-09-19T13:05:00Z",
  latest_report_id: null, latest_success_id: null, open_incident_id: incidentId,
  has_reporting_credential: false, version: 1, created_at: "2026-09-19T12:00:00Z",
  updated_at: "2026-09-19T12:00:00Z", paused_at: null, deleted_at: null };

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status,
    headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json" } });
}

afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });

it("parses stable project and monitor paths without accepting extra or secret segments", () => {
  expect(parseRoute("/projects/jobs")).toEqual({
    kind: "project", projectKey: "jobs", section: "overview",
  });
  expect(parseRoute("/projects/jobs/http-monitors")).toEqual({
    kind: "project", projectKey: "jobs", section: "http",
  });
  expect(parseRoute("/projects/jobs/push-monitors/backup")).toEqual({
    kind: "project", projectKey: "jobs", section: "push", monitorKey: "backup",
  });
  expect(parseRoute("/projects/jobs/settings/email")).toEqual({
    kind: "project", projectKey: "jobs", section: "email",
  });
  expect(parseRoute("/projects/jobs/push-monitors/backup/token")).toEqual({ kind: "missing" });
  expect(parseRoute("/projects/%2Fetc")).toEqual({ kind: "missing" });
});

it("returns to a push incident link after sign-in and follows browser history", async () => {
  window.history.replaceState({}, "", `/projects/jobs/push-monitors/backup?incident=${incidentId}`);
  let admitted = false;
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const request = input as Request;
    const path = new URL(request.url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session" && request.method === "GET") return admitted
      ? json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc", email: "operator@example.test",
        access_path: "browser_session", session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d",
        expires_at: "2026-09-23T12:00:00Z" })
      : json({ code: "authentication_required", status: 401 }, 401);
    if (path === "/api/session" && request.method === "POST") {
      admitted = true;
      return new Response(null, { status: 204 });
    }
    if (path === "/api/projects") return json([project]);
    if (path === "/api/projects/jobs/push-monitors") return json([monitor]);
    if (path === "/api/projects/jobs/push-monitors/backup") return json(monitor);
    if (path.endsWith("/reports")) return json({ items: [], next_before_sequence: null });
    if (path.endsWith("/incidents")) return json({ items: [{ id: incidentId,
      opened_at: "2026-09-19T12:00:00Z", original_reason: "reported_failure",
      latest_reason: "reported_failure", resolved_at: null }], next_before_opening_sequence: null });
    if (path.endsWith("/maintenance")) return json({ project_key: "jobs", scope_type: "push",
      monitor_key: "backup", version: 0, direct_active: false, effective_active: false, active_scopes: [] });
    if (path === "/api/email/deliveries") return json({ items: [], total: 0, limit: 20,
      offset: 0, has_more: false });
    if (path === `/api/email/incidents/${incidentId}`) return json({ incident_id: incidentId,
      project_key: "jobs", monitor_type: "push", monitor_key: "backup", open: true,
      announcement_state: "announced", suppression_reason: null, deliveries: [] });
    throw new Error(`Unexpected ${request.method} ${path}`);
  }));
  const user = userEvent.setup();
  render(<App />);
  expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  await user.type(screen.getByLabelText("Email"), "operator@example.test");
  await user.type(screen.getByLabelText("Password"), "a long password");
  await user.click(screen.getByRole("button", { name: "Sign in" }));
  expect(await screen.findByRole("heading", { name: "Backup" })).toBeInTheDocument();
  expect(await screen.findByText("Announcement: announced")).toBeInTheDocument();
  expect(window.location.search).toBe(`?incident=${incidentId}`);
  await user.click(screen.getByRole("button", { name: "Back to push monitors" }));
  expect(window.location.pathname).toBe("/projects/jobs/push-monitors");
  await waitFor(() => expect(screen.getByRole("heading", { name: "Jobs" })).toBeInTheDocument());
  act(() => { window.history.back(); });
  await waitFor(() => expect(window.location.pathname).toBe("/projects/jobs/push-monitors/backup"));
  expect(await screen.findByRole("heading", { name: "Backup" })).toBeInTheDocument();
  act(() => { window.history.forward(); });
  await waitFor(() => expect(window.location.pathname).toBe("/projects/jobs/push-monitors"));
});

it("gives a deleted project a route back to the project list", async () => {
  window.history.replaceState({}, "", "/projects/gone");
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
      email: "operator@example.test", access_path: "browser_session",
      session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2026-09-23T12:00:00Z" });
    if (path === "/api/projects") return json([]);
    throw new Error(`Unexpected ${path}`);
  }));
  const user = userEvent.setup();
  render(<App />);
  expect(await screen.findByRole("heading", { name: "Project unavailable" })).toBeInTheDocument();
  await user.click(screen.getByRole("link", { name: "Go to projects" }));
  expect(window.location.pathname).toBe("/projects");
  expect(await screen.findByRole("heading", { name: "Projects" })).toBeInTheDocument();
});

it("keeps an unknown HTTP detail inside the project hierarchy", async () => {
  window.history.replaceState({}, "", "/projects/jobs/http-monitors/missing");
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
      email: "operator@example.test", access_path: "browser_session",
      session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2026-09-23T12:00:00Z" });
    if (path === "/api/projects") return json([project]);
    if (path === "/api/projects/jobs/http-monitors") return json([]);
    if (path === "/api/projects/jobs/http-monitors/missing")
      return json({ code: "not_found", status: 404 }, 404);
    if (path.endsWith("/checks")) return json({ items: [], next_before_sequence: null });
    if (path.endsWith("/incidents")) return json({ items: [], next_before_opening_sequence: null });
    throw new Error(`Unexpected ${path}`);
  }));
  const user = userEvent.setup();
  render(<App />);
  expect(await screen.findByText("The requested object no longer exists.")).toBeInTheDocument();
  expect(screen.getByRole("navigation", { name: "Breadcrumb" })).toHaveTextContent("Jobs");
  await user.click(screen.getByRole("button", { name: "Back to monitors" }));
  expect(window.location.pathname).toBe("/projects/jobs/http-monitors");
});
