import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { App } from "@/shell/App";

const session = {
  operator_id: "3a5ccfce-eb16-4b19-8716-49f08cc44fbc",
  email: "operator@example.test",
  access_path: "browser_session",
  session_id: "57691661-6c2a-4a46-87aa-551b1178fc0d",
  expires_at: "2026-09-23T12:00:00Z",
};

const project = {
  id: "f0187842-6f73-4c54-8a8c-57ac7c117c39",
  key: "backup-jobs",
  name: "Backup jobs",
  version: 1,
  created_at: "2026-09-16T12:00:00Z",
  updated_at: "2026-09-16T12:00:00Z",
  deleted_at: null as string | null,
};

describe("the access and project application", () => {
  beforeEach(() => window.history.replaceState({}, "", "/projects"));
  afterEach(() => vi.unstubAllGlobals());

  function answering(answer: (request: Request) => Promise<Response> | Response) {
    const fetch = vi.fn<typeof globalThis.fetch>(async (input) => answer(input as Request));
    vi.stubGlobal("fetch", fetch);
    return fetch;
  }

  function json(body: unknown, status = 200) {
    return new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json" },
    });
  }

  function deferred<T>() {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>((done) => {
      resolve = done;
    });
    return { promise, resolve };
  }

  it("directs a fresh instance to local setup and can detect its completion", async () => {
    const bootstrap = deferred<Response>();
    let initialized = false;
    const fetch = answering(() => initialized ? json({ required: false }) : bootstrap.promise);
    render(<App />);
    expect(screen.getByRole("status")).toHaveTextContent("Opening the instance");

    bootstrap.resolve(json({ required: true }));
    expect(await screen.findByRole("heading", { name: "Establish the operator" })).toBeInTheDocument();
    expect(screen.getByText(/Run the local bootstrap command/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "production startup guide" })).toHaveAttribute(
      "href", "https://github.com/datavisionzero/upaffe/blob/main/docs/operations.md#production-compose-startup",
    );
    const user = userEvent.setup();
    initialized = true;
    fetch.mockImplementation(async (input) => {
      const path = new URL((input as Request).url).pathname;
      return path === "/api/bootstrap" ? json({ required: false }) :
        json({ code: "authentication_required", status: 401, title: "ignored" }, 401);
    });
    await user.click(screen.getByRole("button", { name: "Check again" }));
    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });

  it("routes an initialized anonymous operator through sign-in and clears the password", async () => {
    let signedIn = false;
    const admitted = deferred<Response>();
    answering(async (request) => {
      const path = new URL(request.url).pathname;
      if (path === "/api/bootstrap") return json({ required: false, available: false });
      if (path === "/api/session" && request.method === "GET") {
        return signedIn ? json(session) : json({ code: "authentication_required", status: 401, title: "ignored" }, 401);
      }
      if (path === "/api/session" && request.method === "POST") {
        expect(await request.json()).toEqual({ email: session.email, password: "a long password" });
        signedIn = true;
        return admitted.promise;
      }
      if (path === "/api/projects") return json([]);
      throw new Error(`Unexpected ${request.method} ${path}`);
    });
    render(<App />);
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "Sign in" });
    await user.type(screen.getByLabelText("Email"), session.email);
    await user.type(screen.getByLabelText("Password"), "a long password");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(screen.getByLabelText("Password")).toHaveValue("");
    admitted.resolve(new Response(null, { status: 204 }));
    expect(await screen.findByRole("heading", { name: "Projects" })).toBeInTheDocument();
    expect(await screen.findByText(/No live projects yet/)).toBeInTheDocument();
  });

  it("manages the complete project lifecycle by contract with keyboard-accessible controls", async () => {
    let stored: typeof project | undefined;
    answering(async (request) => {
      const url = new URL(request.url);
      if (url.pathname === "/api/bootstrap") return json({ required: false, available: false });
      if (url.pathname === "/api/session" && request.method === "GET") return json(session);
      if (url.pathname === "/api/projects" && request.method === "GET") {
        const wantsDeleted = url.searchParams.get("deleted") === "true";
        return json(stored && (stored.deleted_at !== null) === wantsDeleted ? [stored] : []);
      }
      expect(request.headers.get("X-Upaffe-CSRF")).toBe("1");
      if (url.pathname === "/api/session" && request.method === "DELETE") {
        return new Response(null, { status: 204 });
      }
      if (url.pathname === "/api/projects" && request.method === "POST") {
        expect(await request.json()).toEqual({ key: "backup-jobs", name: "Backup jobs" });
        stored = { ...project };
        return json(stored, 201);
      }
      if (url.pathname === "/api/projects/backup-jobs/report") {
        return json({ generated_at: "2026-09-16T12:00:00Z", project: stored,
          counts: { total: 0, http: 0, push: 0, healthy: 0, failing: 0, untested: 0, paused: 0 },
          attention: [], healthy: [], project_maintenance: null,
          email: { configured: false, host: null, port: null, security: null, sender_address: null,
            public_base_url: null, has_password: false, recipients: [],
            delivery: { project_key: "backup-jobs", pending_count: 0, retrying_count: 0,
              terminal_failure_count: 0, smtp_accepted_count: 0, oldest_pending_at: null } } });
      }
      if (url.pathname === "/api/projects/backup-jobs" && request.method === "PUT") {
        expect(await request.json()).toEqual({ name: "Backups", version: 1 });
        stored = { ...stored!, name: "Backups", version: 2 };
        return json(stored);
      }
      if (url.pathname === "/api/projects/backup-jobs" && request.method === "DELETE") {
        expect(url.searchParams.get("version")).toBe("2");
        stored = { ...stored!, version: 3, deleted_at: "2026-09-16T13:00:00Z" };
        return json(stored);
      }
      if (url.pathname === "/api/projects/backup-jobs/restore" && request.method === "POST") {
        expect(await request.json()).toEqual({ version: 3 });
        stored = { ...stored!, version: 4, deleted_at: null };
        return json(stored);
      }
      throw new Error(`Unexpected ${request.method} ${url.pathname}`);
    });
    render(<App />);
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "Projects" });
    await screen.findByText(/No live projects yet/);

    await user.click(screen.getByRole("button", { name: "New project" }));
    expect(await screen.findByRole("heading", { name: "New project" })).toBeInTheDocument();
    await user.type(screen.getByLabelText("Immutable key"), "backup-jobs");
    await user.type(screen.getByLabelText("Display name"), "Backup jobs");
    await user.click(screen.getByRole("button", { name: "Create project" }));
    expect(await screen.findByRole("heading", { name: "Backup jobs" })).toBeInTheDocument();
    await user.click(within(screen.getByRole("navigation", { name: "Primary" }))
      .getByRole("link", { name: "Projects" }));
    expect(await screen.findByText("backup-jobs")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Rename" }));
    const rename = screen.getByLabelText("New display name for backup-jobs");
    await user.clear(rename);
    await user.type(rename, "Backups{Enter}");
    expect(await screen.findByRole("heading", { name: "Backups" })).toBeInTheDocument();
    expect(screen.getByText("version 2")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Delete" }));
    await user.click(await screen.findByRole("button", { name: "Delete project" }));
    expect(await screen.findByText(/No live projects yet/)).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Project state"), "deleted");
    expect(await screen.findByRole("button", { name: "Restore" })).toBeInTheDocument();
    expect(screen.getByText("version 3")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Restore" }));
    expect(await screen.findByText("No deleted projects.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: `Account: ${session.email}` }));
    await user.click(await screen.findByRole("menuitem", { name: "Sign out" }));
    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });

  it("keeps project inventory state in the URL and protects an unfinished project", async () => {
    window.history.replaceState({}, "", "/projects?q=back&state=deleted&sort=updated&order=desc");
    const older = { ...project, id: "5a757bab-4cce-41de-8b87-11400e8bf021", key: "archive",
      name: "Archive", updated_at: "2026-09-14T12:00:00Z", deleted_at: "2026-09-17T12:00:00Z" };
    const newer = { ...project, name: "Backup jobs", updated_at: "2026-09-18T12:00:00Z",
      deleted_at: "2026-09-19T12:00:00Z" };
    answering((request) => {
      const url = new URL(request.url);
      if (url.pathname === "/api/bootstrap") return json({ required: false, available: false });
      if (url.pathname === "/api/session") return json(session);
      if (url.pathname === "/api/projects") {
        return json(url.searchParams.get("deleted") === "true" ? [older, newer] : []);
      }
      throw new Error(`Unexpected ${request.method} ${url.pathname}`);
    });
    render(<App />);
    const user = userEvent.setup();
    expect(await screen.findByText("backup-jobs")).toBeInTheDocument();
    expect(screen.queryByText("archive")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Search projects")).toHaveValue("back");
    expect(screen.getByLabelText("Project state")).toHaveValue("deleted");
    expect(screen.getByLabelText("Sort projects")).toHaveValue("updated");
    expect(screen.getByLabelText("Sort order")).toHaveValue("desc");

    await user.click(screen.getByRole("button", { name: "New project" }));
    await user.type(await screen.findByLabelText("Immutable key"), "unfinished");
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(await screen.findByRole("dialog", { name: "Discard new project?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Keep editing" }));
    expect(screen.getByLabelText("Immutable key")).toHaveValue("unfinished");
    await user.click(screen.getByLabelText("Immutable key"));
    await user.keyboard("{Escape}");
    await user.click(await screen.findByRole("button", { name: "Discard" }));
    expect(await screen.findByRole("heading", { name: "Projects" })).toBeInTheDocument();
  });

  it("keeps the authenticated session and exposes the error when sign-out fails", async () => {
    answering((request) => {
      const path = new URL(request.url).pathname;
      if (path === "/api/bootstrap") return json({ required: false, available: false });
      if (path === "/api/session" && request.method === "GET") return json(session);
      if (path === "/api/session" && request.method === "DELETE") {
        return json({ code: "unavailable", status: 503, title: "must-not-render" }, 503);
      }
      if (path === "/api/projects") return json([]);
      throw new Error(`Unexpected ${request.method} ${path}`);
    });
    render(<App />);
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "Projects" });

    const account = screen.getByRole("button", { name: `Account: ${session.email}` });
    await user.click(account);
    await user.click(await screen.findByRole("menuitem", { name: "Sign out" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("HTTP 503");
    expect(screen.getByRole("heading", { name: "Projects" })).toBeInTheDocument();
    expect(account).toBeInTheDocument();
  });

  it("shows stable validation facts without rendering an untrusted problem title", async () => {
    answering((request) => {
      const path = new URL(request.url).pathname;
      if (path === "/api/bootstrap") return json({ required: false, available: false });
      if (path === "/api/session") return json(session);
      if (path === "/api/projects" && request.method === "GET") return json([]);
      return json(
        {
          code: "validation",
          status: 400,
          title: "must-not-render",
          errors: { key: ["A project key is already reserved."] },
        },
        400,
      );
    });
    render(<App />);
    const user = userEvent.setup();
    await screen.findByText(/No live projects yet/);
    await user.click(screen.getByRole("button", { name: "New project" }));
    await user.type(screen.getByLabelText("Immutable key"), "backup-jobs");
    await user.type(screen.getByLabelText("Display name"), "Backup jobs");
    await user.click(screen.getByRole("button", { name: "Create project" }));

    const alert = await screen.findByText(/Check the submitted facts/);
    expect(alert).toHaveTextContent("key: A project key is already reserved.");
    expect(alert).not.toHaveTextContent("must-not-render");
    expect(screen.getByLabelText("Immutable key")).toHaveAccessibleErrorMessage("A project key is already reserved.");
  });

  it("shows a bounded retryable failure without rendering an untrusted body", async () => {
    answering(() => json({ secret: "must-not-render" }, 503));
    render(<App />);
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("HTTP 503");
    expect(alert).not.toHaveTextContent("must-not-render");
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });
});
