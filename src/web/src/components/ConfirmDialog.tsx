import { Dialog } from "@base-ui/react/dialog";
import { useState } from "react";

import { Button } from "./Button";

export function ConfirmDialog({ triggerLabel, title, description, confirmLabel, pending = false, onConfirm }: {
  triggerLabel: string;
  title: string;
  description: string;
  confirmLabel: string;
  pending?: boolean;
  onConfirm: () => Promise<void>;
}) {
  const [open, setOpen] = useState(false);

  async function confirm() {
    await onConfirm();
    setOpen(false);
  }

  return <Dialog.Root open={open} onOpenChange={(next) => { if (!pending) setOpen(next); }}>
    <Dialog.Trigger render={<Button type="button" variant="destructive" />}>{triggerLabel}</Dialog.Trigger>
    <Dialog.Portal>
      <Dialog.Backdrop className="ui-dialog-backdrop" />
      <Dialog.Popup className="ui-dialog-popup">
        <Dialog.Title>{title}</Dialog.Title>
        <Dialog.Description>{description}</Dialog.Description>
        <div className="ui-dialog-actions">
          <Dialog.Close disabled={pending} render={<Button type="button" />}>Cancel</Dialog.Close>
          <Button onClick={() => void confirm()} pending={pending} type="button" variant="destructive">
            {pending ? "Removing…" : confirmLabel}
          </Button>
        </div>
      </Dialog.Popup>
    </Dialog.Portal>
  </Dialog.Root>;
}
