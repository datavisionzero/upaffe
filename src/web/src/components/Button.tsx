import { Button as ButtonPrimitive } from "@base-ui/react/button";
import type { ComponentProps } from "react";

/** The first repository-owned component over a Base UI primitive. */
export function Button({ className = "", ...props }: ComponentProps<typeof ButtonPrimitive>) {
  return (
    <ButtonPrimitive
      className={`border-line hover:border-accent focus-visible:outline-accent rounded-md border px-3 py-1.5 text-sm focus-visible:outline-2 focus-visible:outline-offset-2 ${className}`}
      {...props}
    />
  );
}
