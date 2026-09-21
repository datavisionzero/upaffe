import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";

import { ConfirmDialog } from "./ConfirmDialog";

it("confirms a destructive action in a modal and restores trigger focus after dismissal", async () => {
  const onConfirm = vi.fn(async () => undefined);
  const user = userEvent.setup();
  render(<ConfirmDialog confirmLabel="Confirm removal" description="History is retained."
    onConfirm={onConfirm} title="Remove monitor?" triggerLabel="Remove monitor…" />);

  const trigger = screen.getByRole("button", { name: "Remove monitor…" });
  await user.click(trigger);
  const dialog = screen.getByRole("dialog", { name: "Remove monitor?" });
  expect(dialog).toHaveTextContent("History is retained.");
  await user.keyboard("{Escape}");
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(trigger).toHaveFocus();

  await user.click(trigger);
  await user.click(screen.getByRole("button", { name: "Confirm removal" }));
  expect(onConfirm).toHaveBeenCalledOnce();
});
