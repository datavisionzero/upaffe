import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { MonitorsView } from "@/shell/MonitorsView";

const project = {
  id: "f0187842-6f73-4c54-8a8c-57ac7c117c39",
  key: "public-site",
  name: "Public site",
  version: 1,
  created_at: "2026-09-16T12:00:00Z",
  updated_at: "2026-09-16T12:00:00Z",
  deleted_at: null,
};

const baseMonitor = {
  id: "4a572f67-2edb-46d3-86de-419608f16d83",
  project_key: project.key,
  key: "homepage",
  name: "Homepage",
  purpose: null as string | null,
  target_url: "https://status.example.test/health",
  has_target_query: true,
  expected_status_code: 200,
  text_condition: "required",
  text_fragment: "ready",
  interval_seconds: 60,
  timeout_seconds: 10,
  failure_threshold: 3,
  instruction: "Inspect the public endpoint",
  runbook_url: "https://docs.example.test/runbooks/homepage",
  state: "failing",
  consecutive_failures: 1,
  next_check_at: "2026-09-16T12:02:00Z",
  latest_result_id: "f262d323-0562-4fd1-95e0-5cc80b17a4ec",
  latest_success_id: "74347b5f-0fef-474b-832b-7ed267f55e51",
  open_incident_id: null as string | null,
  headers: [] as Array<{ id: string; name: string; created_at: string; updated_at: string }>,
  version: 1,
  created_at: "2026-09-16T12:00:00Z",
  updated_at: "2026-09-16T12:01:00Z",
  paused_at: null as string | null,
  deleted_at: null as string | null,
};

const latestCheck = {
  id: baseMonitor.latest_result_id,
  sequence: 9,
  trigger: "scheduled",
  scheduled_for: "2026-09-16T12:01:00Z",
  started_at: "2026-09-16T12:01:00Z",
  completed_at: "2026-09-16T12:01:01Z",
  outcome: "failure",
  failure_reason: "unexpected_status",
  status_code: 503,
  response_time_milliseconds: 321,
  effective_url: baseMonitor.target_url,
};

const lastSuccess = {
  ...latestCheck,
  id: baseMonitor.latest_success_id,
  sequence: 8,
  completed_at: "2026-09-16T12:00:01Z",
  outcome: "success",
  failure_reason: null,
  status_code: 200,
  response_time_milliseconds: 45,
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
    if (request.method === "GET" && path.endsWith("/maintenance")) return json({ project_key: project.key, scope_type: "http", version: 0, direct_active: false, effective_active: false, active_scopes: [] });
    if (request.method === "GET" && path === "/api/email/deliveries") return json({ items: [], total: 0, limit: 20, offset: 0, has_more: false });
    if (request.method === "GET" && path.endsWith("/email-summary")) return json({ project_key: project.key, pending_count: 0, retrying_count: 0, terminal_failure_count: 0, smtp_accepted_count: 0 });
    return answer(request);
  });
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

function history(request: Request) {
  const path = new URL(request.url).pathname;
  if (path.endsWith("/checks")) {
    return json({ items: [latestCheck, lastSuccess], next_before_sequence: null });
  }
  if (path.endsWith("/incidents")) {
    return json({ items: [], next_before_opening_sequence: null });
  }
  return undefined;
}

