import { useCallback, useEffect, useState, type MouseEvent, type ReactNode } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { Alert, EmptyState, LoadingState, PageHeader, SectionHeading, StatusBadge } from "@/components/Presentation";
import { monitorStateTone } from "@/components/status";
import { MonitorPurpose } from "@/shell/MonitorPurpose";
import { inventoryPath, monitorPath, projectPath, withReturn } from "@/shell/routes";

type Project = components["schemas"]["ProjectResponse"];
type Report = components["schemas"]["ProjectReport"];
type AttentionMonitor = Report["attention"][number];
type HealthyMonitor = Report["healthy"][number];

export function ProjectOverviewView({ project, onNavigate, onSignedOut }: {
  project: Project;
  onNavigate: (path: string) => void;
  onSignedOut: () => void;
}) {
  const [report, setReport] = useState<Report>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      const result = await api.GET("/api/projects/{key}/report", {
        params: { path: { key: project.key } },
      });
      if (result.data) setReport(result.data);
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch {
      setError("The project health report could not be reached.");
    } finally {
      setLoading(false);
    }
  }, [onSignedOut, project.key]);

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

  const base = projectPath(project.key);
  return <div className="workspace project-overview">
    <PageHeader title={project.name} detail="Current HTTP and push monitor health."
      actions={<Button disabled={loading} onClick={() => void load()} type="button">Refresh health</Button>} />
    <p className="project-key">Project · {project.key}</p>
    {report && <p className="snapshot-time">Snapshot generated {dateLabel(report.generated_at)}</p>}

    <nav aria-label="Project actions" className="project-action-nav">
      {link(inventoryPath(project.key), "Browse all project monitors")}
      {link(`${base}/http-monitors`, "Manage HTTP monitors")}
      {link(`${base}/push-monitors`, "Manage push monitors")}
      {link(withReturn(`${base}/settings/email`, base), "Recipients and maintenance")}
    </nav>

    {loading && !report && <LoadingState>Loading project health…</LoadingState>}
    {error && <Alert tone="danger">{error} <Button onClick={() => void load()} type="button">Try again</Button></Alert>}
    {report && <>
      <section aria-label="Project health counts" className="health-counts">
        <div className="health-count health-count-urgent"><strong>{report.counts.failing}</strong><span>Failing</span></div>
        <div className="health-count"><strong>{report.counts.untested}</strong><span>Untested</span></div>
        <div className="health-count"><strong>{report.counts.paused}</strong><span>Paused</span></div>
        <div className="health-count"><strong>{report.counts.healthy}</strong><span>Healthy</span></div>
      </section>

      <section aria-labelledby="project-attention-title" className="project-monitor-section">
        <SectionHeading action={<span className="count-pill">{report.attention.length}</span>}
          eyebrow="Investigate first" title="Needs attention" titleId="project-attention-title" />
        {report.attention.length === 0 && <EmptyState>No monitors need attention in this snapshot.</EmptyState>}
        <div className="project-monitor-list">{report.attention.map((monitor) =>
          <AttentionCard key={`${monitor.type}:${monitor.key}`} monitor={monitor} generatedAt={report.generated_at}
            link={link} projectKey={project.key} />)}</div>
      </section>

      <section aria-label="Project operation" className="project-operation">
        <div>
          <h2>Maintenance</h2>
          <p>{report.project_maintenance
            ? `Active until ${dateLabel(report.project_maintenance.ends_at)}. Monitoring continues; notifications are suppressed.`
            : "No project maintenance is active."}</p>
          {link(withReturn(`${base}/settings/email`, base), "Manage maintenance", "text-link")}
        </div>
        <div>
          <h2>Email delivery</h2>
          <p>{report.email.delivery.terminal_failure_count} terminal failures · {report.email.delivery.retrying_count} retrying · {report.email.delivery.pending_count} pending</p>
          <p className="muted">{report.email.delivery.smtp_accepted_count} accepted by SMTP; inbox receipt is not confirmed.</p>
          <p className="muted">{report.email.configured ? "Relay configured" : "Relay not configured"} · {report.email.recipients.length} project recipients</p>
          {link(withReturn(`${base}/settings/email`, base), "Manage recipients and delivery", "text-link")}
        </div>
      </section>

      <section aria-labelledby="project-healthy-title" className="project-monitor-section">
        <SectionHeading action={<span className="count-pill">{report.healthy.length}</span>}
          eyebrow="Current observations" title="Healthy" titleId="project-healthy-title" />
        {report.healthy.length === 0 && <EmptyState>No healthy monitors in this snapshot.</EmptyState>}
        <div className="project-healthy-list">{report.healthy.map((monitor) =>
          <HealthyCard key={`${monitor.type}:${monitor.key}`} monitor={monitor} link={link}
            projectKey={project.key} />)}</div>
      </section>
      {report.counts.total === 0 && <EmptyState>This project has no monitors. Use a management link above to create one.</EmptyState>}
    </>}
  </div>;
}

type InternalLink = (path: string, label: string, className?: string) => ReactNode;

