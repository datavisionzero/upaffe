import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "./DropdownMenu";

it("opens from the keyboard, moves through items, dismisses, and restores trigger focus", async () => {
  const first = vi.fn();
  const user = userEvent.setup();
  render(
    <DropdownMenu>
      <DropdownMenuTrigger>Account</DropdownMenuTrigger>
      <DropdownMenuContent>
        <DropdownMenuItem onClick={first}>Settings</DropdownMenuItem>
        <DropdownMenuItem>Sign out</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>,
  );

  const trigger = screen.getByRole("button", { name: "Account" });
  trigger.focus();
  await user.keyboard("{Enter}");

  const settings = await screen.findByRole("menuitem", { name: "Settings" });
  expect(settings).toHaveFocus();
  await user.keyboard("{Enter}");
  expect(first).toHaveBeenCalledOnce();
  await waitFor(() => expect(screen.queryByRole("menuitem", { name: "Settings" })).not.toBeInTheDocument());
  expect(trigger).toHaveFocus();

  await user.keyboard("{ArrowDown}");
  expect(await screen.findByRole("menuitem", { name: "Settings" })).toHaveFocus();
  await user.keyboard("{Escape}");
  expect(trigger).toHaveFocus();
});
