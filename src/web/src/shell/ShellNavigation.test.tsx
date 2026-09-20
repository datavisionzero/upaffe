import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";

import { App } from "./App";

const project = { id: "f0187842-6f73-4c54-8a8c-57ac7c117c39", key: "jobs", name: "Jobs",
  version: 1, created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null };
const report = { generated_at: "2026-09-19T12:00:00Z", project,
  counts: { total: 0, http: 0, push: 0, healthy: 0, failing: 0, untested: 0, paused: 0 },
  attention: [], healthy: [], project_maintenance: null,
  email: { configured: false, recipients: [], delivery: { terminal_failure_count: 0,
    retrying_count: 0, pending_count: 0, smtp_accepted_count: 0 } } };

function json(body: unknown) {
  return new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } });
}

afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState({}, "", "/"); });

it("keeps project context in the URL and restores focus after closing mobile navigation", async () => {
  window.history.replaceState({}, "", "/projects/jobs");
  vi.stubGlobal("matchMedia", vi.fn((query: string) => ({
    matches: query.includes("max-width"), addEventListener: vi.fn(), removeEventListener: vi.fn(),
  })));
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async (input) => {
    const path = new URL((input as Request).url).pathname;
    if (path === "/api/bootstrap") return json({ required: false, available: false });
    if (path === "/api/session") return json({ operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
      email: "operator@example.test", access_path: "browser_session",
      session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d", expires_at: "2026-09-23T12:00:00Z" });
    if (path === "/api/projects") return json([project]);
    if (path === "/api/projects/jobs/report") return json(report);
    throw new Error(`Unexpected ${path}`);
  }));

  const user = userEvent.setup();
  render(<App />);
  expect(await screen.findByRole("heading", { name: "Jobs" })).toBeInTheDocument();
  expect(screen.getByRole("combobox", { name: "Switch project" })).toHaveValue("jobs");
  const toggle = screen.getByRole("button", { name: "Open menu" });
  await user.click(toggle);
  expect(screen.getByRole("dialog", { name: "Navigation" })).toBeInTheDocument();
  await waitFor(() => expect(screen.getByRole("link", { name: "Dashboard" })).toHaveFocus());
  await user.keyboard("{Escape}");
  await waitFor(() => expect(toggle).toHaveFocus());
  expect(screen.queryByRole("dialog", { name: "Navigation" })).not.toBeInTheDocument();

  await user.click(toggle);
  const projectsLink = screen.getByRole("link", { name: "Projects" });
  const href = projectsLink.getAttribute("href")!;
  projectsLink.removeAttribute("href");
  expect(fireEvent.click(projectsLink, { ctrlKey: true })).toBe(true);
  expect(window.location.pathname).toBe("/projects/jobs");
  projectsLink.setAttribute("href", href);
  await user.click(projectsLink);
  expect(window.location.pathname).toBe("/projects");
  act(() => window.history.back());
  await waitFor(() => expect(window.location.pathname).toBe("/projects/jobs"));
  expect(await screen.findByRole("combobox", { name: "Switch project" })).toHaveValue("jobs");
});
