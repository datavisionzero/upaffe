import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { DeliveryHistoryPanel, IncidentEmailPanel, MaintenancePanel } from "@/shell/EmailPanels";
import { InstanceEmailView, ProjectEmailView } from "@/shell/EmailSettingsView";

const project = {
  id: "f0187842-6f73-4c54-8a8c-57ac7c117c39", key: "systems", name: "Systems",
  version: 1, created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null,
};
const incidentId = "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901";
const delivery = {
  id: "ee8469cb-8a75-4952-99e2-f3289d8ab5cb", incident_id: incidentId,
  kind: "alert", recipient: "ops@example.test", project_key: "systems", monitor_type: "http",
  monitor_key: "site", state: "terminal_failure", attempt_count: 5,
  last_error_code: "smtp_timeout", created_at: "2026-09-19T12:00:00Z",
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status, headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json" },
  });
}

function answering(answer: (request: Request) => Response | Promise<Response>) {
  const fetch = vi.fn<typeof globalThis.fetch>(async (input) => answer(input as Request));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

describe("email and maintenance administration", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("replaces a password explicitly and reports test SMTP acceptance without revealing the secret", async () => {
    const settings = { version: 2, host: "mail.example.test", port: 587, security: "starttls", sender_address: "notify@example.test", sender_name: "Monitor", public_base_url: "https://status.example.test", username: "smtp-user", has_password: false, default_recipients: [] };
    const requests: Request[] = [];
    answering(async (request) => {
      requests.push(request);
      const path = new URL(request.url).pathname;
      if (path === "/api/email/settings") return json(settings);
      if (path === "/api/email/deliveries") return json({ items: [], total: 0, limit: 20, offset: 0, has_more: false });
      if (path === "/api/email/deliveries/summary") return json({ pending_count: 0, retrying_count: 0, terminal_failure_count: 0, smtp_accepted_count: 0 });
      if (path === "/api/email/password") return json({ ...settings, version: 3, has_password: true });
      if (path === "/api/email/test") return json({ status: "accepted_by_smtp", accepted_at: "2026-09-19T12:00:00Z" });
      return json({ code: "not_found", status: 404 }, 404);
    });
    const user = userEvent.setup();
    render(<InstanceEmailView onBack={vi.fn()} onSignedOut={vi.fn()} />);
    await screen.findByText("Password: not configured.", { exact: false });
    await user.type(screen.getByLabelText("New SMTP password"), "private-smtp-password");
    await user.click(screen.getByRole("button", { name: "Replace password" }));
    expect(await screen.findByText("Password: configured.", { exact: false })).toBeInTheDocument();
    expect(screen.getByText("SMTP password replaced.")).toBeInTheDocument();
    expect(screen.getByLabelText("New SMTP password")).toHaveValue("");
    expect(document.body.textContent).not.toContain("private-smtp-password");
    const passwordRequest = requests.find((request) => new URL(request.url).pathname === "/api/email/password")!;
    expect(passwordRequest.headers.get("X-Upaffe-CSRF")).toBe("1");
    expect(await passwordRequest.clone().json()).toEqual({ version: 2, password: "private-smtp-password" });
    await user.type(screen.getByLabelText("Test recipient"), "ops@example.test");
    await user.click(screen.getByRole("button", { name: "Send test email" }));
    expect(await screen.findByText("Accepted by SMTP. Inbox delivery is not confirmed.")).toBeInTheDocument();
  });

  it("refreshes the settings version and keeps a conflict visible", async () => {
    let reads = 0;
    const base = { host: "mail.example.test", port: 587, security: "starttls", sender_address: "notify@example.test", sender_name: "Monitor", public_base_url: "https://status.example.test", username: null, has_password: false, default_recipients: [] };
    answering((request) => {
      const path = new URL(request.url).pathname;
      if (path === "/api/email/settings" && request.method === "GET") return json({ ...base, version: ++reads });
      if (path === "/api/email/deliveries") return json({ items: [], total: 0, limit: 20, offset: 0, has_more: false });
      if (path === "/api/email/settings" && request.method === "PUT") return json({ code: "conflict", status: 409, title: "untrusted relay detail" }, 409);
      if (path === "/api/email/deliveries/summary") return json({ pending_count: 0, retrying_count: 0, terminal_failure_count: 0, smtp_accepted_count: 0 });
      return json({ code: "not_found", status: 404 }, 404);
    });
    const user = userEvent.setup();
    render(<InstanceEmailView onBack={vi.fn()} onSignedOut={vi.fn()} />);
    await screen.findByText("Version 1.", { exact: false });
    await user.click(screen.getByRole("button", { name: "Save SMTP settings" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Refresh and try again");
    expect(screen.getByText("Version 2.", { exact: false })).toBeInTheDocument();
    expect(document.body.textContent).not.toContain("untrusted relay detail");
  });

  it("edits project recipients and shows project maintenance separately from health", async () => {
    const requests: Request[] = [];
    answering(async (request) => {
      requests.push(request);
      const path = new URL(request.url).pathname;
      if (path.endsWith("/recipients")) return json({ project_key: "systems", version: request.method === "PUT" ? 2 : 1, recipients: ["ops@example.test"] });
      if (path.endsWith("/maintenance")) return json({ project_key: "systems", scope_type: "project", version: request.method === "POST" ? 1 : 0, direct_active: request.method === "POST", effective_active: request.method === "POST", effective_ends_at: new Date(Date.now() + 3600000).toISOString(), ends_at: new Date(Date.now() + 3600000).toISOString(), active_scopes: request.method === "POST" ? ["project"] : [] });
      if (path === "/api/email/deliveries") return json({ items: [], total: 0, limit: 20, offset: 0, has_more: false });
      if (path.endsWith("/email-summary")) return json({ project_key: "systems", pending_count: 0, retrying_count: 0, terminal_failure_count: 0, smtp_accepted_count: 0 });
      return json({ code: "not_found", status: 404 }, 404);
    });
    const user = userEvent.setup();
    render(<ProjectEmailView project={project} onBack={vi.fn()} onSignedOut={vi.fn()} />);
    await screen.findByText("Version 1");
    await user.clear(screen.getByLabelText("One address per line"));
    await user.type(screen.getByLabelText("One address per line"), "ops@example.test\nbackup@example.test");
    await user.click(screen.getByRole("button", { name: "Save project recipients" }));
    expect(await screen.findByText("Project recipients saved.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Start maintenance" }));
    expect(await screen.findByText("Effective: Active", { exact: false })).toBeInTheDocument();
    expect(screen.getByText("Monitoring continues.", { exact: false })).toBeInTheDocument();
    const start = requests.find((request) => request.method === "POST" && new URL(request.url).pathname.endsWith("/maintenance"))!;
    expect(await start.clone().json()).toEqual({ version: 0, duration_seconds: 3600 });
    expect(start.headers.get("X-Upaffe-CSRF")).toBe("1");
  });

  it("uses monitor scope and displays expired effective maintenance as inactive", async () => {
    const past = new Date(Date.now() - 60000).toISOString();
    const calls: string[] = [];
    answering((request) => {
      calls.push(new URL(request.url).pathname);
      return json({ project_key: "systems", monitor_key: "site", scope_type: "http", version: 2, direct_active: true, effective_active: true, ends_at: past, effective_ends_at: past, active_scopes: ["project", "http"] });
    });
    render(<MaintenancePanel projectKey="systems" monitorType="http" monitorKey="site" onSignedOut={vi.fn()} />);
    const panel = await screen.findByRole("region", { name: "HTTP monitor maintenance" });
    expect(await within(panel).findByText("Effective: Inactive")).toBeInTheDocument();
    expect(within(panel).getByText("Direct scope: inactive", { exact: false })).toBeInTheDocument();
    expect(calls).toContain("/api/projects/systems/http-monitors/site/maintenance");
  });

  it("starts, extends, and ends direct monitor maintenance after a concurrent change", async () => {
    const future = new Date(Date.now() + 3600000).toISOString();
    const requests: Request[] = [];
    let version = 0;
    let active = false;
    let writes = 0;
    answering((request) => {
      requests.push(request);
      if (request.method === "POST") {
        writes += 1;
        if (writes === 2) { version = 2; return json({ code: "conflict", title: "raw relay diagnostic" }, 409); }
        version += 1;
        active = true;
      }
      if (request.method === "DELETE") { version += 1; active = false; }
      return json({ project_key: "systems", monitor_key: "site", scope_type: "http", version,
        direct_active: active, effective_active: true, ends_at: active ? future : null,
        effective_ends_at: future, active_scopes: active ? ["project", "http"] : ["project"] });
    });
    const user = userEvent.setup();
    render(<MaintenancePanel projectKey="systems" monitorType="http" monitorKey="site" onSignedOut={vi.fn()} />);
    const panel = await screen.findByRole("region", { name: "HTTP monitor maintenance" });
    expect(await within(panel).findByText("Direct scope: inactive", { exact: false })).toBeInTheDocument();
    expect(within(panel).getByText("Effective: Active", { exact: false })).toBeInTheDocument();
    await user.click(within(panel).getByRole("button", { name: "Start maintenance" }));
    expect(await within(panel).findByRole("button", { name: "Extend maintenance" })).toBeInTheDocument();
    await user.click(within(panel).getByRole("button", { name: "Extend maintenance" }));
    expect(await within(panel).findByRole("alert")).toHaveTextContent("Refresh and try again");
    expect(within(panel).getByText("version 2", { exact: false })).toBeInTheDocument();
    expect(panel.textContent).not.toContain("raw relay diagnostic");
    await user.click(within(panel).getByRole("button", { name: "Extend maintenance" }));
    expect(await within(panel).findByText("Maintenance window saved.", { exact: false })).toBeInTheDocument();
    await user.click(within(panel).getByRole("button", { name: "End direct maintenance" }));
    expect(await within(panel).findByText("Direct maintenance ended.", { exact: false })).toBeInTheDocument();
    expect(within(panel).getByText("Effective: Active", { exact: false })).toBeInTheDocument();
    expect(within(panel).getByText("Direct scope: inactive", { exact: false })).toBeInTheDocument();
    const posts = requests.filter((request) => request.method === "POST");
    expect(await Promise.all(posts.map((request) => request.clone().json()))).toEqual([
      { version: 0, duration_seconds: 3600 }, { version: 1, duration_seconds: 3600 },
      { version: 2, duration_seconds: 3600 },
    ]);
    expect(new URL(requests.find((request) => request.method === "DELETE")!.url).searchParams.get("version")).toBe("3");
  });

  it("shows actionable sanitized delivery failure and incident suppression", async () => {
    answering((request) => {
      const path = new URL(request.url).pathname;
      if (path === "/api/email/deliveries") return json({ items: [delivery], total: 1, limit: 20, offset: 0, has_more: false });
      if (path.endsWith("/email-summary")) return json({ project_key: "systems", pending_count: 0, retrying_count: 0, terminal_failure_count: 1, smtp_accepted_count: 0 });
      if (path.endsWith(incidentId)) return json({ incident_id: incidentId, project_key: "systems", monitor_type: "http", monitor_key: "site", open: true, announcement_state: "suppressed", suppression_reason: "maintenance", deliveries: [delivery] });
      return json({ code: "not_found", status: 404 }, 404);
    });
    render(<><DeliveryHistoryPanel projectKey="systems" monitorType="http" monitorKey="site" onSignedOut={vi.fn()} /><IncidentEmailPanel incidentId={incidentId} monitorType="http" onSignedOut={vi.fn()} /></>);
    const history = await screen.findByRole("region", { name: "Email delivery" });
    expect(await within(history).findByText("terminal failures 1", { exact: false })).toBeInTheDocument();
    expect(within(history).getByText("Failure code smtp_timeout")).toBeInTheDocument();
    expect(within(history).getByRole("link", { name: /Inspect incident and recipient status/ })).toHaveAttribute("href",
      `/projects/systems/http-monitors/site?incident=${incidentId}`);
    const incident = await screen.findByRole("region", { name: "Incident email status" });
    expect(within(incident).getByText("Announcement: suppressed", { exact: false })).toBeInTheDocument();
    expect(document.body.textContent).not.toContain("raw relay diagnostic");
  });
});
