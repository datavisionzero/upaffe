import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { App } from "@/shell/App";

describe("the technical application shell", () => {
  afterEach(() => vi.unstubAllGlobals());

  function answering(answer: () => Promise<Response>) {
    const fetch = vi.fn<typeof globalThis.fetch>(() => answer());
    vi.stubGlobal("fetch", fetch);
    return fetch;
  }

  function json(body: unknown, status = 200) {
    return new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    });
  }

  it("shows the version returned through the generated contract", async () => {
    answering(() => Promise.resolve(json({ version: "1.2.3" })));
    render(<App />);
    expect(await screen.findByText(/1\.2\.3/)).toBeInTheDocument();
  });

  it("shows a bounded refusal without rendering an untrusted response body", async () => {
    answering(() => Promise.resolve(json({ secret: "must-not-render" }, 503)));
    render(<App />);
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("HTTP 503");
    expect(alert).not.toHaveTextContent("must-not-render");
  });

  it("distinguishes an unreachable instance", async () => {
    answering(() => Promise.reject(new TypeError("Failed to fetch")));
    render(<App />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Nothing answered");
  });

  it("asks the same-origin API and can retry from the keyboard", async () => {
    let version = "1.2.3";
    const fetch = answering(() => Promise.resolve(json({ version })));
    render(<App />);
    await screen.findByText(/1\.2\.3/);

    const asked = new URL((fetch.mock.calls[0][0] as Request).url);
    expect(asked.origin).toBe(window.location.origin);
    expect(asked.pathname).toBe("/api/version");

    version = "1.2.4";
    await userEvent.tab();
    await userEvent.keyboard("{Enter}");
    expect(await screen.findByText(/1\.2\.4/)).toBeInTheDocument();
  });
});
