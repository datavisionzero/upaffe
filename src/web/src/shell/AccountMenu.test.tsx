import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import { ThemeProvider } from "@/theme/ThemeProvider";
import { AccountMenu } from "./AccountMenu";

beforeEach(() => {
  const values = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value); },
  });
  vi.stubGlobal("matchMedia", vi.fn(() => ({
    matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  })));
});

afterEach(() => {
  vi.unstubAllGlobals();
  document.documentElement.classList.remove("light", "dark");
});

it("offers identity, all appearance choices, settings and sign out from the keyboard", async () => {
  const onOpenSettings = vi.fn();
  const onSignOut = vi.fn();
  const user = userEvent.setup();
  render(<ThemeProvider><AccountMenu email="operator@example.test" signingOut={false}
    onOpenSettings={onOpenSettings} onSignOut={onSignOut} /></ThemeProvider>);

  const trigger = screen.getByRole("button", { name: "Account: operator@example.test" });
  trigger.focus();
  await user.keyboard("{Enter}");
  expect(await screen.findByText("operator@example.test")).toBeInTheDocument();
  expect(screen.getByRole("menuitemradio", { name: "System" })).toHaveAttribute("aria-checked", "true");

  await user.click(screen.getByRole("menuitemradio", { name: "Dark" }));
  expect(document.documentElement).toHaveClass("dark");
  expect(window.localStorage.getItem("upaffe-theme")).toBe("dark");
  await waitFor(() => expect(trigger).toHaveFocus());

  for (const choice of ["Light", "System"]) {
    await user.click(trigger);
    await user.click(await screen.findByRole("menuitemradio", { name: choice }));
  }
  expect(window.localStorage.getItem("upaffe-theme")).toBe("system");

  await user.click(trigger);
  await user.click(await screen.findByRole("menuitem", { name: "Settings" }));
  expect(onOpenSettings).toHaveBeenCalledOnce();

  await user.click(trigger);
  await user.click(await screen.findByRole("menuitem", { name: "Sign out" }));
  expect(onSignOut).toHaveBeenCalledOnce();
});

it("keeps a failed sign-out visible beside the account trigger", () => {
  render(<ThemeProvider><AccountMenu email="operator@example.test" error="Sign-out could not be completed."
    signingOut={false} onOpenSettings={vi.fn()} onSignOut={vi.fn()} /></ThemeProvider>);
  expect(screen.getByRole("alert")).toHaveTextContent("Sign-out could not be completed.");
  expect(screen.getByRole("button", { name: "Account: operator@example.test" })).toBeInTheDocument();
});