describe("HTTP monitor administration", () => {
  afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });

  it("shows purpose as text and lets an existing monitor clear it", async () => {
    let current = { ...baseMonitor, purpose: "<b>Checks the homepage</b>" };
    answering(async (request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/http-monitors/homepage") && request.method === "GET") return json(current);
      if (path.endsWith("/http-monitors/homepage") && request.method === "PUT") {
        expect(await request.json()).toMatchObject({ purpose: null, version: 1, target_url: null });
        current = { ...current, purpose: "", version: 2 };
        return json({ ...current, purpose: null });
      }
      return history(request) ?? json({ code: "not_found", status: 404, title: "ignored" }, 404);
    });
    render(<MonitorsView onBack={vi.fn()} onOpenPush={vi.fn()} onSignedOut={vi.fn()}
      project={project} routeMonitorKey="homepage" />);
    expect(await screen.findByText("<b>Checks the homepage</b>", { selector: ".monitor-purpose" })).toBeInTheDocument();
    expect(document.querySelector(".monitor-purpose b")).toBeNull();
    const user = userEvent.setup();
    await user.clear(screen.getByLabelText("Purpose (optional)"));
    await user.click(screen.getByRole("button", { name: "Save configuration" }));
    expect(await screen.findByText(/Purpose not documented/, { selector: ".monitor-purpose" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Add purpose" })).toHaveAttribute("href", "#configuration-title");
  });

  it("loads old pointer evidence and a deep-linked incident beyond the first history page", async () => {
    const incidentId = "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901";
    const oldSuccess = { ...lastSuccess, sequence: 1, applied_to_current_state: true };
    const late = { ...latestCheck, id: "44aa2e91-a7b4-4efd-8323-3c2d84445593", sequence: 10,
      response_time_milliseconds: null, status_code: null, applied_to_current_state: false };
    const oldIncident = { id: incidentId, first_failure_check_id: latestCheck.id,
      opening_check_id: latestCheck.id, latest_failure_check_id: latestCheck.id,
      resolution_check_id: null, first_failure_sequence: 9, opening_sequence: 9,
      latest_failure_sequence: 9, resolution_sequence: null, began_at: latestCheck.completed_at,
      opened_at: latestCheck.completed_at, last_observed_at: latestCheck.completed_at,
      resolved_at: null, original_reason: "unexpected_status", latest_reason: "unexpected_status" };
    window.history.replaceState({}, "", `/projects/public-site/http-monitors/homepage?incident=${incidentId}`);
    const fetch = answering((request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/http-monitors/homepage")) return json({ ...baseMonitor, open_incident_id: incidentId });
      if (path.endsWith("/checks")) return json({ items: [late, { ...latestCheck, applied_to_current_state: true }], next_before_sequence: 9 });
      if (path.endsWith(`/checks/${oldSuccess.id}`)) return json(oldSuccess);
      if (path.endsWith("/incidents")) return json({ items: [], next_before_opening_sequence: 9 });
      if (path === `/api/email/incidents/${incidentId}`) return json({ incident_id: incidentId,
        announcement_state: "announced", suppression_reason: null, deliveries: [] });
      if (path.endsWith(`/incidents/${incidentId}`)) return json(oldIncident);
      throw new Error(`Unexpected ${path}`);
    });
    render(<MonitorsView onBack={vi.fn()} onOpenPush={vi.fn()} onSignedOut={vi.fn()}
      project={project} routeMonitorKey="homepage" />);
    expect(await screen.findByRole("heading", { name: "Homepage", level: 1 })).toBeInTheDocument();
    expect(await screen.findByText(/Began .*latest observation.*reason unexpected_status/, { selector: "dd" })).toBeInTheDocument();
    expect(screen.getByText(/Last success/, { selector: "dt" }).nextElementSibling).toHaveTextContent("2026");
    expect(screen.getByText("retained, not applied", { exact: false })).toBeInTheDocument();
    expect(screen.getByText("not available", { exact: false })).toBeInTheDocument();
    expect(await screen.findByRole("region", { name: "Selected incident" })).toHaveTextContent("latest reason unexpected_status");
    expect(await screen.findByText("Announcement: announced")).toBeInTheDocument();
    expect(fetch.mock.calls.some(([request]) => new URL((request as Request).url).pathname.endsWith(`/checks/${oldSuccess.id}`))).toBe(true);
    expect(screen.queryByText(oldSuccess.id)).not.toBeInTheDocument();
  });

  it("renders every lifecycle state explicitly and opens detail from the keyboard", async () => {
    const list = deferred<Response>();
    const monitors = [
      { ...baseMonitor, id: "00000000-0000-0000-0000-000000000001", key: "untested", name: "Untested", state: "untested", consecutive_failures: 0, latest_result_id: null, latest_success_id: null, has_target_query: false },
      { ...baseMonitor, id: "00000000-0000-0000-0000-000000000002", key: "healthy", name: "Healthy", state: "healthy", consecutive_failures: 0, open_incident_id: null, has_target_query: false },
      { ...baseMonitor, id: "00000000-0000-0000-0000-000000000003", key: "warning", name: "Warning", state: "failing", consecutive_failures: 1, open_incident_id: null },
      { ...baseMonitor, id: "00000000-0000-0000-0000-000000000004", key: "incident", name: "Incident", state: "failing", consecutive_failures: 3, open_incident_id: "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901" },
      { ...baseMonitor, id: "00000000-0000-0000-0000-000000000005", key: "paused", name: "Paused", state: "paused", open_incident_id: "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901", paused_at: "2026-09-16T12:04:00Z", next_check_at: null },
    ];
    answering((request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/http-monitors")) return list.promise;
      if (path.endsWith("/http-monitors/untested")) return json(monitors[0]);
      return history(request) ?? json({ code: "not_found", title: "ignored", status: 404 }, 404);
    });
    render(<MonitorsView onBack={vi.fn()} onOpenPush={vi.fn()} onSignedOut={vi.fn()} project={project} />);

    expect(screen.getByRole("status")).toHaveTextContent("Loading monitors");
    list.resolve(json(monitors));
    expect(await screen.findByText("Untested", { selector: ".state" })).toBeInTheDocument();
    expect(screen.getByText("Healthy", { selector: ".state" })).toBeInTheDocument();
    expect(screen.getByText("Failing below threshold · 1/3 failures")).toBeInTheDocument();
    expect(screen.getByText("Incident open · 3/3 failures")).toBeInTheDocument();
    expect(screen.getByText("Paused · incident remains open")).toBeInTheDocument();

    const open = screen.getAllByRole("button", { name: "Open details" })[0];
    open.focus();
    await userEvent.keyboard("{Enter}");
    expect(await screen.findByRole("heading", { name: "Untested", level: 1 })).toBeInTheDocument();
  });

  it("creates complete configuration from the form and clears submitted header secrets immediately", async () => {
    const submitted = deferred<Response>();
    let stored: typeof baseMonitor | undefined;
    answering(async (request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/http-monitors") && request.method === "GET") return json(stored ? [stored] : []);
      if (path.endsWith("/http-monitors") && request.method === "POST") {
        expect(request.headers.get("X-Upaffe-CSRF")).toBe("1");
        expect(await request.json()).toEqual({
          key: "homepage",
          name: "Homepage",
          purpose: "Checks the public homepage.",
          target_url: "https://status.example.test/health?access=secret-query",
          expected_status_code: 204,
          text_condition: "forbidden",
          text_fragment: "maintenance",
          interval_seconds: 120,
          timeout_seconds: 15,
          failure_threshold: 2,
          instruction: "Inspect it",
          runbook_url: "https://docs.example.test/runbook",
          headers: [{ name: "Authorization", value: "Bearer submitted-secret" }],
        });
        stored = { ...baseMonitor, purpose: "Checks the public homepage." };
        return submitted.promise;
      }
      if (path.endsWith("/http-monitors/homepage")) return json(stored);
      return history(request) ?? json({ code: "not_found", title: "ignored", status: 404 }, 404);
    });
    render(<MonitorsView onBack={vi.fn()} onOpenPush={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();
    await screen.findByText("No HTTP monitors yet.");

    await user.type(screen.getByLabelText("Immutable key"), "homepage");
    await user.type(screen.getByLabelText("Display name"), "Homepage");
    await user.type(screen.getByLabelText("Purpose (optional)"), "Checks the public homepage.");
    await user.type(screen.getByLabelText("Target URL"), "https://status.example.test/health?access=secret-query");
    await user.clear(screen.getByLabelText("Expected status"));
    await user.type(screen.getByLabelText("Expected status"), "204");
    await user.selectOptions(screen.getByLabelText("Text condition"), "forbidden");
    await user.type(screen.getByLabelText("Text fragment"), "maintenance");
    await user.clear(screen.getByLabelText("Interval (seconds)"));
    await user.type(screen.getByLabelText("Interval (seconds)"), "120");
    await user.clear(screen.getByLabelText("Timeout (seconds)"));
    await user.type(screen.getByLabelText("Timeout (seconds)"), "15");
    await user.clear(screen.getByLabelText("Failures before incident"));
    await user.type(screen.getByLabelText("Failures before incident"), "2");
    await user.type(screen.getByLabelText("Operator instruction"), "Inspect it");
    await user.type(screen.getByLabelText("Runbook URL"), "https://docs.example.test/runbook");
    await user.click(screen.getByRole("button", { name: "Add secret header" }));
    await user.type(screen.getByLabelText("Header 1 name"), "Authorization");
    await user.type(screen.getByLabelText("Header 1 value"), "Bearer submitted-secret");
    await user.click(screen.getByRole("button", { name: "Create monitor" }));

    expect(screen.getByLabelText("Header 1 value")).toHaveValue("");
    expect(screen.getByLabelText("Target URL")).toHaveValue("");
    submitted.resolve(json(stored, 201));
    expect(await screen.findByRole("heading", { name: "Homepage", level: 1 })).toBeInTheDocument();
    expect(screen.getByText("Checks the public homepage.", { selector: ".monitor-purpose" })).toBeInTheDocument();
    expect(screen.queryByDisplayValue("Bearer submitted-secret")).not.toBeInTheDocument();
    expect(screen.queryByText("secret-query")).not.toBeInTheDocument();
  });

  it("uses the shared contract for detail, history, lifecycle, edits, headers, and removal", async () => {
    let current: Omit<typeof baseMonitor, "next_check_at"> & { next_check_at: string | null } = { ...baseMonitor };
    const headerResponse = deferred<Response>();
    const operations: string[] = [];
    answering(async (request) => {
      const url = new URL(request.url);
      const path = url.pathname;
      if (path.endsWith("/http-monitors") && request.method === "GET") return json([current]);
      if (path.endsWith("/checks") && request.method === "GET") {
        if (url.searchParams.has("before_sequence")) {
          operations.push("older-checks");
          return json({ items: [], next_before_sequence: null });
        }
        return json({ items: [latestCheck, lastSuccess], next_before_sequence: 8 });
      }
      if (path.endsWith("/incidents") && request.method === "GET") return json({
        items: [{
          id: "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901",
          first_failure_check_id: latestCheck.id,
          opening_check_id: latestCheck.id,
          latest_failure_check_id: latestCheck.id,
          resolution_check_id: lastSuccess.id,
          first_failure_sequence: 9,
          opening_sequence: 9,
          latest_failure_sequence: 9,
          resolution_sequence: 10,
          began_at: latestCheck.completed_at,
          opened_at: latestCheck.completed_at,
          last_observed_at: lastSuccess.completed_at,
          resolved_at: lastSuccess.completed_at,
          original_reason: "unexpected_status",
          latest_reason: "unexpected_status",
        }],
        next_before_opening_sequence: null,
      });
      if (path.endsWith("/http-monitors/homepage") && request.method === "GET") return json(current);

      expect(request.headers.get("X-Upaffe-CSRF")).toBe("1");
      if (path.endsWith("/pause")) {
        expect(await request.json()).toEqual({ version: 1 });
        operations.push("pause");
        current = { ...current, state: "paused", version: 2, paused_at: "2026-09-16T12:05:00Z", next_check_at: null };
        return json(current);
      }
      if (path.endsWith("/resume")) {
        expect(await request.json()).toEqual({ version: 2 });
        operations.push("resume");
        current = { ...current, state: "untested", consecutive_failures: 0, version: 3, paused_at: null, next_check_at: "2026-09-16T12:06:00Z" };
        return json(current);
      }
      if (path.endsWith("/test")) {
        operations.push("test");
        current = { ...current, state: "healthy", version: 4, latest_result_id: lastSuccess.id, latest_success_id: lastSuccess.id };
        return json({ check_id: lastSuccess.id, applied_to_current_state: true, succeeded: true, reason_code: null, message: "succeeded", status_code: 200, response_time_milliseconds: 45, effective_url: current.target_url, monitor: current });
      }
      if (path.endsWith("/headers/X-Api-Key") && request.method === "PUT") {
        expect(await request.json()).toEqual({ value: "header-secret", version: 5 });
        operations.push("set-header");
        current = { ...current, version: 6, headers: [{ id: "ea634209-c0de-4487-b68a-e33fefc70ba7", name: "X-Api-Key", created_at: current.created_at, updated_at: current.updated_at }] };
        return headerResponse.promise;
      }
      if (path.endsWith("/headers/X-Api-Key") && request.method === "DELETE") {
        expect(url.searchParams.get("version")).toBe("6");
        operations.push("remove-header");
        current = { ...current, version: 7, headers: [] };
        return json(current);
      }
      if (path.endsWith("/http-monitors/homepage") && request.method === "PUT") {
        const body = await request.json();
        expect(body).toMatchObject({ name: "Homepage status", purpose: "Checks the site.", target_url: null, version: 4 });
        operations.push("update");
        current = { ...current, name: "Homepage status", purpose: "Checks the site.", version: 5 };
        return json(current);
      }
      if (path.endsWith("/http-monitors/homepage") && request.method === "DELETE") {
        expect(url.searchParams.get("version")).toBe("7");
        operations.push("remove");
        return json({ ...current, deleted_at: "2026-09-16T12:10:00Z", version: 8 });
      }
      throw new Error(`Unexpected ${request.method} ${path}`);
    });
    render(<MonitorsView onBack={vi.fn()} onOpenPush={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();
    await screen.findByText("Failing below threshold · 1/3 failures");
    const open = screen.getByRole("button", { name: "Open details" });
    open.focus();
    await user.keyboard("{Enter}");

    expect(await screen.findByText("321 ms")).toBeInTheDocument();
    expect(screen.getByText("unexpected_status")).toBeInTheDocument();
    expect(screen.getByText("Resolved incident")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open runbook" })).toHaveAttribute("href", baseMonitor.runbook_url);
    await user.click(screen.getByRole("button", { name: "Load older checks" }));

    await user.click(screen.getByRole("button", { name: "Pause" }));
    expect(await screen.findByText("Monitor paused.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Resume" }));
    expect(await screen.findByText(/fresh check is due/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Run test now" }));
    expect(await screen.findByText(/Immediate check: success/)).toBeInTheDocument();

    const name = screen.getByLabelText("Display name");
    await user.clear(name);
    await user.type(name, "Homepage status");
    await user.type(screen.getByLabelText("Purpose (optional)"), "Checks the site.");
    expect(screen.getByLabelText(/Replace the complete target URL/)).not.toBeChecked();
    await user.click(screen.getByRole("button", { name: "Save configuration" }));
    expect(await screen.findByText("Configuration saved.")).toBeInTheDocument();
    expect(screen.getByText("Checks the site.", { selector: ".monitor-purpose" })).toBeInTheDocument();

    await user.type(screen.getByLabelText("Header name"), "X-Api-Key");
    await user.type(screen.getByLabelText("New secret value"), "header-secret");
    await user.click(screen.getByRole("button", { name: "Set or replace header" }));
    expect(screen.getByLabelText("New secret value")).toHaveValue("");
    headerResponse.resolve(json(current));
    expect(await screen.findByText(/Header X-Api-Key was stored/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Remove" }));
    expect(await screen.findByText("Header X-Api-Key was removed.")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Remove monitor…" }));
    const confirmation = screen.getByRole("group", { name: "Confirm monitor removal" });
    expect(within(confirmation).getByText(/Remove Homepage status/)).toBeInTheDocument();
    await user.click(within(confirmation).getByRole("button", { name: "Confirm removal" }));
    expect(await screen.findByRole("heading", { name: "Monitors" })).toBeInTheDocument();
    expect(operations).toEqual(["older-checks", "pause", "resume", "test", "update", "set-header", "remove-header", "remove"]);
  });

  it("keeps a conflict visible while refreshing the concurrent monitor version", async () => {
    let detailReads = 0;
    answering(async (request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith("/http-monitors") && request.method === "GET") return json([baseMonitor]);
      if (path.endsWith("/http-monitors/homepage") && request.method === "GET") {
        detailReads += 1;
        return json(detailReads === 1 ? baseMonitor : { ...baseMonitor, version: 2, name: "Changed elsewhere" });
      }
      if (path.endsWith("/pause")) return json({ code: "conflict", title: "must-not-render", status: 409 }, 409);
      return history(request) ?? json({ code: "not_found", title: "ignored", status: 404 }, 404);
    });
    render(<MonitorsView onBack={vi.fn()} onOpenPush={vi.fn()} onSignedOut={vi.fn()} project={project} />);
    const user = userEvent.setup();
    await screen.findByRole("button", { name: "Open details" });
    await user.click(screen.getByRole("button", { name: "Open details" }));
    await screen.findByRole("heading", { name: "Homepage", level: 1 });
    await user.click(screen.getByRole("button", { name: "Pause" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("conflicts with the current state");
    expect(alert).not.toHaveTextContent("must-not-render");
    expect(await screen.findByRole("heading", { name: "Changed elsewhere", level: 1 })).toBeInTheDocument();
    expect(screen.getByText("2", { selector: "dd" })).toBeInTheDocument();
  });
});
