import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";

type Project = components["schemas"]["ProjectResponse"];
type Monitor = components["schemas"]["PushMonitorResponse"];
type CreateMonitor = components["schemas"]["CreatePushMonitorRequest"];
type UpdateMonitor = components["schemas"]["UpdatePushMonitorRequest"];
type Report = components["schemas"]["PushReportHistoryResponse"];
type Incident = components["schemas"]["PushIncidentHistoryResponse"];
type Credential = components["schemas"]["ReportingCredentialResponse"];
type IssuedCredential = components["schemas"]["IssuedReportingCredentialResponse"];

type Props = {
  project: Project;
  onBack: () => void;
  onOpenHttp: () => void;
  onSignedOut: () => void;
};

export function PushMonitorsView({ project, onBack, onOpenHttp, onSignedOut }: Props) {
  const [monitors, setMonitors] = useState<Monitor[]>([]);
  const [selectedKey, setSelectedKey] = useState<string>();
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string>();

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      const result = await api.GET("/api/projects/{projectKey}/push-monitors", {
        params: { path: { projectKey: project.key } },
      });
      if (result.data) setMonitors(result.data);
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch {
      setError("The push monitor list could not be reached.");
    } finally {
      setLoading(false);
    }
  }, [onSignedOut, project.key]);

  useEffect(() => {
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load]);

  async function create(body: CreateMonitor) {
    setCreating(true);
    setError(undefined);
    try {
      const result = await api.POST("/api/projects/{projectKey}/push-monitors", {
        params: { path: { projectKey: project.key } }, body, headers: csrfHeaders,
      });
      if (result.data) {
        await load();
        setSelectedKey(result.data.key);
      } else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch {
      setError("The push monitor could not be created.");
    } finally {
      setCreating(false);
    }
  }

  if (selectedKey) {
    return <PushMonitorDetail
      monitorKey={selectedKey}
      onBack={() => { setSelectedKey(undefined); void load(); }}
      onRemoved={() => { setSelectedKey(undefined); void load(); }}
      onSignedOut={onSignedOut}
      project={project}
    />;
  }

  return (
    <div className="workspace">
      <header className="workspace-header">
        <div>
          <p className="eyebrow">Project · {project.key}</p>
          <h1>{project.name}</h1>
          <p className="muted">Push monitors</p>
        </div>
        <div className="actions">
          <Button onClick={onOpenHttp} type="button">HTTP monitors</Button>
          <Button onClick={onBack} type="button">Back to projects</Button>
        </div>
      </header>

      <section aria-labelledby="push-create-title" className="panel">
        <h2 id="push-create-title">Create a push monitor</h2>
        <PushMonitorForm busy={creating} mode="create" onSubmit={(body) => create(body as CreateMonitor)} />
      </section>

      <section aria-labelledby="push-list-title" className="panel">
        <div className="section-heading">
          <div>
            <h2 id="push-list-title">Push monitors</h2>
            <p className="muted">State and reporting mode are always written out; color is only supporting emphasis.</p>
          </div>
          <Button disabled={loading} onClick={() => void load()} type="button">Refresh</Button>
        </div>
        {error && <div className="error" role="alert">{error}</div>}
        {loading && <p role="status">Loading push monitors…</p>}
        {!loading && monitors.length === 0 && <div className="empty" role="status">No push monitors yet.</div>}
        {!loading && monitors.length > 0 && <div className="monitor-list">
          {monitors.map((monitor) => <article className="monitor-card" key={monitor.id}>
            <div>
              <p className={`state state-${monitor.state}`}>{pushStateLabel(monitor)}</p>
              <h3>{monitor.name}</h3>
              <code>{monitor.key}</code>
              <p className="muted monitor-target">{modeLabel(monitor.mode)} · every {monitor.interval_seconds}s + {monitor.tolerance_seconds}s tolerance</p>
            </div>
            <div className="monitor-card-actions">
              <span className="version">version {monitor.version}</span>
              <Button onClick={() => setSelectedKey(monitor.key)} type="button">Open details</Button>
            </div>
          </article>)}
        </div>}
      </section>
    </div>
  );
}

type FormProps = {
  busy: boolean;
  mode: "create" | "edit";
  monitor?: Monitor;
  onSubmit: (body: CreateMonitor | UpdateMonitor) => Promise<void>;
};

