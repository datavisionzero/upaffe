import { expect, it } from "vitest";

import { returnPath, withReturn } from "@/shell/routes";

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
