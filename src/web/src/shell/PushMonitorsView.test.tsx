import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { NewPushMonitorView, PushMonitorsView } from "@/shell/PushMonitorsView";

const project = {
  id: "f0187842-6f73-4c54-8a8c-57ac7c117c39",
  key: "backup-jobs",
  name: "Backup jobs",
  version: 1,
  created_at: "2026-09-18T10:00:00Z",
  updated_at: "2026-09-18T10:00:00Z",
  deleted_at: null,
};

const report = {
  id: "b843ea2f-875f-430d-bebc-02a8f66c2948",
  report_id: "1c789ae8-d229-48a8-b3e6-a14ba6d2b33a",
  evaluation_generation: 1,
  sequence: 12,
  observed_at: "2026-09-18T11:00:00Z",
  received_at: "2026-09-18T11:00:01Z",
  outcome: "failure",
  reason: "backup_failed",
  applicable: true,
};

const success = {
  ...report,
  id: "e5039eef-6d89-4c47-8d7a-9206bb2fa70a",
  report_id: "98dd341a-ecfa-4d9a-81d3-1c69c98801b7",
  sequence: 11,
  outcome: "success",
  reason: null,
  observed_at: "2026-09-18T10:00:00Z",
  received_at: "2026-09-18T10:00:01Z",
};

const monitor = {
  id: "a24b957b-4452-478b-8bac-4eb86bd9f438",
  project_key: project.key,
  key: "nightly-backup",
  name: "Nightly backup",
  purpose: null as string | null,
  mode: "job_completion",
  interval_seconds: 3600,
  tolerance_seconds: 300,
  instruction: "Inspect the backup log",
  runbook_url: "https://docs.example.test/runbooks/backup",
  state: "failing",
  last_received_at: report.received_at,
  next_deadline_at: "2026-09-18T11:05:00Z",
  latest_report_id: report.id,
  latest_success_id: success.id,
  open_incident_id: "ddbc569f-a269-4985-87bb-873748071b6c",
  has_reporting_credential: false,
  version: 1,
  created_at: "2026-09-18T09:00:00Z",
  updated_at: "2026-09-18T11:00:01Z",
  paused_at: null as string | null,
  deleted_at: null as string | null,
};

const incident = {
  id: monitor.open_incident_id,
  opening_report_id: report.id,
  latest_failure_report_id: report.id,
  resolution_report_id: null,
  opening_sequence: 12,
  latest_failure_sequence: 12,
  resolution_sequence: null,
  began_at: report.observed_at,
  opened_at: report.observed_at,
  last_observed_at: report.observed_at,
  resolved_at: null,
  original_reason: "backup_failed",
  latest_reason: "backup_failed",
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json" },
  });
}

function answering(answer: (request: Request) => Promise<Response> | Response) {
  const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
    const request = input as Request;
    const path = new URL(request.url).pathname;
    if (request.method === "GET" && path.endsWith("/maintenance")) return json({ project_key: project.key, scope_type: "push", version: 0, direct_active: false, effective_active: false, active_scopes: [] });
    if (request.method === "GET" && path === "/api/email/deliveries") return json({ items: [], total: 0, limit: 20, offset: 0, has_more: false });
    if (request.method === "GET" && path.endsWith("/email-summary")) return json({ project_key: project.key, pending_count: 0, retrying_count: 0, terminal_failure_count: 0, smtp_accepted_count: 0 });
    return answer(request);
  });
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