function PushMonitorForm({ busy, mode, monitor, onSubmit }: FormProps) {
  const [key, setKey] = useState("");
  const [name, setName] = useState(monitor?.name ?? "");
  const [reportingMode, setReportingMode] = useState(monitor?.mode ?? "job_completion");
  const [interval, setInterval] = useState(monitor?.interval_seconds ?? 3600);
  const [tolerance, setTolerance] = useState(monitor?.tolerance_seconds ?? 300);
  const [instruction, setInstruction] = useState(monitor?.instruction ?? "");
  const [runbook, setRunbook] = useState(monitor?.runbook_url ?? "");

  async function submit(event: FormEvent) {
    event.preventDefault();
    const common = {
      name,
      interval_seconds: interval,
      tolerance_seconds: tolerance,
      instruction: instruction || null,
      runbook_url: runbook || null,
    };
    await onSubmit(mode === "create"
      ? { ...common, key, mode: reportingMode }
      : { ...common, version: monitor!.version });
  }

  return <form className="monitor-form" onSubmit={submit}>
    {mode === "create" && <label>
      <span>Immutable key</span>
      <input pattern="[a-z][a-z0-9-]{1,39}" placeholder="nightly-backup" required value={key} onChange={(event) => setKey(event.target.value)} />
    </label>}
    <label>
      <span>Display name</span>
      <input maxLength={100} required value={name} onChange={(event) => setName(event.target.value)} />
    </label>
    {mode === "create" && <label>
      <span>Reporting mode</span>
      <select value={reportingMode} onChange={(event) => setReportingMode(event.target.value)}>
        <option value="job_completion">Job completion</option>
        <option value="state_report">State report</option>
      </select>
    </label>}
    {mode === "create" && <p className="mode-explanation wide-field" role="note">
      {reportingMode === "job_completion"
        ? "Job completion expects each successful run to report. An explicit failure opens an incident but does not postpone the next success deadline."
        : "State report expects the sender’s latest health assessment. Both success and explicit failure start a fresh reporting window."}
      {" "}Silence after the interval and tolerance is a separate missing-report failure.
    </p>}
    {mode === "edit" && <div className="wide-field"><span className="muted">Immutable mode</span><p>{modeLabel(monitor!.mode)}</p></div>}
    <label>
      <span>Expected interval (seconds)</span>
      <input min={30} max={2592000} required type="number" value={interval} onChange={(event) => setInterval(event.target.valueAsNumber)} />
    </label>
    <label>
      <span>Deadline tolerance (seconds)</span>
      <input min={0} max={2592000} required type="number" value={tolerance} onChange={(event) => setTolerance(event.target.valueAsNumber)} />
    </label>
    <label className="wide-field">
      <span>Operator instruction</span>
      <textarea maxLength={1000} value={instruction} onChange={(event) => setInstruction(event.target.value)} />
    </label>
    <label className="wide-field">
      <span>Runbook URL</span>
      <input maxLength={2048} type="url" value={runbook} onChange={(event) => setRunbook(event.target.value)} />
    </label>
    <Button disabled={busy} type="submit">{busy ? "Saving…" : mode === "create" ? "Create push monitor" : "Save configuration"}</Button>
  </form>;
}

type DetailProps = {
  project: Project;
  monitorKey: string;
  onBack: () => void;
  onRemoved: () => void;
  onSignedOut: () => void;
};

