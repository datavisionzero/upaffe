import { useCallback, useEffect, useState, type MouseEvent, type ReactNode } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { Alert, EmptyState, LoadingState, PageHeader, SectionHeading, StatusBadge } from "@/components/Presentation";
import { monitorStateTone, type StatusTone } from "@/components/status";
import { MonitorPurpose } from "@/shell/MonitorPurpose";
import { monitorPath, projectPath, withReturn } from "@/shell/routes";

type Overview = components["schemas"]["InstanceOverview"];
type Monitor = components["schemas"]["OverviewMonitor"];
type Project = components["schemas"]["OverviewProject"];

export function DashboardView({ onNavigate, onSignedOut }: {
  onNavigate: (path: string) => void;
  onSignedOut: () => void;
}) {
  const [overview, setOverview] = useState<Overview>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      const result = await api.GET("/api/overview");
      if (result.data) setOverview(result.data);
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch {
      setError("The health overview could not be reached.");
    } finally {
      setLoading(false);
    }
  }, [onSignedOut]);

  useEffect(() => {
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load]);

  function link(path: string, label: string, className?: string) {
    return <a className={className} href={path} onClick={(event: MouseEvent<HTMLAnchorElement>) => {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      onNavigate(path);
    }}>{label}</a>;
  }

  const generatedAt = overview?.generated_at;
  const openIncidents = overview?.attention.filter((monitor) => monitor.incident_id).length ?? 0;
  const otherAttention = (overview?.attention.length ?? 0) - openIncidents;

  return <div className="workspace dashboard">
    <PageHeader title="Health dashboard" detail="Current monitoring evidence across your projects."
      actions={<Button disabled={loading} onClick={() => void load()} type="button">
        {loading && overview ? "Refreshing…" : "Refresh"}
      </Button>} />
    {generatedAt && <p className="snapshot-time">Snapshot generated {dateLabel(generatedAt)}</p>}

    {loading && !overview && <LoadingState>Loading instance health…</LoadingState>}
    {error && <Alert tone="danger">{error} <Button onClick={() => void load()} type="button">Try again</Button></Alert>}
    {overview && <>
      <section aria-label="Health counts" className="health-counts">
        <div className="health-count health-count-urgent"><strong>{openIncidents}</strong><span>Open incidents</span></div>
        <div className="health-count"><strong>{otherAttention}</strong><span>Other monitors needing attention</span></div>
        <div className="health-count"><strong>{overview.counts.overdue}</strong><span>Overdue checks or reports</span></div>
        <div className="health-count"><strong>{overview.counts.healthy}</strong><span>Healthy monitors</span></div>
      </section>

      <section aria-labelledby="delivery-title" className="delivery-strip">
        <SectionHeading action={link(withReturn("/settings/email", "/dashboard"), "Open email status and settings", "text-link")}
          eyebrow="Notifications" title="Email delivery" titleId="delivery-title" />
          <p>
            {overview.delivery.terminal_failure} terminal failures · {overview.delivery.retrying} retrying · {overview.delivery.overdue} overdue · {overview.delivery.pending} pending
          </p>
          <p className="muted">
            {overview.delivery.accepted} accepted by SMTP; inbox receipt is not confirmed.
            {overview.delivery.oldest_pending_at && ` Oldest pending: ${ageLabel(overview.generated_at, overview.delivery.oldest_pending_at)}.`}
          </p>
      </section>

      <div className="dashboard-columns">
        <section aria-labelledby="attention-title" className="dashboard-attention">
          <SectionHeading action={<span className="count-pill">{overview.attention.length}</span>}
            eyebrow="Investigate" title="Needs attention" titleId="attention-title" />
          {overview.attention.length === 0 && <EmptyState>No monitors need attention in this snapshot.</EmptyState>}
          <div className="attention-list">
            {overview.attention.map((monitor) => {
              const path = monitorPath(monitor.project_key, monitor.type as "http" | "push", monitor.key);
              const description = monitorDescription(monitor);
              return <article className="attention-card" key={`${monitor.project_key}:${monitor.type}:${monitor.key}`}>
                <div className="attention-card-main">
                  <div className="attention-tags">
                    <StatusBadge tone={monitorStateTone(monitor.state)}>{stateLabel(monitor.state)}</StatusBadge>
                    {monitor.overdue && <StatusBadge tone="overdue">Overdue</StatusBadge>}
                    {monitor.effective_maintenance_until && <StatusBadge tone="maintenance">Maintenance</StatusBadge>}
                  </div>
                  <h3>{link(path, monitor.name, "monitor-name-link")}</h3>
                  <MonitorPurpose purpose={monitor.purpose} />
                  <p className="monitor-context">{monitor.project_key} · {monitor.type === "http" ? "HTTP check" : modeLabel(monitor.mode)}</p>
                  <p className="monitor-reason">{description}</p>
                  {monitor.incident_began_at && <p className="muted">Incident began {ageLabel(overview.generated_at, monitor.incident_began_at)} ago.</p>}
                  {monitor.latest_result_at && <p className="muted">Latest {monitor.latest_outcome ?? "result"}: {dateLabel(monitor.latest_result_at)}</p>}
                  {monitor.last_success_at && <p className="muted">Last success: {dateLabel(monitor.last_success_at)}</p>}
                  {monitor.next_due_at && <p className="muted">Next expected work: {dateLabel(monitor.next_due_at)}</p>}
                  {monitor.effective_maintenance_until && <p className="maintenance-note">Notifications suppressed by maintenance until {dateLabel(monitor.effective_maintenance_until)}. Monitoring continues.</p>}
                </div>
                {link(path, "Open monitor →", "text-link")}
              </article>;
            })}
          </div>
        </section>

        <section aria-labelledby="projects-title" className="dashboard-projects">
          <SectionHeading action={link("/projects", "Manage projects", "text-link")}
            eyebrow="Across the instance" title="Projects" titleId="projects-title" />
          {overview.projects.length === 0 && <EmptyState>No projects yet. Create one from Projects.</EmptyState>}
          <div className="project-summary-list">
            {overview.projects.map((project) => <ProjectSummary key={project.key}
              project={project} hasOpenIncident={overview.attention.some((monitor) =>
                monitor.project_key === project.key && !!monitor.incident_id)} link={link} />)}
          </div>
        </section>
      </div>
    </>}
  </div>;
}

