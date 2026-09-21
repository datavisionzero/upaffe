import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it } from "vitest";

import { Button } from "./Button";
import { CheckboxField, SelectField, TextField } from "./Fields";

it("associates labels, guidance and validation with their controls", async () => {
  const user = userEvent.setup();
  render(<>
    <TextField description="Use the operator address" error="Enter a valid address"
      label="Email" name="email" />
    <SelectField label="Mode" description="How reports arrive" defaultValue="state">
      <option value="state">State</option>
    </SelectField>
    <CheckboxField label="Include history" description="Show prior observations" />
    <Button type="submit" variant="primary">Save</Button>
  </>);

  const email = screen.getByRole("textbox", { name: "Email" });
  expect(email).toHaveAttribute("aria-invalid", "true");
  expect(email).toHaveAccessibleErrorMessage("Enter a valid address");
  expect(email).toHaveAccessibleDescription("Use the operator address Enter a valid address");
  expect(screen.getByRole("combobox", { name: "Mode" })).toHaveAccessibleDescription("How reports arrive");
  expect(screen.getByRole("checkbox", { name: "Include history" })).toHaveAccessibleDescription("Show prior observations");
  await user.tab();
  expect(email).toHaveFocus();
});

it("blocks a pending action while retaining its name", () => {
  render(<Button pending variant="primary">Saving…</Button>);
  expect(screen.getByRole("button", { name: "Saving…" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "Saving…" })).toHaveAttribute("aria-busy", "true");
});
