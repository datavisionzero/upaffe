import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";

import { ProjectsView } from "@/shell/ProjectsView";

// The API omits absent values, so a live project arrives without deleted_at.
const live = { id: "5c0e5d49-5a8d-4ac6-a2f0-3c3f3a1e2b10", key: "review-demo", name: "Review Demo",
  version: 1, created_at: "2026-09-23T08:00:00Z", updated_at: "2026-09-23T08:00:00Z" };
const deleted = { ...live, version: 2, updated_at: "2026-09-23T09:00:00Z", deleted_at: "2026-09-23T09:00:00Z" };

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

afterEach(() => { vi.unstubAllGlobals(); });

it("opens a live project whose response omits the deletion time", async () => {
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async () => json([live])));
  const onNavigate = vi.fn();
  render(<ProjectsView onNavigate={onNavigate} onSignedOut={vi.fn()} search="" />);

  const link = await screen.findByRole("link", { name: "Review Demo" });
  expect(link).toHaveAttribute("href", "/projects/review-demo");
  expect(screen.getByText("Showing 1 of 1 live projects")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Restore" })).not.toBeInTheDocument();
  const row = link.closest("article")!;
  expect(row).not.toHaveTextContent(/Deleted|Invalid Date/);
  await userEvent.setup().click(link);
  expect(onNavigate).toHaveBeenCalledWith("/projects/review-demo");
});

it("keeps deleted projects distinct and restores them", async () => {
  let restored = false;
  const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
    const request = input as Request;
    const url = new URL(request.url);
    if (url.pathname.endsWith("/restore")) {
      restored = true;
      expect(await request.json()).toEqual({ version: 2 });
      return json({ ...live, version: 3 });
    }
    return json(url.searchParams.get("deleted") === "true" && !restored ? [deleted] : []);
  });
  vi.stubGlobal("fetch", fetch);
  render(<ProjectsView onNavigate={vi.fn()} onSignedOut={vi.fn()} search="?state=deleted" />);

  expect(await screen.findByText(`Deleted ${new Date(deleted.deleted_at).toLocaleString()}`)).toBeInTheDocument();
  expect(screen.queryByRole("link", { name: "Review Demo" })).not.toBeInTheDocument();
  await userEvent.setup().click(screen.getByRole("button", { name: "Restore" }));
  expect(await screen.findByText("No deleted projects.")).toBeInTheDocument();
});

it("never renders an invalid deletion date", async () => {
  vi.stubGlobal("fetch", vi.fn<typeof globalThis.fetch>(async () => json([{ ...deleted, deleted_at: "not a time" }])));
  render(<ProjectsView onNavigate={vi.fn()} onSignedOut={vi.fn()} search="?state=deleted" />);

  expect(await screen.findByText("Deleted")).toBeInTheDocument();
  expect(screen.queryByText(/Invalid Date/)).not.toBeInTheDocument();
});