function PushMonitorDetail({ project, monitorKey, onBack, onRemoved, onSignedOut }: DetailProps) {
  const [monitor, setMonitor] = useState<Monitor>();
  const [reports, setReports] = useState<Report[]>([]);
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [credential, setCredential] = useState<Credential>();
  const [revealed, setRevealed] = useState<IssuedCredential>();
  const [nextReportCursor, setNextReportCursor] = useState<number | null>(null);
  const [nextIncidentCursor, setNextIncidentCursor] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [confirmRemove, setConfirmRemove] = useState(false);
  const paths = useMemo(() => ({ projectKey: project.key, monitorKey }), [monitorKey, project.key]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    setRevealed(undefined);
    try {
      const [detail, reportPage, incidentPage] = await Promise.all([
        api.GET("/api/projects/{projectKey}/push-monitors/{monitorKey}", { params: { path: paths } }),
        api.GET("/api/projects/{projectKey}/push-monitors/{monitorKey}/reports", { params: { path: paths, query: { limit: 20 } } }),
        api.GET("/api/projects/{projectKey}/push-monitors/{monitorKey}/incidents", { params: { path: paths, query: { limit: 20 } } }),
      ]);
      if ([detail, reportPage, incidentPage].some((result) => result.response.status === 401)) {
        onSignedOut();
        return;
      }
      if (!detail.data) setError(problemMessage(detail.error, detail.response.status));
      else if (!reportPage.data) setError(problemMessage(reportPage.error, reportPage.response.status));
      else if (!incidentPage.data) setError(problemMessage(incidentPage.error, incidentPage.response.status));
      else {
        setMonitor(detail.data);
        setReports(reportPage.data.items);
        setIncidents(incidentPage.data.items);
        setNextReportCursor(reportPage.data.next_before_sequence);
        setNextIncidentCursor(incidentPage.data.next_before_opening_sequence);
        if (detail.data.has_reporting_credential) {
          const metadata = await api.GET("/api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential", { params: { path: paths } });
          if (metadata.data) setCredential(metadata.data);
          else if (metadata.response.status === 401) onSignedOut();
          else setError(problemMessage(metadata.error, metadata.response.status));
        } else setCredential(undefined);
      }
    } catch {
      setError("The push monitor detail could not be reached.");
    } finally {
      setLoading(false);
    }
  }, [onSignedOut, paths]);

  useEffect(() => {
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load]);

  async function failure(errorValue: unknown, status: number) {
    if (status === 401) { onSignedOut(); return; }
    const message = problemMessage(errorValue, status);
    if (status === 409) await load();
    setError(message);
  }

  async function lifecycle(operation: "pause" | "resume" | "remove") {
    if (!monitor) return;
    setBusy(operation); setError(undefined); setNotice(undefined); setRevealed(undefined);
    try {
      if (operation === "remove") {
        const result = await api.DELETE("/api/projects/{projectKey}/push-monitors/{monitorKey}", {
          params: { path: paths, query: { version: String(monitor.version) } }, headers: csrfHeaders,
        });
        if (result.data) onRemoved(); else await failure(result.error, result.response.status);
      } else {
        const body = { version: monitor.version };
        const result = operation === "pause"
          ? await api.POST("/api/projects/{projectKey}/push-monitors/{monitorKey}/pause", { params: { path: paths }, body, headers: csrfHeaders })
          : await api.POST("/api/projects/{projectKey}/push-monitors/{monitorKey}/resume", { params: { path: paths }, body, headers: csrfHeaders });
        if (result.data) {
          setMonitor(result.data);
          setNotice(operation === "pause" ? "Push monitor paused; its current incident remains visible." : "Push monitor resumed with a fresh reporting deadline.");
        } else await failure(result.error, result.response.status);
      }
    } catch {
      setError("The push monitor change could not be completed.");
    } finally { setBusy(undefined); }
  }

  async function update(body: CreateMonitor | UpdateMonitor) {
    setBusy("update"); setError(undefined); setNotice(undefined); setRevealed(undefined);
    try {
      const result = await api.PUT("/api/projects/{projectKey}/push-monitors/{monitorKey}", {
        params: { path: paths }, body: body as UpdateMonitor, headers: csrfHeaders,
      });
      if (result.data) { setMonitor(result.data); setNotice("Configuration saved."); }
      else await failure(result.error, result.response.status);
    } catch { setError("The push monitor configuration could not be saved."); }
    finally { setBusy(undefined); }
  }

  async function changeCredential(operation: "issue" | "rotate" | "revoke") {
    setBusy(`credential-${operation}`); setError(undefined); setNotice(undefined); setRevealed(undefined);
    try {
      if (operation === "revoke") {
        const result = await api.DELETE("/api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential", { params: { path: paths }, headers: csrfHeaders });
        if (result.response.status === 204) {
          setCredential(undefined);
          setMonitor((current) => current ? { ...current, has_reporting_credential: false } : current);
          setNotice("Reporting credential revoked. Reports using it are now rejected.");
        } else await failure(result.error, result.response.status);
      } else {
        const result = operation === "issue"
          ? await api.POST("/api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential", { params: { path: paths }, headers: csrfHeaders })
          : await api.POST("/api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential/rotate", { params: { path: paths }, headers: csrfHeaders });
        if (result.data) {
          setCredential({ id: result.data.id, created_at: result.data.created_at, rotated_at: result.data.rotated_at, revoked_at: null });
          setMonitor((current) => current ? { ...current, has_reporting_credential: true } : current);
          setRevealed(result.data);
        } else await failure(result.error, result.response.status);
      }
    } catch { setError("The reporting credential change could not be completed."); }
    finally { setBusy(undefined); }
  }

  async function moreReports() {
    if (nextReportCursor === null) return;
    setBusy("reports"); setError(undefined);
    try {
      const result = await api.GET("/api/projects/{projectKey}/push-monitors/{monitorKey}/reports", { params: { path: paths, query: { before_sequence: nextReportCursor, limit: 20 } } });
      if (result.data) { setReports((current) => [...current, ...result.data!.items]); setNextReportCursor(result.data.next_before_sequence); }
      else await failure(result.error, result.response.status);
    } catch { setError("Older reports could not be reached."); }
    finally { setBusy(undefined); }
  }

  async function moreIncidents() {
    if (nextIncidentCursor === null) return;
    setBusy("incidents"); setError(undefined);
    try {
      const result = await api.GET("/api/projects/{projectKey}/push-monitors/{monitorKey}/incidents", { params: { path: paths, query: { before_opening_sequence: nextIncidentCursor, limit: 20 } } });
      if (result.data) { setIncidents((current) => [...current, ...result.data!.items]); setNextIncidentCursor(result.data.next_before_opening_sequence); }
      else await failure(result.error, result.response.status);
    } catch { setError("Older incidents could not be reached."); }
    finally { setBusy(undefined); }
  }

  if (loading && !monitor) return <div className="workspace"><p role="status">Loading push monitor detail…</p></div>;
  if (!monitor) return <div className="workspace panel">
    <div className="error" role="alert">{error ?? "The push monitor is unavailable."}</div>
    <div className="actions"><Button onClick={() => void load()} type="button">Try again</Button><Button onClick={onBack} type="button">Back to push monitors</Button></div>
  </div>;

  const latest = reports.find((report) => report.id === monitor.latest_report_id);
  const lastSuccess = reports.find((report) => report.id === monitor.latest_success_id);

  return <div className="workspace">
    <header className="workspace-header">
      <div>
        <p className="eyebrow">{project.key} · {monitor.key}</p>
        <h1>{monitor.name}</h1>
        <p className={`state state-${monitor.state}`}>{pushStateLabel(monitor)}</p>
      </div>
      <Button onClick={onBack} type="button">Back to push monitors</Button>
    </header>

    {error && <div className="error" role="alert">{error}</div>}
    {notice && <div className="notice" role="status">{notice}</div>}

    <section aria-labelledby="push-status-title" className="panel">
      <div className="section-heading">
        <div><h2 id="push-status-title">Current status</h2><p className="muted">{modeLabel(monitor.mode)}</p></div>
        <div className="actions">
          {monitor.state === "paused"
            ? <Button disabled={busy !== undefined} onClick={() => void lifecycle("resume")} type="button">Resume</Button>
            : <Button disabled={busy !== undefined} onClick={() => void lifecycle("pause")} type="button">Pause</Button>}
          <Button disabled={busy !== undefined || loading} onClick={() => void load()} type="button">Refresh</Button>
        </div>
      </div>
      <dl className="fact-grid">
        <Fact label="Mode" value={modeLabel(monitor.mode)} />
        <Fact label="Last report" value={latest ? `${latest.outcome} · ${formatDate(latest.received_at)}` : monitor.latest_report_id ?? "No report yet"} />
        <Fact label="Last success" value={lastSuccess ? formatDate(lastSuccess.observed_at) : monitor.latest_success_id ?? "No success yet"} />
        <Fact label="Last received" value={monitor.last_received_at ? formatDate(monitor.last_received_at) : "Nothing received"} />
        <Fact label="Next deadline" value={monitor.next_deadline_at ? formatDate(monitor.next_deadline_at) : "No active deadline"} />
        <Fact label="Open incident" value={monitor.open_incident_id ?? "None"} />
        <Fact label="Interval + tolerance" value={`${monitor.interval_seconds}s + ${monitor.tolerance_seconds}s`} />
        <Fact label="Version" value={String(monitor.version)} />
      </dl>
      {monitor.instruction && <div className="operator-note"><strong>Operator instruction</strong><p>{monitor.instruction}</p></div>}
      {monitor.runbook_url && <p><a href={monitor.runbook_url} rel="noreferrer" target="_blank">Open runbook</a></p>}
    </section>

    <section aria-labelledby="push-configuration-title" className="panel">
      <h2 id="push-configuration-title">Configuration</h2>
      <PushMonitorForm busy={busy === "update"} key={monitor.version} mode="edit" monitor={monitor} onSubmit={update} />
    </section>

    <section aria-labelledby="credential-title" className="panel">
      <h2 id="credential-title">Reporting credential</h2>
      <p className="muted">This credential can report only to this monitor. Its token and secret URL are available during one handoff only.</p>
      {revealed && <div className="secret-reveal" role="status">
        <strong>Save these now — they will not be shown again.</strong>
        <dl>
          <Fact label="Reporting token" value={revealed.token} />
          <Fact label="Secret report URL" value={revealed.report_url} />
          <Fact label="Previous token valid until" value={revealed.previous_valid_until ? formatDate(revealed.previous_valid_until) : "Not applicable"} />
        </dl>
        <Button onClick={() => setRevealed(undefined)} type="button">I have saved them</Button>
      </div>}
      {credential ? <>
        <dl className="fact-grid">
          <Fact label="Credential ID" value={credential.id} />
          <Fact label="Created" value={formatDate(credential.created_at)} />
          <Fact label="Rotated" value={credential.rotated_at ? formatDate(credential.rotated_at) : "Never"} />
          <Fact label="Status" value={credential.revoked_at ? "Revoked" : "Active"} />
        </dl>
        <div className="actions credential-actions">
          <Button disabled={busy !== undefined} onClick={() => void changeCredential("rotate")} type="button">Rotate and reveal new credential</Button>
          <Button className="danger" disabled={busy !== undefined} onClick={() => void changeCredential("revoke")} type="button">Revoke credential</Button>
        </div>
      </> : <div className="empty">No active reporting credential.</div>}
      {!credential && <Button disabled={busy !== undefined} onClick={() => void changeCredential("issue")} type="button">Issue reporting credential</Button>}
    </section>

    <section aria-labelledby="push-reports-title" className="panel">
      <h2 id="push-reports-title">Report history</h2>
      {reports.length === 0 ? <div className="empty">No reports received yet.</div> : <ol className="history-list">
        {reports.map((report) => <li key={report.id}>
          <strong>{report.outcome === "success" ? "Successful report" : report.reason === "report_missing" ? "Missing report" : "Failed report"}</strong> · observed {formatDate(report.observed_at)}
          <span>received {formatDate(report.received_at)} · sequence {report.sequence} · reason {report.reason ?? "none"} · {report.applicable ? "applied to current state" : "retained but not applied"}</span>
        </li>)}
      </ol>}
      {nextReportCursor !== null && <Button disabled={busy !== undefined} onClick={() => void moreReports()} type="button">Load older reports</Button>}
    </section>

    <section aria-labelledby="push-incidents-title" className="panel">
      <h2 id="push-incidents-title">Incident history</h2>
      {incidents.length === 0 ? <div className="empty">No incidents.</div> : <ol className="history-list">
        {incidents.map((incident) => <li key={incident.id}>
          <strong>{incident.resolved_at ? "Resolved incident" : "Open incident"}</strong> · opened {formatDate(incident.opened_at)}
          <span>original reason {incident.original_reason} · latest reason {incident.latest_reason}{incident.resolved_at ? ` · resolved ${formatDate(incident.resolved_at)}` : ""}</span>
        </li>)}
      </ol>}
      {nextIncidentCursor !== null && <Button disabled={busy !== undefined} onClick={() => void moreIncidents()} type="button">Load older incidents</Button>}
    </section>

    <section aria-labelledby="push-remove-title" className="panel danger-zone">
      <h2 id="push-remove-title">Remove push monitor</h2>
      <p className="muted">Removal revokes its reporting credential and retains its history and key.</p>
      {!confirmRemove
        ? <Button className="danger" onClick={() => setConfirmRemove(true)} type="button">Remove push monitor…</Button>
        : <div className="actions confirmation" role="group" aria-label="Confirm push monitor removal">
          <span>Remove {monitor.name}?</span>
          <Button className="danger" disabled={busy !== undefined} onClick={() => void lifecycle("remove")} type="button">Confirm removal</Button>
          <Button disabled={busy !== undefined} onClick={() => setConfirmRemove(false)} type="button">Cancel</Button>
        </div>}
    </section>
  </div>;
}

function Fact({ label, value }: { label: string; value: string }) {
  return <div><dt>{label}</dt><dd>{value}</dd></div>;
}

function modeLabel(mode: string): string {
  return mode === "job_completion" ? "Job completion" : "State report";
}

function pushStateLabel(monitor: Monitor): string {
  if (monitor.state === "paused") return monitor.open_incident_id ? "Paused · incident remains open" : "Paused";
  if (monitor.open_incident_id) return monitor.state === "untested" ? "Untested · incident remains open" : "Incident open";
  if (monitor.state === "healthy") return "Healthy";
  if (monitor.state === "failing") return "Failing";
  return "Untested";
}

function formatDate(value: string): string {
  return new Date(value).toLocaleString();
}