function AttentionCard({ monitor, generatedAt, projectKey, link }: {
  monitor: AttentionMonitor; generatedAt: string; projectKey: string; link: InternalLink;
}) {
  const path = monitorPath(projectKey, monitor.type as "http" | "push", monitor.key);
  return <article className="attention-card">
    <div className="attention-card-main">
      <div className="attention-tags">
        <StatusBadge tone={monitorStateTone(monitor.state)}>{stateLabel(monitor.state)}</StatusBadge>
        {monitor.overdue && <StatusBadge tone="overdue">Overdue</StatusBadge>}
        {monitor.effective_maintenance_until && <StatusBadge tone="maintenance">Maintenance</StatusBadge>}
      </div>
      <h3>{link(path, monitor.name, "monitor-name-link")}</h3>
      <MonitorPurpose purpose={monitor.purpose} />
      <p className="monitor-context">{monitor.type === "http" ? "HTTP check" : modeLabel(monitor.mode)} · {monitor.key}</p>
      <p className="monitor-reason">{attentionReason(monitor)}</p>
      {monitor.incident && <p className="muted">Incident open since {dateLabel(monitor.incident.began_at)} ({ageLabel(generatedAt, monitor.incident.began_at)}).</p>}
      {monitor.latest_result && <p className="muted">Latest {monitor.latest_result.outcome}: {dateLabel(monitor.latest_result.observed_at)}{monitor.latest_result.reason ? ` · ${reasonLabel(monitor.latest_result.reason)}` : ""}</p>}
      {monitor.last_success && <p className="muted">Last success: {dateLabel(monitor.last_success.observed_at)}</p>}
      {monitor.next_due_at && <p className="muted">Next expected {monitor.type === "http" ? "check" : "report"}: {dateLabel(monitor.next_due_at)}</p>}
      {monitor.failure_count !== null && monitor.failure_threshold !== null && monitor.state === "failing" && !monitor.incident &&
        <p className="muted">{monitor.failure_count} of {monitor.failure_threshold} failures before an incident opens.</p>}
      {monitor.instruction && <p className="operator-guidance"><strong>Instruction:</strong> {monitor.instruction}</p>}
      {safeRunbook(monitor.runbook_url) && <p><a href={safeRunbook(monitor.runbook_url)} rel="noopener noreferrer" target="_blank">Open runbook</a></p>}
      {monitor.effective_maintenance_until && <p className="maintenance-note">Notifications suppressed until {dateLabel(monitor.effective_maintenance_until)}. Monitoring continues.</p>}
    </div>
    {link(path, "Open monitor →", "text-link")}
  </article>;
}

function HealthyCard({ monitor, projectKey, link }: {
  monitor: HealthyMonitor; projectKey: string; link: InternalLink;
}) {
  const path = monitorPath(projectKey, monitor.type as "http" | "push", monitor.key);
  return <article className="project-healthy-card">
    <div>
      <StatusBadge tone="healthy">Healthy</StatusBadge>
      <h3>{link(path, monitor.name, "monitor-name-link")}</h3>
      <MonitorPurpose purpose={monitor.purpose} />
      <p className="monitor-context">{monitor.type === "http" ? "HTTP check" : modeLabel(monitor.mode)} · {monitor.key}</p>
      <p className="muted">Last success: {monitor.last_success_at ? dateLabel(monitor.last_success_at) : "No success recorded"}</p>
      {monitor.next_due_at && <p className="muted">Next expected {monitor.type === "http" ? "check" : "report"}: {dateLabel(monitor.next_due_at)}</p>}
      {monitor.effective_maintenance_until && <p className="maintenance-note">Maintenance until {dateLabel(monitor.effective_maintenance_until)}</p>}
    </div>
    {link(path, "Open monitor →", "text-link")}
  </article>;
}

function attentionReason(monitor: AttentionMonitor): string {
  const reason = monitor.incident?.latest_reason ?? monitor.latest_result?.reason;
  if (monitor.state === "paused") return monitor.incident
    ? `Monitoring paused; incident remains open: ${reasonLabel(reason)}.`
    : "Monitoring paused. Previous results do not represent current health.";
  if (monitor.incident) return `Incident open: ${reasonLabel(reason)}.`;
  if (monitor.state === "failing") return `${monitor.type === "http" ? "Failed check" : "Failed report"}; no incident open${reason ? `: ${reasonLabel(reason)}` : "."}`;
  if (monitor.overdue) return monitor.type === "http"
    ? "Check execution overdue. The last result remains the current observation."
    : "Report deadline passed; missing-report evaluation has not yet been recorded.";
  return "Awaiting a first applicable result.";
}

function reasonLabel(reason: string | null | undefined): string {
  if (!reason) return "reason unavailable";
  const known: Record<string, string> = {
    report_missing: "missing report", reported_failure: "reported failure",
    status_mismatch: "HTTP status mismatch", text_missing: "required text missing",
    text_present: "forbidden text present", timeout: "request timeout",
  };
  return known[reason] ?? reason.replaceAll("_", " ");
}

function safeRunbook(value: string | null): string | undefined {
  if (!value) return undefined;
  try {
    const url = new URL(value);
    return url.protocol === "https:" || url.protocol === "http:" ? url.toString() : undefined;
  } catch { return undefined; }
}

function stateLabel(state: string): string {
  return state === "failing" ? "Failing" : state === "paused" ? "Paused" : state === "healthy" ? "Healthy" : "Untested";
}

function modeLabel(mode: string | null): string {
  return mode === "job_completion" ? "Job completion" : "State report";
}

function ageLabel(generatedAt: string, occurredAt: string): string {
  const seconds = Math.max(0, Math.floor((Date.parse(generatedAt) - Date.parse(occurredAt)) / 1000));
  if (!Number.isFinite(seconds)) return "unknown age";
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h`;
  return `${Math.floor(seconds / 86400)}d`;
}

function dateLabel(value: string): string {
  return new Date(value).toLocaleString();
}