describe("push monitor administration", () => {
  afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });

  it("shows purpose as text and clears it through the versioned form", async () => {
    let current = { ...monitor, purpose: "<b>Confirms the backup</b>" };
    answering(async (request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith(`/push-monitors/${monitor.key}`) && request.method === "GET") return json(current);
      if (path.endsWith(`/push-monitors/${monitor.key}`) && request.method === "PUT") {
        expect(await request.json()).toMatchObject({ purpose: null, version: 1 });
        current = { ...current, purpose: "", version: 2 };
        return json({ ...current, purpose: null });
      }
      if (path.endsWith("/reports")) return json({ items: [], next_before_sequence: null });
      if (path.endsWith("/incidents")) return json({ items: [], next_before_opening_sequence: null });
      return json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<PushMonitorsView onOpenHttp={vi.fn()} onSignedOut={vi.fn()}
      project={project} routeMonitorKey={monitor.key} />);
    expect(await screen.findByText("<b>Confirms the backup</b>", { selector: ".monitor-purpose" })).toBeInTheDocument();
    expect(document.querySelector(".monitor-purpose b")).toBeNull();
    const user = userEvent.setup();
    await user.clear(screen.getByLabelText("Purpose (optional)"));
    await user.click(screen.getByRole("button", { name: "Save configuration" }));
    expect(await screen.findByText(/Purpose not documented/, { selector: ".monitor-purpose" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Add purpose" })).toHaveAttribute("href", "#push-configuration-title");
  });

  it("loads old success and deep-linked incident evidence without exposing sender diagnostics", async () => {
    const oldSuccess = { ...success, sequence: 1, observed_at: "2026-08-01T10:00:00Z",
      received_at: "2026-08-01T10:00:03Z" };
    window.history.replaceState({}, "",
      `/projects/backup-jobs/push-monitors/nightly-backup?incident=${incident.id}`);
    const fetch = answering((request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith(`/push-monitors/${monitor.key}`)) return json(monitor);
      if (path.endsWith("/reports")) return json({ items: [report], next_before_sequence: 12 });
      if (path.endsWith(`/reports/${oldSuccess.id}`)) return json(oldSuccess);
      if (path.endsWith("/incidents")) return json({ items: [], next_before_opening_sequence: 12 });
      if (path === `/api/email/incidents/${incident.id}`) return json({ incident_id: incident.id,
        announcement_state: "announced", suppression_reason: null, deliveries: [] });
      if (path.endsWith(`/incidents/${incident.id}`)) return json(incident);
      throw new Error(`Unexpected ${path}`);
    });
    render(<PushMonitorsView onOpenHttp={vi.fn()} onSignedOut={vi.fn()}
      project={project} routeMonitorKey={monitor.key} />);
    expect(await screen.findByRole("heading", { name: "Nightly backup", level: 1 })).toBeInTheDocument();
    expect(screen.getByText("Last success", { selector: "dt" }).nextElementSibling).toHaveTextContent("2026");
    expect(screen.getByText("Latest report", { selector: "dt" }).nextElementSibling).toHaveTextContent("observed");
    expect(screen.getByText("Latest report", { selector: "dt" }).nextElementSibling).toHaveTextContent("received");
    expect(await screen.findByRole("region", { name: "Selected incident" })).toHaveTextContent("latest reason backup_failed");
    expect(await screen.findByText("Announcement: announced")).toBeInTheDocument();
    expect(fetch.mock.calls.some(([request]) => new URL((request as Request).url).pathname.endsWith(`/reports/${oldSuccess.id}`))).toBe(true);
    expect(screen.queryByText("private sender detail")).not.toBeInTheDocument();
    expect(screen.queryByText(oldSuccess.id)).not.toBeInTheDocument();
  });

  it("lists both modes and creates a state report from the explained form", async () => {
    const stateMonitor = { ...monitor, id: "75798222-1923-4a8b-a556-768a031b320f", key: "local-state", name: "Local state", mode: "state_report", state: "healthy", open_incident_id: null };
    let monitors = [monitor, stateMonitor];
    answering(async (request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/push-monitors") && request.method === "GET") return json(monitors);
      if (path.endsWith("/push-monitors") && request.method === "POST") {
        expect(request.headers.get("X-Upaffe-CSRF")).toBe("1");
        expect(await request.json()).toEqual({
          key: "database-state",
          name: "Database state",
          purpose: "Reports database replication health.",
          mode: "state_report",
          interval_seconds: 120,
          tolerance_seconds: 30,
          instruction: "Check replication",
          runbook_url: "https://docs.example.test/runbooks/database",
        });
        const created = { ...stateMonitor, id: "bb51f0d7-8f04-4a8a-b85f-1683a15ce172", key: "database-state", name: "Database state", purpose: "Reports database replication health.", interval_seconds: 120, tolerance_seconds: 30, instruction: "Check replication", runbook_url: "https://docs.example.test/runbooks/database" };
        monitors = [...monitors, created];
        return json(created, 201);
      }
      const created = monitors[2];
      if (path.endsWith("/push-monitors/database-state")) return json(created);
      if (path.endsWith("/reports")) return json({ items: [], next_before_sequence: null });
      if (path.endsWith("/incidents")) return json({ items: [], next_before_opening_sequence: null });
      return json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<PushMonitorsView onOpenHttp={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();

    expect(await screen.findByText(/Job completion · every/, { selector: ".monitor-target" })).toBeInTheDocument();
    expect(screen.getByText(/State report · every/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Immutable key")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "New push monitor" }));
    expect(await screen.findByRole("heading", { name: "New push monitor", level: 1 })).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Reporting mode"), "state_report");
    expect(screen.getByRole("note")).toHaveTextContent("latest health assessment");
    expect(screen.getByRole("note")).toHaveTextContent("Silence");
    await user.type(screen.getByLabelText("Immutable key"), "database-state");
    await user.type(screen.getByLabelText("Display name"), "Database state");
    await user.type(screen.getByLabelText("Purpose (optional)"), "Reports database replication health.");
    await user.clear(screen.getByLabelText("Expected interval (seconds)"));
    await user.type(screen.getByLabelText("Expected interval (seconds)"), "120");
    await user.clear(screen.getByLabelText("Deadline tolerance (seconds)"));
    await user.type(screen.getByLabelText("Deadline tolerance (seconds)"), "30");
    await user.type(screen.getByLabelText("Operator instruction"), "Check replication");
    await user.type(screen.getByLabelText("Runbook URL"), "https://docs.example.test/runbooks/database");
    await user.click(screen.getByRole("button", { name: "Create push monitor" }));
    expect(await screen.findByRole("heading", { name: "Database state", level: 1 })).toBeInTheDocument();
    expect(screen.getByText("Reports database replication health.", { selector: ".monitor-purpose" })).toBeInTheDocument();
    expect(screen.getByText("State report", { selector: "dd" })).toBeInTheDocument();
  });

  it("shows deadlines, explicit failure, missing reports, history, and lifecycle states", async () => {
    let current: Omit<typeof monitor, "next_deadline_at"> & { next_deadline_at: string | null } = { ...monitor };
    let firstReports = true;
    const operations: string[] = [];
    answering(async (request) => {
      const url = new URL(request.url);
      const path = url.pathname;
      if (path.endsWith("/push-monitors") && request.method === "GET") return json([current]);
      if (path.endsWith("/reports")) {
        if (url.searchParams.has("before_sequence")) {
          operations.push("older-reports");
          return json({ items: [{ ...report, id: "77f35430-6083-4bda-a9d3-e831c3e9dbd6", sequence: 10, reason: "report_missing", received_at: "2026-09-18T09:05:01Z" }], next_before_sequence: null });
        }
        const cursor = firstReports ? 11 : null;
        firstReports = false;
        return json({ items: [report, success], next_before_sequence: cursor });
      }
      if (path.endsWith("/incidents")) return json({ items: [incident], next_before_opening_sequence: null });
      if (path.endsWith(`/push-monitors/${monitor.key}`) && request.method === "GET") return json(current);
      if (path.endsWith("/pause")) {
        expect(await request.json()).toEqual({ version: 1 });
        current = { ...current, state: "paused", paused_at: "2026-09-18T11:01:00Z", next_deadline_at: null, version: 2 };
        operations.push("pause");
        return json(current);
      }
      if (path.endsWith("/resume")) {
        expect(await request.json()).toEqual({ version: 2 });
        current = { ...current, state: "untested", paused_at: null, next_deadline_at: "2026-09-18T12:06:00Z", version: 3 };
        operations.push("resume");
        return json(current);
      }
      return json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<PushMonitorsView onOpenHttp={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();
    await user.click((await screen.findAllByRole("button", { name: "Open details" }))[0]);

    expect(await screen.findByRole("heading", { name: "Nightly backup", level: 1 })).toBeInTheDocument();
    expect(screen.getByText("Incident open", { selector: ".ui-badge" })).toBeInTheDocument();
    expect(screen.getByText("Failed report")).toBeInTheDocument();
    expect(screen.getAllByText(/reason backup_failed/)).toHaveLength(3);
    expect(screen.getByText("Open incident", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByText("Inspect the backup log", { selector: ".operator-note p" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open runbook" })).toHaveAttribute("href", monitor.runbook_url);

    await user.click(screen.getByRole("button", { name: "Load older reports" }));
    expect(await screen.findByText("Missing report")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Pause" }));
    expect(await screen.findByText("Paused · incident remains open", { selector: ".ui-badge" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Resume" }));
    expect(await screen.findByText("Untested · incident remains open", { selector: ".ui-badge" })).toBeInTheDocument();
    expect(operations).toEqual(["older-reports", "pause", "resume"]);
  });

  it("reveals a credential once, clears it from state, and redacts remote problem text", async () => {
    let current = { ...monitor, state: "untested", open_incident_id: null, latest_report_id: null, latest_success_id: null, last_received_at: null };
    let issued = false;
    const secret = "uar_example-one-time-secret";
    const reportUrl = "https://upaffe.example.test/r/example-secret-path";
    answering(async (request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/push-monitors") && request.method === "GET") return json([current]);
      if (path.endsWith("/reports")) return json({ items: [], next_before_sequence: null });
      if (path.endsWith("/incidents")) return json({ items: [], next_before_opening_sequence: null });
      if (path.endsWith(`/push-monitors/${monitor.key}`) && request.method === "GET") return json(current);
      if (path.endsWith("/reporting-credential") && request.method === "POST") {
        issued = true;
        current = { ...current, has_reporting_credential: true };
        return json({ id: "08702220-1471-4cc1-899e-c7d2aa8f590b", token: secret, report_url: reportUrl, created_at: "2026-09-18T12:00:00Z", rotated_at: null, previous_valid_until: null }, 201);
      }
      if (path.endsWith("/reporting-credential/rotate")) return json({ code: "conflict", status: 409, title: `attacker says ${secret}` }, 409);
      if (path.endsWith("/reporting-credential") && request.method === "GET" && issued) return json({ id: "08702220-1471-4cc1-899e-c7d2aa8f590b", created_at: "2026-09-18T12:00:00Z", rotated_at: null, revoked_at: null });
      return json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<PushMonitorsView onOpenHttp={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();
    await user.click((await screen.findAllByRole("button", { name: "Open details" }))[0]);
    await screen.findByText("No active reporting credential.");

    await user.click(screen.getByRole("button", { name: "Issue reporting credential" }));
    expect(await screen.findByText(secret)).toBeInTheDocument();
    expect(screen.getByText(reportUrl)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "I have saved them" }));
    expect(screen.queryByText(secret)).not.toBeInTheDocument();
    expect(screen.queryByText(reportUrl)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Rotate and reveal new credential" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("conflicts with the current state");
    expect(screen.queryByText(/attacker says/)).not.toBeInTheDocument();
    expect(screen.queryByText(secret)).not.toBeInTheDocument();
  });

  it("reloads the current version after an edit conflict", async () => {
    let current = { ...monitor };
    let detailReads = 0;
    answering(async (request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/push-monitors") && request.method === "GET") return json([current]);
      if (path.endsWith(`/push-monitors/${monitor.key}`) && request.method === "GET") { detailReads += 1; return json(current); }
      if (path.endsWith("/reports")) return json({ items: [report, success], next_before_sequence: null });
      if (path.endsWith("/incidents")) return json({ items: [incident], next_before_opening_sequence: null });
      if (path.endsWith(`/push-monitors/${monitor.key}`) && request.method === "PUT") {
        current = { ...current, name: "Name from another operator", version: 2 };
        return json({ code: "conflict", status: 409, title: "untrusted details" }, 409);
      }
      return json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<PushMonitorsView onOpenHttp={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();
    await user.click((await screen.findAllByRole("button", { name: "Open details" }))[0]);
    const configuration = await screen.findByRole("heading", { name: "Configuration" });
    const panel = configuration.closest("section")!;
    const name = within(panel).getByLabelText("Display name");
    await user.clear(name);
    await user.type(name, "My stale edit");
    await user.click(within(panel).getByRole("button", { name: "Save configuration" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Refresh and try again");
    expect(within(panel).getByLabelText("Display name")).toHaveValue("Name from another operator");
    expect(detailReads).toBe(2);
  });
  it("lands on the inventory and offers creation from an empty state", async () => {
    const onCreate = vi.fn();
    answering((request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/push-monitors")) return json([]);
      return json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<PushMonitorsView onCreate={onCreate} onOpenHttp={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    expect(screen.getByRole("heading", { name: "Push monitors", level: 1 })).toBeInTheDocument();
    expect(await screen.findByText(/No push monitors in Backup jobs yet/)).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Create the first push monitor" }));
    await user.click(screen.getByRole("button", { name: "New push monitor" }));
    expect(onCreate).toHaveBeenCalledTimes(2);
  });

  it("offers a retry when the inventory cannot be read", async () => {
    let fail = true;
    answering((request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/push-monitors") && fail) { fail = false; return json({ code: "unavailable", status: 503, title: "private" }, 503); }
      if (path.endsWith("/push-monitors")) return json([monitor]);
      return json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<PushMonitorsView onOpenHttp={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("HTTP 503");
    expect(screen.queryByText(/No push monitors/)).not.toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByRole("link", { name: "Nightly backup" })).toHaveAttribute("href", "/projects/backup-jobs/push-monitors/nightly-backup");
  });
});

describe("push monitor history", () => {
  afterEach(() => { vi.unstubAllGlobals(); });

  it("offers no older pages when the API omits the last-page cursor", async () => {
    answering((request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith(`/push-monitors/${monitor.key}`)) return json({ ...monitor, open_incident_id: undefined });
      if (path.endsWith("/reports")) return json({ items: [report, success] });
      if (path.endsWith("/incidents")) return json({ items: [] });
      return json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<PushMonitorsView onOpenHttp={vi.fn()} onSignedOut={vi.fn()} project={project} routeMonitorKey={monitor.key} />);
    expect(await screen.findByText("Failed report")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Load older reports" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Load older incidents" })).not.toBeInTheDocument();
  });
});

describe("push monitor creation", () => {
  afterEach(() => { vi.unstubAllGlobals(); });

  it("leaves an untouched form directly and protects typed input", async () => {
    const onCancel = vi.fn();
    answering(() => json({ code: "not_found", status: 404, title: "ignored" }, 404));
    render(<NewPushMonitorView onCancel={onCancel} onCreated={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();
    expect(screen.getByLabelText("Immutable key")).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onCancel).toHaveBeenCalledTimes(1);

    await user.type(screen.getByLabelText("Display name"), "Half typed");
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    const dialog = await screen.findByRole("dialog", { name: "Discard new push monitor?" });
    await user.click(within(dialog).getByRole("button", { name: "Keep editing" }));
    expect(screen.getByLabelText("Display name")).toHaveValue("Half typed");
    expect(onCancel).toHaveBeenCalledTimes(1);

    await user.keyboard("{Escape}");
    await user.click(within(await screen.findByRole("dialog", { name: "Discard new push monitor?" }))
      .getByRole("button", { name: "Discard" }));
    expect(onCancel).toHaveBeenCalledTimes(2);
  });

  it("shows field errors beside their fields and opens the created monitor", async () => {
    const onCreated = vi.fn();
    let attempts = 0;
    answering(async (request) => {
      attempts += 1;
      if (attempts === 1) return json({ code: "validation", status: 400, title: "private",
        errors: { interval_seconds: ["Interval must be at least 30 seconds."] } }, 400);
      const body = await request.json() as { key: string; mode: string };
      expect(body.mode).toBe("job_completion");
      return json({ ...monitor, key: body.key }, 201);
    });
    render(<NewPushMonitorView onCancel={vi.fn()} onCreated={onCreated} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Immutable key"), "nightly-backup");
    await user.type(screen.getByLabelText("Display name"), "Nightly backup");
    await user.click(screen.getByRole("button", { name: "Create push monitor" }));
    expect(await screen.findByText("Interval must be at least 30 seconds.")).toBeInTheDocument();
    expect(screen.getByLabelText("Expected interval (seconds)")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Immutable key")).toHaveValue("nightly-backup");
    await user.click(screen.getByRole("button", { name: "Create push monitor" }));
    await vi.waitFor(() => expect(onCreated).toHaveBeenCalledWith("nightly-backup"));
  });
});
