import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import { ThemeProvider, useTheme } from "./ThemeProvider";

function Choice() {
  const { theme, setTheme } = useTheme();
  return <><output>{theme}</output><button onClick={() => setTheme("light")}>Light</button>
    <button onClick={() => setTheme("dark")}>Dark</button>
    <button onClick={() => setTheme("system")}>System</button></>;
}

let dark = false;
let change: (() => void) | undefined;

beforeEach(() => {
  const values = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value); },
    clear: () => values.clear(),
  });
  dark = false;
  change = undefined;
  vi.stubGlobal("matchMedia", vi.fn(() => ({
    get matches() { return dark; },
    addEventListener: (_type: string, callback: () => void) => { change = callback; },
    removeEventListener: () => { change = undefined; },
  })));
});

afterEach(() => {
  vi.unstubAllGlobals();
  document.documentElement.classList.remove("light", "dark");
});

it("follows system changes until an explicit preference is selected", () => {
  render(<ThemeProvider><Choice /></ThemeProvider>);
  expect(document.documentElement).toHaveClass("light");
  act(() => { dark = true; change?.(); });
  expect(document.documentElement).toHaveClass("dark");
  act(() => screen.getByRole("button", { name: "Light" }).click());
  expect(document.documentElement).toHaveClass("light");
  expect(window.localStorage.getItem("upaffe-theme")).toBe("light");
  act(() => { dark = false; change?.(); });
  expect(document.documentElement).toHaveClass("light");
});

it("restores a stored choice and tolerates blocked storage", () => {
  window.localStorage.setItem("upaffe-theme", "dark");
  const first = render(<ThemeProvider><Choice /></ThemeProvider>);
  expect(document.documentElement).toHaveClass("dark");
  first.unmount();
  vi.spyOn(window.localStorage, "getItem").mockImplementation(() => { throw new Error("blocked"); });
  vi.spyOn(window.localStorage, "setItem").mockImplementation(() => { throw new Error("blocked"); });
  render(<ThemeProvider><Choice /></ThemeProvider>);
  expect(screen.getByText("system")).toBeInTheDocument();
  act(() => screen.getByRole("button", { name: "Light" }).click());
  expect(document.documentElement).toHaveClass("light");
  vi.restoreAllMocks();
});

it("synchronizes a valid theme preference from another tab", () => {
  render(<ThemeProvider><Choice /></ThemeProvider>);
  act(() => window.dispatchEvent(new StorageEvent("storage", {
    key: "upaffe-theme", newValue: "dark",
  })));
  expect(screen.getByText("dark")).toBeInTheDocument();
  expect(document.documentElement).toHaveClass("dark");

  act(() => window.dispatchEvent(new StorageEvent("storage", {
    key: "upaffe-theme", newValue: "unknown",
  })));
  expect(screen.getByText("system")).toBeInTheDocument();
});