function ProjectSummary({ project, hasOpenIncident, link }: {
  project: Project;
  hasOpenIncident: boolean;
  link: (path: string, label: string, className?: string) => ReactNode;
}) {
  const status = projectStatus(project, hasOpenIncident);
  return <article className="project-summary">
    <div className="project-summary-top">
      <h3>{link(projectPath(project.key), project.name, "project-name-link")}</h3>
      <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
    </div>
    <p className="project-key">{project.key}</p>
    <p className="project-counts">{project.counts.total} monitors · {project.counts.healthy} healthy · {project.counts.failing} failing · {project.counts.untested} untested · {project.counts.paused} paused</p>
    {project.counts.overdue > 0 && <p className="project-warning">{project.counts.overdue} overdue checks or reports</p>}
    {project.delivery.terminal_failure > 0 && <p className="project-warning">{project.delivery.terminal_failure} terminal email failures</p>}
    {project.delivery.overdue > 0 && <p className="project-warning">{project.delivery.overdue} overdue email attempts</p>}
    {project.maintenance_until && <p className="maintenance-note">Project maintenance until {dateLabel(project.maintenance_until)}</p>}
  </article>;
}

function projectStatus(project: Project, hasOpenIncident: boolean): { label: string; tone: StatusTone } {
  if (project.counts.total === 0) return { label: "Empty", tone: "untested" };
  if (hasOpenIncident || project.counts.failing > 0 || project.delivery.terminal_failure > 0)
    return { label: "Needs attention", tone: "failing" };
  if (project.counts.overdue > 0 || project.delivery.overdue > 0)
    return { label: "Overdue work", tone: "overdue" };
  if (project.counts.untested > 0) return { label: "Untested monitors", tone: "untested" };
  if (project.counts.paused > 0) return { label: "Paused monitors", tone: "paused" };
  return { label: "Healthy", tone: "healthy" };
}

function monitorDescription(monitor: Monitor): string {
  const reason = reasonLabel(monitor.incident_reason ?? monitor.latest_reason);
  if (monitor.state === "paused") return monitor.incident_id
    ? `Monitoring paused; incident remains open${reason ? `: ${reason}` : "."}`
    : "Monitoring paused. Earlier results do not represent current health.";
  if (monitor.incident_id) return `Incident open${reason ? `: ${reason}` : "."}`;
  if (monitor.state === "failing") return `${monitor.type === "http" ? "Failed check" : "Failed report"}; no incident open${reason ? `: ${reason}` : "."}`;
  if (monitor.overdue) return monitor.type === "http"
    ? "Check execution overdue. The last result remains the current observation."
    : "Report deadline passed; missing-report evaluation has not yet been recorded.";
  return monitor.state === "untested" ? "Awaiting a first result." : "Current state needs review.";
}

function reasonLabel(reason: string | null | undefined): string | undefined {
  if (!reason) return undefined;
  const known: Record<string, string> = {
    report_missing: "missing report",
    reported_failure: "reported failure",
    status_mismatch: "HTTP status mismatch",
    text_missing: "required text missing",
    text_present: "forbidden text present",
    timeout: "request timeout",
  };
  return known[reason] ?? reason.replaceAll("_", " ");
}

function stateLabel(state: string): string {
  return state === "healthy" ? "Healthy" : state === "failing" ? "Failing"
    : state === "paused" ? "Paused" : "Untested";
}

function modeLabel(mode: string | null | undefined): string {
  return mode === "job_completion" ? "Job completion" : "State report";
}

function ageLabel(generatedAt: string, occurredAt: string): string {
  const seconds = Math.max(0, Math.floor((Date.parse(generatedAt) - Date.parse(occurredAt)) / 1000));
  if (!Number.isFinite(seconds)) return "at an unknown time";
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h`;
  return `${Math.floor(seconds / 86400)}d`;
}

function dateLabel(value: string): string {
  return new Date(value).toLocaleString();
}
