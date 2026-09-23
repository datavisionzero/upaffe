import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";

import { App } from "@/shell/App";

const session = { operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
  email: "operator@example.test", access_path: "browser_session",
  session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d",
  expires_at: "2026-09-23T12:00:00Z" };
const projects = [
  { id: "f0187842-6f73-4c54-8a8c-57ac7c117c39", key: "jobs", name: "Jobs", version: 1,
    created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null },
  { id: "7bd2da16-3d7f-4676-81e0-ef4149971f00", key: "public", name: "Public", version: 1,
    created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null },
];
const items = [
  { id: "4a572f67-2edb-46d3-86de-419608f16d83", project_key: "jobs", project_name: "Jobs",
    key: "backup", name: "Backup", purpose: "Confirms nightly backup completion", type: "push",
    mode: "job_completion", target_url: null, interval_seconds: 3600, tolerance_seconds: 300,
    state: "failing", overdue: true, incident_open: true, maintenance_until: null,
    latest_observation_at: "2026-09-19T12:00:00Z", last_success_at: null,
    next_due_at: "2026-09-19T13:05:00Z" },
  { id: "f71f436f-fba2-40dc-af42-6b15ffb56a12", project_key: "public", project_name: "Public",
    key: "site", name: "Site", purpose: null, type: "http", mode: null,
    target_url: "https://status.example.test/health", interval_seconds: 60, tolerance_seconds: null,
    state: "healthy", overdue: false, incident_open: false, maintenance_until: null,
    latest_observation_at: "2026-09-19T12:00:00Z", last_success_at: "2026-09-19T12:00:00Z",
    next_due_at: "2026-09-19T12:01:00Z" },
];

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
}

afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });

it("loads a linked project inventory and keeps composed filters in browser history", async () => {
  window.history.replaceState({}, "", "/monitors?project=jobs");
  const requests: string[] = [];
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const url = new URL((input as Request).url);
    if (url.pathname === "/api/bootstrap") return json({ required: false, available: false });
    if (url.pathname === "/api/session") return json(session);
    if (url.pathname === "/api/projects") return json(projects);
    if (url.pathname === "/api/monitors") {
      requests.push(url.search);
      const filtered = items.filter((item) =>
        (!url.searchParams.get("project") || item.project_key === url.searchParams.get("project")) &&
        (!url.searchParams.get("state") || item.state === url.searchParams.get("state")) &&
        (!url.searchParams.get("type") || item.type === url.searchParams.get("type")) &&
        (!url.searchParams.get("q") || `${item.name} ${item.purpose}`.toLowerCase().includes(url.searchParams.get("q")!.toLowerCase())));
      return json({ generated_at: "2026-09-19T12:00:00Z", items: filtered, total: filtered.length,
        limit: 25, offset: 0, has_more: false });
    }
    throw new Error(`Unexpected ${url.pathname}`);
  }));
  const user = userEvent.setup();
  render(<App />);
  expect(await screen.findByRole("link", { name: "Backup" })).toHaveAttribute("href", "/projects/jobs/push-monitors/backup");
  expect(screen.queryByRole("link", { name: "Site" })).not.toBeInTheDocument();
  expect(screen.getByRole("combobox", { name: "Project" })).toHaveValue("jobs");
  expect(screen.getByRole("row", { name: /Backup/ })).toHaveTextContent("Confirms nightly backup completion");
  expect(screen.getByRole("row", { name: /Backup/ })).toHaveTextContent("Incident open");
  expect(screen.getByRole("row", { name: /Backup/ })).toHaveTextContent("Overdue");
  expect(screen.getByRole("cell", { name: /Job completion report/ })).toHaveAttribute("data-label", "Method and cadence");
  await user.selectOptions(screen.getByRole("combobox", { name: "State" }), "failing");
  await user.selectOptions(screen.getByRole("combobox", { name: "Type" }), "push");
  await user.type(screen.getByRole("textbox", { name: "Search monitors" }), "backup");
  await user.click(screen.getByRole("button", { name: "Apply filters" }));
  await waitFor(() => expect(new URLSearchParams(window.location.search).get("q")).toBe("backup"));
  expect(new URLSearchParams(window.location.search).get("project")).toBe("jobs");
  expect(new URLSearchParams(window.location.search).get("state")).toBe("failing");
  expect(new URLSearchParams(window.location.search).get("type")).toBe("push");
  await waitFor(() => expect(requests.some((value) => value.includes("q=backup"))).toBe(true));
  window.history.replaceState({}, "", "/monitors?project=public");
  window.dispatchEvent(new PopStateEvent("popstate"));
  expect(await screen.findByRole("link", { name: "Site" })).toHaveAttribute("href", "/projects/public/http-monitors/site");
  expect(screen.getByText("Purpose not documented")).toBeInTheDocument();
  expect(screen.getByRole("combobox", { name: "Project" })).toHaveValue("public");
  expect(screen.queryByText("hidden-query-value")).not.toBeInTheDocument();
  expect(within(screen.getByRole("row", { name: /Site/ })).getByText(/https:\/\/status\.example\.test\/health/)).toBeInTheDocument();
});

