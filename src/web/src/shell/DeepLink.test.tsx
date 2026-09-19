import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";

import { App } from "@/shell/App";

const incidentId = "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901";
const project = { id: "f0187842-6f73-4c54-8a8c-57ac7c117c39", key: "systems", name: "Systems", version: 1, created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null };
const monitor = { id: "4a572f67-2edb-46d3-86de-419608f16d83", project_key: "systems", key: "site", name: "Site", target_url: "https://site.example.test/health", has_target_query: false, expected_status_code: 200, text_condition: "none", text_fragment: null, interval_seconds: 300, timeout_seconds: 10, failure_threshold: 1, instruction: null, runbook_url: null, state: "failing", consecutive_failures: 1, next_check_at: "2026-09-19T12:05:00Z", latest_result_id: null, latest_success_id: null, open_incident_id: incidentId, headers: [], version: 1, created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:01:00Z", paused_at: null, deleted_at: null };

function json(body: unknown) { return new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } }); }

afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });

it("opens the monitor and incident from an email detail link after sign-in", async () => {
  window.history.replaceState({}, "", `/projects/systems/http-monitors/site?incident=${incidentId}`);
  const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc", email: "operator@example.test", access_path: "browser_session", session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2026-09-23T12:00:00Z" });
    if (path === "/api/projects") return json([project]);
    if (path === "/api/projects/systems/http-monitors") return json([monitor]);
    if (path === "/api/projects/systems/http-monitors/site") return json(monitor);
    if (path.endsWith("/checks")) return json({ items: [], next_before_sequence: null });
    if (path.endsWith("/incidents")) return json({ items: [{ id: incidentId, opened_at: "2026-09-19T12:01:00Z", original_reason: "timeout", latest_reason: "timeout", resolved_at: null }], next_before_opening_sequence: null });
    if (path.endsWith("/maintenance")) return json({ project_key: "systems", scope_type: "http", monitor_key: "site", version: 0, direct_active: false, effective_active: false, active_scopes: [] });
    if (path === "/api/email/deliveries") return json({ items: [], total: 0, limit: 20, offset: 0, has_more: false });
    if (path.endsWith("/email-summary")) return json({ project_key: "systems", pending_count: 0, retrying_count: 0, terminal_failure_count: 0, smtp_accepted_count: 0 });
    if (path === `/api/email/incidents/${incidentId}`) return json({ incident_id: incidentId, project_key: "systems", monitor_type: "http", monitor_key: "site", open: true, announcement_state: "announced", suppression_reason: null, deliveries: [] });
    throw new Error(`Unexpected path ${path}`);
  });
  vi.stubGlobal("fetch", fetch);
  const user = userEvent.setup();
  render(<App />);
  expect(await screen.findByRole("heading", { name: "Site" })).toBeInTheDocument();
  expect(await screen.findByText("Announcement: announced")).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Back to monitors" }));
  await user.click(screen.getByRole("button", { name: "Back to projects" }));
  expect(window.location.pathname).toBe("/projects");
});
