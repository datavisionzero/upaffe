import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type RefObject } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemFieldErrors, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { DiscardDialog } from "@/components/DiscardGuard";
import { useDiscardGuard } from "@/components/useDiscardGuard";
import { SelectField, TextAreaField, TextField } from "@/components/Fields";
import { Alert, EmptyState, LoadingState, PageHeader, SectionHeading, StatusBadge } from "@/components/Presentation";
import { monitorStateTone } from "@/components/status";
import { DeliveryHistoryPanel, IncidentEmailPanel, MaintenancePanel } from "@/shell/EmailPanels";
import { monitorLink } from "@/shell/deepLink";
import { monitorPath } from "@/shell/routes";

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
  onOpenHttp: () => void;
  onSignedOut: () => void;
  routeMonitorKey?: string;
  onOpenMonitor?: (key: string) => void;
  onBackToList?: () => void;
  onCreate?: () => void;
};

export function PushMonitorsView({ project, onOpenHttp, onSignedOut,
  routeMonitorKey, onOpenMonitor, onBackToList, onCreate }: Props) {
  const [monitors, setMonitors] = useState<Monitor[]>([]);
  const [selectedKey, setSelectedKey] = useState<string | undefined>(() => {
    if (onOpenMonitor) return undefined;
    const link = monitorLink();
    return link?.projectKey === project.key && link.monitorType === "push" ? link.monitorKey : undefined;
  });
  const [creatingInline, setCreatingInline] = useState(false);
  const [loading, setLoading] = useState(true);
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

  function open(key: string) {
    if (onOpenMonitor) onOpenMonitor(key);
    else setSelectedKey(key);
  }

  function create() {
    if (onCreate) onCreate();
    else setCreatingInline(true);
  }

  if (creatingInline) {
    return <NewPushMonitorView project={project} onSignedOut={onSignedOut}
      onCancel={() => setCreatingInline(false)}
      onCreated={(key) => { setCreatingInline(false); setSelectedKey(key); void load(); }} />;
  }

  const activeKey = routeMonitorKey ?? selectedKey;
  if (activeKey) {
    return <PushMonitorDetail
      monitorKey={activeKey}
      onBack={() => { if (onBackToList) onBackToList(); else { setSelectedKey(undefined); void load(); } }}
      onRemoved={() => { if (onBackToList) onBackToList(); else { setSelectedKey(undefined); void load(); } }}
      onSignedOut={onSignedOut}
      project={project}
    />;
  }

  return (
    <div className="workspace monitor-workspace">
      <PageHeader title="Push monitors" detail={`${project.name} · ${project.key} · Jobs and services that report to upaffe.`} actions={<>
          <Button onClick={onOpenHttp} type="button">HTTP monitors</Button>
          <Button onClick={create} type="button" variant="primary">New push monitor</Button>
        </>} />

      <section aria-labelledby="push-list-title" className="panel">
        <SectionHeading title="Inventory" titleId="push-list-title" eyebrow={loading || error ? undefined : countLabel(monitors.length)}
          action={<Button disabled={loading} onClick={() => void load()} type="button">Refresh</Button>} />
        {error && <Alert tone="danger">{error} <Button onClick={() => void load()} type="button">Try again</Button></Alert>}
        {loading && <LoadingState>Loading push monitors…</LoadingState>}
        {!loading && !error && monitors.length === 0 && <div className="inventory-empty">
          <EmptyState>No push monitors in {project.name} yet. A push monitor waits for a job or service to report and alerts when a report fails or does not arrive.</EmptyState>
          <Button onClick={create} type="button" variant="primary">Create the first push monitor</Button>
        </div>}
        {!loading && monitors.length > 0 && <div className="monitor-list">
          {monitors.map((monitor) => <article className="monitor-card" key={monitor.id}>
            <div>
              <StatusBadge tone={monitorStateTone(monitor.state)}>{pushStateLabel(monitor)}</StatusBadge>
              <h3><a href={monitorPath(project.key, "push", monitor.key)} onClick={(event) => {
                if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
                event.preventDefault();
                open(monitor.key);
              }}>{monitor.name}</a></h3>
              <code>{monitor.key}</code>
              <p className="muted monitor-target">{modeLabel(monitor.mode)} · every {monitor.interval_seconds}s + {monitor.tolerance_seconds}s tolerance</p>
            </div>
            <div className="monitor-card-actions">
              <span className="version">version {monitor.version}</span>
              <Button onClick={() => open(monitor.key)} type="button">Open details</Button>
            </div>
          </article>)}
        </div>}
      </section>
    </div>
  );
}

