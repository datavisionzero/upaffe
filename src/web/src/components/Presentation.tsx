import type { ReactNode } from "react";
import { CalendarClock, CircleAlert, CircleCheck, CircleHelp, Clock3, Pause, TriangleAlert } from "lucide-react";
import type { StatusTone } from "./status";

const icons = { healthy: CircleCheck, failing: CircleAlert, untested: CircleHelp,
  paused: Pause, overdue: Clock3, maintenance: CalendarClock, warning: TriangleAlert };

export function StatusBadge({ tone, children }: { tone: StatusTone; children: ReactNode }) {
  const Icon = tone === "neutral" ? null : icons[tone];
  return <span className="ui-badge" data-tone={tone}>{Icon && <Icon aria-hidden="true" size={12} />}{children}</span>;
}

export function Alert({ tone = "neutral", children }: { tone?: "neutral" | "warning" | "danger"; children: ReactNode }) {
  return <div className="ui-alert" data-tone={tone} role={tone === "danger" ? "alert" : "status"}>{children}</div>;
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="ui-empty" role="status">{children}</p>;
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

export function SectionHeading({ title, titleId, eyebrow, action }: {
  title: ReactNode; titleId: string; eyebrow?: string; action?: ReactNode;
}) {
  return <header className="ui-section-header"><div>{eyebrow && <p className="eyebrow">{eyebrow}</p>}
    <h2 id={titleId}>{title}</h2></div>{action}</header>;
}
