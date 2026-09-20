import type { ReactNode } from "react";

type Tone = "neutral" | "healthy" | "failing" | "untested" | "paused" | "overdue" | "maintenance" | "warning";

export function StatusBadge({ tone, children }: { tone: Tone; children: ReactNode }) {
  return <span className="ui-badge" data-tone={tone}>{children}</span>;
}

export function Alert({ tone = "neutral", children }: { tone?: "neutral" | "warning" | "danger"; children: ReactNode }) {
  return <div className="ui-alert" data-tone={tone} role={tone === "danger" ? "alert" : "status"}>{children}</div>;
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="ui-empty">{children}</p>;
}

export function LoadingState({ children = "Loading…" }: { children?: ReactNode }) {
  return <p className="ui-loading" role="status">{children}</p>;
}

export function PageHeader({ title, detail, actions }: { title: ReactNode; detail?: ReactNode; actions?: ReactNode }) {
  return <header className="ui-page-header"><div><h1>{title}</h1>{detail && <p>{detail}</p>}</div>
    {actions && <div className="ui-page-actions">{actions}</div>}</header>;
}

export function Section({ title, actions, children }: { title: ReactNode; actions?: ReactNode; children: ReactNode }) {
  return <section className="ui-section"><header className="ui-section-header"><h2>{title}</h2>{actions}</header>{children}</section>;
}
