import { Dialog } from "@base-ui/react/dialog";
import type { RefObject } from "react";

import { Button } from "@/components/Button";

export function DiscardDialog({ open, onOpenChange, title, description, onDiscard, returnFocus }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  onDiscard: () => void;
  returnFocus?: RefObject<HTMLElement | null>;
}) {
  return <Dialog.Root open={open} onOpenChange={onOpenChange}>
    <Dialog.Portal>
      <Dialog.Backdrop className="ui-dialog-backdrop" />
      <Dialog.Popup className="ui-dialog-popup" finalFocus={() => returnFocus?.current ?? true}>
        <Dialog.Title>{title}</Dialog.Title>
        <Dialog.Description>{description}</Dialog.Description>
        <div className="ui-dialog-actions">
          <Dialog.Close render={<Button type="button" />}>Keep editing</Dialog.Close>
          <Button onClick={onDiscard} type="button" variant="destructive">Discard</Button>
        </div>
      </Dialog.Popup>
    </Dialog.Portal>
  </Dialog.Root>;
}
