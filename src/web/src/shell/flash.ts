/**
 * A one-shot confirmation carried across a route change, such as "Configuration
 * saved." shown on the detail a focused edit route returns to.
 */
const pending = new Map<string, string>();

export function setFlash(scope: string, message: string) {
  pending.set(scope, message);
}

export function takeFlash(scope: string): string | undefined {
  const message = pending.get(scope);
  pending.delete(scope);
  return message;
}
