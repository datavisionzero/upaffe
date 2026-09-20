import { Button as ButtonPrimitive } from "@base-ui/react/button";
import type { ComponentProps } from "react";

type Variant = "primary" | "secondary" | "subtle" | "destructive";

export function Button({ className = "", variant = "secondary", pending = false, disabled, ...props }:
  ComponentProps<typeof ButtonPrimitive> & { variant?: Variant; pending?: boolean }) {
  return (
    <ButtonPrimitive
      aria-busy={pending || undefined}
      className={`ui-button ${className}`}
      data-variant={variant}
      disabled={disabled || pending}
      type="button"
      {...props}
    />
  );
}
