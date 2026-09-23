import { expect, it } from "vitest";

import { parseRoute, returnPath, withReturn } from "@/shell/routes";

it("keeps project creation distinct from a project key", () => {
  expect(parseRoute("/projects/new")).toEqual({ kind: "new-project" });
  expect(parseRoute("/projects/example")).toEqual({ kind: "project", projectKey: "example", section: "overview" });
});

it("returns to a monitor investigation with its selected incident", () => {
  const source = "/projects/systems/http-monitors/site?incident=dcf2cf4b-1070-45b7-bd39-dfd12f1fb901";
  const destination = withReturn("/settings/email", source);
  expect(returnPath(destination.slice(destination.indexOf("?")), "/projects")).toBe(source);
});

it("rejects external or unknown return destinations", () => {
  for (const source of ["//example.test", "/\\example.test", "https://example.test", "/missing"]) {
    expect(returnPath(`?return=${encodeURIComponent(source)}`, "/projects")).toBe("/projects");
  }
});

it("opens monitor creation without shadowing a monitor keyed new", () => {
  expect(parseRoute("/projects/jobs/new-push-monitor"))
    .toEqual({ kind: "project", projectKey: "jobs", section: "push", create: true });
  expect(parseRoute("/projects/jobs/new-http-monitor"))
    .toEqual({ kind: "project", projectKey: "jobs", section: "http", create: true });
  expect(parseRoute("/projects/jobs/push-monitors/new"))
    .toEqual({ kind: "project", projectKey: "jobs", section: "push", monitorKey: "new" });
});