function countLabel(count: number) {
  return count === 1 ? "1 push monitor" : `${count} push monitors`;
}

/** The focused creation workflow; the created monitor opens next. */
export function NewPushMonitorView({ project, onCancel, onCreated, onSignedOut }: {
  project: Project;
  onCancel: () => void;
  onCreated: (key: string) => void;
  onSignedOut: () => void;
}) {
  const [pending, setPending] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState<string>();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const firstField = useRef<HTMLInputElement>(null);
  const guard = useDiscardGuard({ dirty, pending, onLeave: onCancel });

  async function create(body: CreateMonitor) {
    setPending(true);
    setError(undefined);
    setFieldErrors({});
    try {
      const result = await api.POST("/api/projects/{projectKey}/push-monitors", {
        params: { path: { projectKey: project.key } }, body, headers: csrfHeaders,
      });
      if (result.data) onCreated(result.data.key);
      else if (result.response.status === 401) onSignedOut();
      else {
        setFieldErrors(problemFieldErrors(result.error));
        setError(problemMessage(result.error, result.response.status));
      }
    } catch {
      setError("The push monitor could not be created.");
    } finally {
      setPending(false);
    }
  }

  return <div className="workspace monitor-workspace monitor-create-workspace">
    <PageHeader title="New push monitor" detail={`${project.name} · ${project.key} · Choose how the sender reports; issue its reporting credential after creation.`} />
    {error && <Alert tone="danger">{error}</Alert>}
    <PushMonitorForm busy={pending} fieldErrors={fieldErrors} firstField={firstField} mode="create"
      onCancel={guard.cancel} onDirtyChange={setDirty} onSubmit={(body) => create(body as CreateMonitor)} />
    <DiscardDialog description="The push monitor details you entered will be lost." onDiscard={onCancel}
      onOpenChange={guard.setOpen} open={guard.open} returnFocus={firstField} title="Discard new push monitor?" />
  </div>;
}

type FormProps = {
  busy: boolean;
  mode: "create" | "edit";
  monitor?: Monitor;
  fieldErrors?: Record<string, string>;
  firstField?: RefObject<HTMLInputElement | null>;
  onCancel?: () => void;
  onDirtyChange?: (dirty: boolean) => void;
  onSubmit: (body: CreateMonitor | UpdateMonitor) => Promise<void>;
};