it("shows a clear empty state for unmatched filters", async () => {
  window.history.replaceState({}, "", "/monitors?q=absent");
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json(session);
    if (path === "/api/projects") return json(projects);
    if (path === "/api/monitors") return json({ generated_at: "2026-09-19T12:00:00Z", items: [],
      total: 0, limit: 25, offset: 0, has_more: false });
    throw new Error(`Unexpected ${path}`);
  }));
  render(<App />);
  expect(await screen.findByText("No monitors match these filters.")).toBeInTheDocument();
  expect(screen.getByText("No matching monitors")).toBeInTheDocument();
  expect(screen.queryByRole("navigation", { name: "Inventory pages" })).not.toBeInTheDocument();
  await userEvent.setup().click(screen.getByRole("button", { name: "Reset filters" }));
  await waitFor(() => expect(window.location.pathname + window.location.search).toBe("/monitors"));
});

function emptyInventory(projectList: unknown[]) {
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json(session);
    if (path === "/api/projects") return json(projectList);
    if (path === "/api/monitors") return json({ generated_at: "2026-09-19T12:00:00Z", items: [],
      total: 0, limit: 25, offset: 0, has_more: false });
    if (path.endsWith("/report")) return json({});
    throw new Error(`Unexpected ${path}`);
  }));
}

it("sends an instance without projects to project creation", async () => {
  window.history.replaceState({}, "", "/monitors");
  emptyInventory([]);
  render(<App />);
  expect(await screen.findByText(/No projects yet/)).toBeInTheDocument();
  expect(screen.queryByText("No matching monitors")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Previous" })).not.toBeInTheDocument();
  await userEvent.setup().click(screen.getByRole("button", { name: "Create a project" }));
  expect(window.location.pathname).toBe("/projects/new");
});

it("creates the first monitor in a chosen project and type", async () => {
  window.history.replaceState({}, "", "/monitors");
  emptyInventory(projects);
  const user = userEvent.setup();
  render(<App />);
  expect(await screen.findByText(/No monitors configured yet/)).toBeInTheDocument();
  expect(screen.getByText("No monitors")).toBeInTheDocument();
  await user.selectOptions(screen.getByRole("combobox", { name: "Project for the new monitor" }), "public");
  await user.click(screen.getByRole("button", { name: "New push monitor" }));
  expect(window.location.pathname).toBe("/projects/public/new-push-monitor");
});

it("offers creation inside an empty filtered project", async () => {
  window.history.replaceState({}, "", "/monitors?project=jobs");
  emptyInventory(projects);
  render(<App />);
  expect(await screen.findByText("Jobs has no monitors yet.")).toBeInTheDocument();
  await userEvent.setup().click(screen.getByRole("button", { name: "New HTTP monitor" }));
  expect(window.location.pathname).toBe("/projects/jobs/new-http-monitor");
});
