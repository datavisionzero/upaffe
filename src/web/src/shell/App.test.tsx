import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

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

  it("shows loading and a distinct bootstrap surface when the operator is missing", async () => {
    const bootstrap = deferred<Response>();
    answering(() => bootstrap.promise);
    render(<App />);
    expect(screen.getByRole("status")).toHaveTextContent("Opening the instance");

    bootstrap.resolve(json({ required: true, available: false }));
    expect(await screen.findByRole("heading", { name: "Establish the operator" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("No live bootstrap proof");
    expect(screen.getByRole("button", { name: "Establish operator" })).toBeDisabled();
  });

  it("submits bootstrap password fields and clears both secrets immediately", async () => {
    const established = deferred<Response>();
    answering(async (request) => {
      if (request.method === "GET") return json({ required: true, available: true });
      expect(new URL(request.url).pathname).toBe("/api/bootstrap");
      expect(await request.json()).toEqual({
        email: "operator@example.test",
        proof: "one-use-proof",
        password: "a long password",
      });
      return established.promise;
    });
    render(<App />);
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "Establish the operator" });

    await user.type(screen.getByLabelText("Email"), "operator@example.test");
    await user.type(screen.getByLabelText("Bootstrap proof"), "one-use-proof");
    await user.type(screen.getByLabelText("Password"), "a long password");
    await user.click(screen.getByRole("button", { name: "Establish operator" }));

    expect(screen.getByLabelText("Bootstrap proof")).toHaveValue("");
    expect(screen.getByLabelText("Password")).toHaveValue("");
    established.resolve(new Response(null, { status: 204 }));
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
    expect(await screen.findByText(/No projects yet/)).toBeInTheDocument();
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
    await screen.findByText(/No projects yet/);

    await user.type(screen.getByLabelText("Immutable key"), "backup-jobs");
    await user.type(screen.getByLabelText("Display name"), "Backup jobs");
    await user.tab();
    await user.keyboard("{Enter}");
    expect(await screen.findByText("backup-jobs")).toBeInTheDocument();

    const rename = screen.getByLabelText("New display name for backup-jobs");
    await user.clear(rename);
    await user.type(rename, "Backups{Enter}");
    expect(await screen.findByRole("heading", { name: "Backups" })).toBeInTheDocument();
    expect(screen.getByText("version 2")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Delete" }));
    expect(await screen.findByText(/No projects yet/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Show deleted" }));
    expect(await screen.findByRole("button", { name: "Restore" })).toBeInTheDocument();
    expect(screen.getByText("version 3")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Restore" }));
    expect(await screen.findByText("No deleted projects.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Sign out" }));
    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
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
    await screen.findByText(/No projects yet/);
    await user.type(screen.getByLabelText("Immutable key"), "backup-jobs");
    await user.type(screen.getByLabelText("Display name"), "Backup jobs");
    await user.click(screen.getByRole("button", { name: "Create" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("key: A project key is already reserved.");
    expect(alert).not.toHaveTextContent("must-not-render");
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
