import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";

import { ProjectSwitcher } from "./ProjectSwitcher";

const projects = [
  { id: "1", key: "jobs", name: "Jobs", version: 1, created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null },
  { id: "2", key: "sites", name: "Sites", version: 1, created_at: "2026-09-19T12:00:00Z", updated_at: "2026-09-19T12:00:00Z", deleted_at: null },
];

it("shows current project context and switches to another project", async () => {
  const onSwitch = vi.fn();
  const user = userEvent.setup();
  render(<ProjectSwitcher projects={projects} currentKey="jobs" currentName="Jobs" loading={false}
    onSwitch={onSwitch} />);

  await user.click(screen.getByRole("button", { name: "Switch project" }));
  expect(await screen.findByRole("menuitemradio", { name: /Jobs/ })).toHaveAttribute("aria-checked", "true");
  await user.click(screen.getByRole("menuitemradio", { name: /Sites/ }));
  expect(onSwitch).toHaveBeenCalledWith("sites");
});

it("describes loading and unavailable project lists inside the switcher", async () => {
  const user = userEvent.setup();
  const view = render(<ProjectSwitcher projects={[]} currentKey="jobs" loading onSwitch={vi.fn()} />);
  await user.click(screen.getByRole("button", { name: "Switch project" }));
  expect(await screen.findByRole("menuitem", { name: "Loading projects…" })).toHaveAttribute("aria-disabled", "true");

  view.unmount();
  render(<ProjectSwitcher projects={[]} currentKey="jobs" loading={false} error="unavailable"
    onSwitch={vi.fn()} />);
  await user.click(screen.getByRole("button", { name: "Switch project" }));
  expect(await screen.findByRole("menuitem", { name: "Projects unavailable" })).toHaveAttribute("aria-disabled", "true");
});
