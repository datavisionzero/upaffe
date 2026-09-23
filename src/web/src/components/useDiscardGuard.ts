import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";

/**
 * Protects typed input on a focused creation route. Escape and Cancel leave
 * directly while the form is untouched; otherwise they ask before discarding.
 */
export function useDiscardGuard({ dirty, pending, onLeave }: {
  dirty: boolean; pending: boolean; onLeave: () => void;
}) {
  const [open, setOpen] = useState(false);
  const current = useRef({ dirty, open, pending, onLeave });
  useLayoutEffect(() => { current.current = { dirty, open, pending, onLeave }; }, [dirty, open, pending, onLeave]);

  useEffect(() => {
    const unload = (event: BeforeUnloadEvent) => {
      if (!current.current.dirty || current.current.pending) return;
      event.preventDefault();
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || current.current.open || current.current.pending) return;
      event.preventDefault();
      if (current.current.dirty) window.setTimeout(() => setOpen(true), 0);
      else current.current.onLeave();
    };
    window.addEventListener("beforeunload", unload);
    window.addEventListener("keydown", escape, true);
    return () => {
      window.removeEventListener("beforeunload", unload);
      window.removeEventListener("keydown", escape, true);
    };
  }, []);

  const cancel = useCallback(() => {
    if (current.current.dirty) setOpen(true);
    else current.current.onLeave();
  }, []);

  return { open, setOpen, cancel };
}