function PushMonitorForm({ busy, mode, monitor, fieldErrors = {}, firstField, onCancel, onDirtyChange, onSubmit }: FormProps) {
  const [key, setKey] = useState("");
  const [name, setName] = useState(monitor?.name ?? "");
  const [purpose, setPurpose] = useState(monitor?.purpose ?? "");
  const [reportingMode, setReportingMode] = useState(monitor?.mode ?? "job_completion");
  const [interval, setInterval] = useState(monitor?.interval_seconds ?? 3600);
  const [tolerance, setTolerance] = useState(monitor?.tolerance_seconds ?? 300);
  const [instruction, setInstruction] = useState(monitor?.instruction ?? "");
  const [runbook, setRunbook] = useState(monitor?.runbook_url ?? "");
  const dirty = key !== "" || name !== (monitor?.name ?? "") || purpose !== (monitor?.purpose ?? "")
    || reportingMode !== (monitor?.mode ?? "job_completion") || interval !== (monitor?.interval_seconds ?? 3600)
    || tolerance !== (monitor?.tolerance_seconds ?? 300) || instruction !== (monitor?.instruction ?? "")
    || runbook !== (monitor?.runbook_url ?? "");
  useEffect(() => { onDirtyChange?.(dirty); }, [dirty, onDirtyChange]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const common = {
      name,
      purpose: purpose.trim() || null,
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
    {mode === "create" && <TextField autoFocus description="Lowercase letters, numbers, and hyphens. This cannot be changed later."
      error={fieldErrors.key} label="Immutable key" pattern="[a-z][a-z0-9-]{1,39}" placeholder="nightly-backup" ref={firstField}
      required value={key} onChange={(event) => setKey(event.target.value)} />}
    <TextField error={fieldErrors.name} label="Display name" maxLength={100} required value={name} onChange={(event) => setName(event.target.value)} />
    <TextAreaField className="wide-field" description="What this reports, for example “Confirms the nightly backup completes.” Operator instruction below is for investigation steps."
      error={fieldErrors.purpose} label="Purpose (optional)" maxLength={240} rows={2} value={purpose} onChange={(event) => setPurpose(event.target.value)} />
    {mode === "create" && <SelectField error={fieldErrors.mode} label="Reporting mode" value={reportingMode} onChange={(event) => setReportingMode(event.target.value)}>
        <option value="job_completion">Job completion</option>
        <option value="state_report">State report</option>
      </SelectField>}
    {mode === "create" && <p className="mode-explanation wide-field" role="note">
      {reportingMode === "job_completion"
        ? "Job completion expects each successful run to report. An explicit failure opens an incident but does not postpone the next success deadline."
        : "State report expects the sender’s latest health assessment. Both success and explicit failure start a fresh reporting window."}
      {" "}Silence after the interval and tolerance is a separate missing-report failure.
    </p>}
    {mode === "edit" && <div className="wide-field"><span className="muted">Immutable mode</span><p>{modeLabel(monitor!.mode)}</p></div>}
    <TextField error={fieldErrors.interval_seconds} label="Expected interval (seconds)" max={2592000} min={30} required type="number"
      value={Number.isNaN(interval) ? "" : interval} onChange={(event) => setInterval(event.target.valueAsNumber)} />
    <TextField error={fieldErrors.tolerance_seconds} label="Deadline tolerance (seconds)" max={2592000} min={0} required type="number"
      value={Number.isNaN(tolerance) ? "" : tolerance} onChange={(event) => setTolerance(event.target.valueAsNumber)} />
    <TextAreaField className="wide-field" error={fieldErrors.instruction} label="Operator instruction" maxLength={1000}
      value={instruction} onChange={(event) => setInstruction(event.target.value)} />
    <TextField className="wide-field" error={fieldErrors.runbook_url} label="Runbook URL" maxLength={2048} type="url"
      value={runbook} onChange={(event) => setRunbook(event.target.value)} />
    {mode === "create"
      ? <div className="actions wide-field form-actions">
          <Button disabled={busy} onClick={onCancel} type="button">Cancel</Button>
          <Button pending={busy} type="submit" variant="primary">{busy ? "Creating…" : "Create push monitor"}</Button>
        </div>
      : <Button disabled={busy} type="submit" variant="primary">{busy ? "Saving…" : "Save configuration"}</Button>}
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
  const [pointerReports, setPointerReports] = useState<Report[]>([]);
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [pointerIncidents, setPointerIncidents] = useState<Incident[]>([]);
  const [snapshotAt, setSnapshotAt] = useState<number>();
  const [credential, setCredential] = useState<Credential>();
  const [revealed, setRevealed] = useState<IssuedCredential>();
  const [nextReportCursor, setNextReportCursor] = useState<number | null>(null);
  const [nextIncidentCursor, setNextIncidentCursor] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [emailIncidentId, setEmailIncidentId] = useState<string | undefined>(() => {
    const link = monitorLink();
    return link?.projectKey === project.key && link.monitorType === "push" && link.monitorKey === monitorKey ? link.incidentId : undefined;
  });
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
        const missingReports = [...new Set([detail.data.latest_report_id, detail.data.latest_success_id]
          .filter((id): id is string => !!id && !reportPage.data!.items.some((report) => report.id === id)))];
        const linked = monitorLink();
        const linkedId = linked?.projectKey === project.key && linked.monitorType === "push"
          && linked.monitorKey === monitorKey ? linked.incidentId : undefined;
        const missingIncidents = [...new Set([detail.data.open_incident_id, linkedId]
          .filter((id): id is string => !!id && !incidentPage.data!.items.some((incident) => incident.id === id)))];
        const [reportEvidence, incidentEvidence] = await Promise.all([
          Promise.all(missingReports.map((reportId) => api.GET(
            "/api/projects/{projectKey}/push-monitors/{monitorKey}/reports/{reportId}",
            { params: { path: { ...paths, reportId } } }))),
          Promise.all(missingIncidents.map((incidentId) => api.GET(
            "/api/projects/{projectKey}/push-monitors/{monitorKey}/incidents/{incidentId}",
            { params: { path: { ...paths, incidentId } } }))),
        ]);
        if ([...reportEvidence, ...incidentEvidence].some((result) => result.response.status === 401)) {
          onSignedOut();
          return;
        }
        setMonitor(detail.data);
        setSnapshotAt(Date.now());
        setReports(reportPage.data.items);
        setPointerReports(reportEvidence.flatMap((result) => result.data ? [result.data] : []));
        setIncidents(incidentPage.data.items);
        setPointerIncidents(incidentEvidence.flatMap((result) => result.data ? [result.data] : []));
        setNextReportCursor(reportPage.data.next_before_sequence);
        setNextIncidentCursor(incidentPage.data.next_before_opening_sequence);
        const unavailable = [...reportEvidence, ...incidentEvidence].find((result) =>
          !result.data && result.response.status !== 404);
        if (unavailable) setError(problemMessage(unavailable.error, unavailable.response.status));
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
  }, [onSignedOut, paths, project.key, monitorKey]);

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

  if (loading && !monitor) return <div className="workspace"><LoadingState>Loading push monitor detail…</LoadingState></div>;
  if (!monitor) return <div className="workspace panel">
    <h1>Monitor unavailable</h1>
    <Alert tone="danger">{error ?? "The push monitor is unavailable."}</Alert>
    <div className="actions"><Button onClick={() => void load()} type="button">Try again</Button><Button onClick={onBack} type="button">Back to push monitors</Button></div>
  </div>;

  const evidence = [...reports, ...pointerReports];
  const latest = evidence.find((report) => report.id === monitor.latest_report_id);
  const lastSuccess = evidence.find((report) => report.id === monitor.latest_success_id);
  const openIncident = [...incidents, ...pointerIncidents].find((incident) =>
    incident.id === monitor.open_incident_id);
  const linkedIncident = [...incidents, ...pointerIncidents].find((incident) =>
    incident.id === emailIncidentId);
  const overdue = monitor.state !== "paused" && monitor.next_deadline_at !== null
    && snapshotAt !== undefined && Date.parse(monitor.next_deadline_at) < snapshotAt;

  return <div className="workspace monitor-workspace">
    <PageHeader title={monitor.name} detail={<>{project.key} · {monitor.key} · <span className="monitor-purpose">{monitor.purpose || <>Purpose not documented. <a href="#push-configuration-title">Add purpose</a></>}</span></>}
      actions={<><StatusBadge tone={monitorStateTone(monitor.state)}>{pushStateLabel(monitor)}</StatusBadge><Button onClick={onBack} type="button">Back to push monitors</Button></>} />

    {error && <Alert tone="danger">{error}</Alert>}
    {notice && <Alert>{notice}</Alert>}
    <section aria-labelledby="push-status-title" className="panel">
      <SectionHeading title="Current status" titleId="push-status-title" eyebrow={modeLabel(monitor.mode)} action={<div className="actions">
          {monitor.state === "paused"
            ? <Button disabled={busy !== undefined} onClick={() => void lifecycle("resume")} type="button">Resume</Button>
            : <Button disabled={busy !== undefined} onClick={() => void lifecycle("pause")} type="button">Pause</Button>}
          <Button disabled={busy !== undefined || loading} onClick={() => void load()} type="button">Refresh</Button>
        </div>} />
      <dl className="fact-grid">
        <Fact label="Mode" value={modeLabel(monitor.mode)} />
        <Fact label="Latest report" value={latest ? `${latest.outcome} · observed ${formatDate(latest.observed_at)} · received ${formatDate(latest.received_at)} · ${latest.reason ?? "no failure reason"}` : monitor.latest_report_id ? "Evidence unavailable" : "No report yet"} />
        <Fact label="Last success" value={lastSuccess ? `Observed ${formatDate(lastSuccess.observed_at)}; received ${formatDate(lastSuccess.received_at)}` : monitor.latest_success_id ? "Evidence unavailable" : "No success yet"} />
        <Fact label="Last received" value={monitor.last_received_at ? formatDate(monitor.last_received_at) : "Nothing received"} />
        <Fact label="Next deadline" value={monitor.next_deadline_at ? formatDate(monitor.next_deadline_at) : "No active deadline"} />
        <Fact label="Reporting at refresh" value={overdue ? "Report deadline passed; missing-report evaluation may still be pending" : monitor.state === "paused" ? "Paused; no active deadline" : "Deadline had not passed"} />
        <Fact label="Open incident" value={openIncident ? `Began ${formatDate(openIncident.began_at)}; opened ${formatDate(openIncident.opened_at)}; latest observation ${formatDate(openIncident.last_observed_at)}; reason ${openIncident.latest_reason}` : monitor.open_incident_id ? "Incident evidence unavailable" : "None"} />
        <Fact label="Interval + tolerance" value={`${monitor.interval_seconds}s + ${monitor.tolerance_seconds}s`} />
        <Fact label="Version" value={String(monitor.version)} />
      </dl>
      {monitor.instruction && <div className="operator-note"><strong>Operator instruction</strong><p>{monitor.instruction}</p></div>}
      {monitor.runbook_url && <p><a href={monitor.runbook_url} rel="noreferrer" target="_blank">Open runbook</a></p>}
    </section>

    <MaintenancePanel projectKey={project.key} monitorType="push" monitorKey={monitorKey} onSignedOut={onSignedOut} />

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
          <Button variant="destructive" disabled={busy !== undefined} onClick={() => void changeCredential("revoke")} type="button">Revoke credential</Button>
        </div>
      </> : <EmptyState>No active reporting credential.</EmptyState>}
      {!credential && <Button disabled={busy !== undefined} onClick={() => void changeCredential("issue")} type="button">Issue reporting credential</Button>}
    </section>

    <section aria-labelledby="push-reports-title" className="panel">
      <h2 id="push-reports-title">Report history</h2>
      {reports.length === 0 ? <EmptyState>No reports received yet.</EmptyState> : <ol className="history-list">
        {reports.map((report) => <li key={report.id}>
          <strong>{report.outcome === "success"
            ? incidents.some((incident) => incident.resolution_report_id === report.id) ? "Recovery" : "Successful report"
            : report.reason === "report_missing" ? "Missing report" : "Failed report"}</strong> · observed {formatDate(report.observed_at)}
          <span>received {formatDate(report.received_at)} · sequence {report.sequence} · reason {report.reason ?? "none"} · {report.applicable ? "applied to state" : "retained, not applied"}</span>
        </li>)}
      </ol>}
      {nextReportCursor !== null && <Button disabled={busy !== undefined} onClick={() => void moreReports()} type="button">Load older reports</Button>}
    </section>

    <section aria-labelledby="push-incidents-title" className="panel">
      <h2 id="push-incidents-title">Incident history</h2>
      {incidents.length === 0 ? <EmptyState>No incidents.</EmptyState> : <ol className="history-list">
        {incidents.map((incident) => <li key={incident.id}>
          <strong>{incident.resolved_at ? "Resolved incident" : "Open incident"}</strong> · began {formatDate(incident.began_at)} · opened {formatDate(incident.opened_at)}
          <span>original reason {incident.original_reason} · latest reason {incident.latest_reason} · last observed {formatDate(incident.last_observed_at)}{incident.resolved_at ? ` · recovered ${formatDate(incident.resolved_at)}` : ""}</span>
          <Button onClick={() => setEmailIncidentId(incident.id)} type="button">Email status for incident</Button>
        </li>)}
      </ol>}
      {nextIncidentCursor !== null && <Button disabled={busy !== undefined} onClick={() => void moreIncidents()} type="button">Load older incidents</Button>}
    </section>

    {emailIncidentId && <section className="panel" aria-label="Selected incident">
      <h2>Selected incident</h2>
      <p>{linkedIncident
        ? `${linkedIncident.resolved_at ? "Resolved" : "Open"} incident began ${formatDate(linkedIncident.began_at)}, opened ${formatDate(linkedIncident.opened_at)}; latest reason ${linkedIncident.latest_reason}.`
        : "Incident history is unavailable for this link."}</p>
    </section>}

    {emailIncidentId && <IncidentEmailPanel key={emailIncidentId} incidentId={emailIncidentId} monitorType="push" onSignedOut={onSignedOut} />}
    <DeliveryHistoryPanel projectKey={project.key} monitorType="push" monitorKey={monitorKey} onSignedOut={onSignedOut} />

    <section aria-labelledby="push-remove-title" className="panel danger-zone">
      <h2 id="push-remove-title">Remove push monitor</h2>
      <p className="muted">Removal revokes its reporting credential and retains its history and key.</p>
      <ConfirmDialog confirmLabel="Confirm removal"
        description="The reporting credential is revoked immediately. History and the monitor key are retained."
        onConfirm={() => lifecycle("remove")} pending={busy !== undefined}
        title={`Remove ${monitor.name}?`} triggerLabel="Remove push monitor…" />
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
