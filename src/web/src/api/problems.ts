type UnknownRecord = Record<string, unknown>;

function record(value: unknown): UnknownRecord | undefined {
  return typeof value === "object" && value !== null ? (value as UnknownRecord) : undefined;
}

/** Render only stable problem facts, never an untrusted remote title. */
export function problemMessage(error: unknown, status: number): string {
  const problem = record(error);
  const code = typeof problem?.code === "string" ? problem.code : undefined;
  const errors = record(problem?.errors);
  const fieldMessages = errors
    ? Object.entries(errors).flatMap(([field, messages]) =>
        Array.isArray(messages)
          ? messages.filter((message): message is string => typeof message === "string").map((message) => `${field}: ${message}`)
          : [],
      )
    : [];

  if (code === "validation") {
    return fieldMessages.length > 0
      ? `Check the submitted facts. ${fieldMessages.join(" ")}`
      : "Check the submitted facts.";
  }
  if (code === "conflict") return "The request conflicts with the current state. Refresh and try again.";
  if (code === "not_found") return "The requested object no longer exists.";
  if (code === "sign_in_rejected") return "The email address or password was rejected.";
  if (code === "authentication_required") return "Sign in to continue.";
  if (code === "authentication_rejected") return "This session is no longer valid. Sign in again.";
  if (code === "forbidden") return "This access path cannot perform that operation.";
  return `The instance refused the request (HTTP ${status}).`;
}

export const csrfHeaders = { "X-Upaffe-CSRF": "1" } as const;
