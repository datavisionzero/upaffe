export type StatusTone = "neutral" | "healthy" | "failing" | "untested" | "paused" | "overdue" | "maintenance" | "warning";

export function monitorStateTone(state: string): StatusTone {
  return state === "healthy" || state === "failing" || state === "untested" || state === "paused"
    ? state : "neutral";
}
