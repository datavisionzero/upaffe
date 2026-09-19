import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { DeliveryHistoryPanel, IncidentEmailPanel, MaintenancePanel } from "@/shell/EmailPanels";
import { monitorLink } from "@/shell/deepLink";

type Project = components["schemas"]["ProjectResponse"];
type Monitor = components["schemas"]["HttpMonitorResponse"];
type CreateMonitor = components["schemas"]["CreateHttpMonitorRequest"];
type UpdateMonitor = components["schemas"]["UpdateHttpMonitorRequest"];
type Check = components["schemas"]["HttpCheckHistoryResponse"];
type Incident = components["schemas"]["IncidentHistoryResponse"];
type TestResult = components["schemas"]["HttpMonitorTestResponse"];

type Props = {
  project: Project;
  onBack: () => void;
  onOpenPush: () => void;
  onSignedOut: () => void;
  routeMonitorKey?: string;
  onOpenMonitor?: (key: string) => void;
  onBackToList?: () => void;
};

export function MonitorsView({ project, onBack, onOpenPush, onSignedOut,
  routeMonitorKey, onOpenMonitor, onBackToList }: Props) {
  const [monitors, setMonitors] = useState<Monitor[]>([]);
  const [selectedKey, setSelectedKey] = useState<string | undefined>(() => {
    if (onOpenMonitor) return undefined;
    const link = monitorLink();
    return link?.projectKey === project.key && link.monitorType === "http" ? link.monitorKey : undefined;
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      const result = await api.GET("/api/projects/{projectKey}/http-monitors", {
        params: { path: { projectKey: project.key } },
      });
      if (result.data) setMonitors(result.data);
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch {
      setError("The monitor list could not be reached.");
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
      const result = await api.POST("/api/projects/{projectKey}/http-monitors", {
        params: { path: { projectKey: project.key } },
        body,
        headers: csrfHeaders,
      });
      if (result.data) {
        await load();
        if (onOpenMonitor) onOpenMonitor(result.data.key);
        else setSelectedKey(result.data.key);
      } else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch {
      setError("The monitor could not be created.");
    } finally {
      setCreating(false);
    }
  }

  const activeKey = routeMonitorKey ?? selectedKey;
  if (activeKey) {
    return (
      <MonitorDetail
        monitorKey={activeKey}
        onBack={() => {
          if (onBackToList) onBackToList();
          else { setSelectedKey(undefined); void load(); }
        }}
        onRemoved={() => {
          if (onBackToList) onBackToList();
          else { setSelectedKey(undefined); void load(); }
        }}
        onSignedOut={onSignedOut}
        project={project}
      />
    );
  }

  return (
    <div className="workspace">
      <header className="workspace-header">
        <div>
          <p className="eyebrow">Project · {project.key}</p>
          <h1>{project.name}</h1>
          <p className="muted">HTTP monitors</p>
        </div>
        <div className="actions">
          <Button onClick={onOpenPush} type="button">Push monitors</Button>
          <Button onClick={onBack} type="button">Back to projects</Button>
        </div>
      </header>

      <section aria-labelledby="monitor-create-title" className="panel">
        <h2 id="monitor-create-title">Create an HTTP monitor</h2>
        <MonitorForm busy={creating} mode="create" onSubmit={(body) => create(body as CreateMonitor)} />
      </section>

      <section aria-labelledby="monitor-list-title" className="panel">
        <div className="section-heading">
          <div>
            <h2 id="monitor-list-title">Monitors</h2>
            <p className="muted">State is written explicitly; color is only supporting emphasis.</p>
          </div>
          <Button disabled={loading} onClick={() => void load()} type="button">Refresh</Button>
        </div>
        {error && <div className="error" role="alert">{error}</div>}
        {loading && <p role="status">Loading monitors…</p>}
        {!loading && monitors.length === 0 && <div className="empty" role="status">No HTTP monitors yet.</div>}
        {!loading && monitors.length > 0 && (
          <div className="monitor-list">
            {monitors.map((monitor) => (
              <article className="monitor-card" key={monitor.id}>
                <div>
                  <p className={`state state-${monitor.state}`}>{monitorStateLabel(monitor)}</p>
                  <h3>{monitor.name}</h3>
                  <code>{monitor.key}</code>
                  <p className="muted monitor-target">{monitor.target_url}{monitor.has_target_query ? " · secret query configured" : ""}</p>
                </div>
                <div className="monitor-card-actions">
                  <span className="version">version {monitor.version}</span>
                  <Button onClick={() => onOpenMonitor ? onOpenMonitor(monitor.key) : setSelectedKey(monitor.key)} type="button">Open details</Button>
                </div>
              </article>
            ))}
          </div>
        )}
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

type SecretHeader = { id: number; name: string; value: string };

function MonitorForm({ busy, mode, monitor, onSubmit }: FormProps) {
  const [key, setKey] = useState("");
  const [name, setName] = useState(monitor?.name ?? "");
  const [targetUrl, setTargetUrl] = useState(mode === "create" ? "" : monitor?.target_url ?? "");
  const [replaceTarget, setReplaceTarget] = useState(mode === "create");
  const [expectedStatus, setExpectedStatus] = useState(monitor?.expected_status_code ?? 200);
  const [textCondition, setTextCondition] = useState(monitor?.text_condition ?? "none");
  const [textFragment, setTextFragment] = useState(monitor?.text_fragment ?? "");
  const [interval, setInterval] = useState(monitor?.interval_seconds ?? 60);
  const [timeout, setTimeoutValue] = useState(monitor?.timeout_seconds ?? 10);
  const [threshold, setThreshold] = useState(monitor?.failure_threshold ?? 3);
  const [instruction, setInstruction] = useState(monitor?.instruction ?? "");
  const [runbook, setRunbook] = useState(monitor?.runbook_url ?? "");
  const [headers, setHeaders] = useState<SecretHeader[]>([]);
  const nextHeaderID = useRef(0);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const common = {
      name,
      target_url: replaceTarget ? targetUrl : null,
      expected_status_code: expectedStatus,
      text_condition: textCondition,
      text_fragment: textCondition === "none" ? null : textFragment,
      interval_seconds: interval,
      timeout_seconds: timeout,
      failure_threshold: threshold,
      instruction: instruction || null,
      runbook_url: runbook || null,
    };
    if (mode === "create") {
      const body: CreateMonitor = {
        ...common,
        key,
        headers: headers.length > 0 ? headers.map((header) => ({ name: header.name, value: header.value })) : null,
      };
      setTargetUrl("");
      setHeaders((current) => current.map((header) => ({ ...header, value: "" })));
      await onSubmit(body);
    } else {
      if (replaceTarget) setTargetUrl("");
      await onSubmit({ ...common, version: monitor!.version });
    }
  }

  return (
    <form className="monitor-form" onSubmit={submit}>
      {mode === "create" && (
        <label>
          <span>Immutable key</span>
          <input name="monitor-key" pattern="[a-z][a-z0-9-]{1,39}" placeholder="homepage" required value={key} onChange={(event) => setKey(event.target.value)} />
        </label>
      )}
      <label>
        <span>Display name</span>
        <input maxLength={100} name="monitor-name" required value={name} onChange={(event) => setName(event.target.value)} />
      </label>
      {mode === "edit" && (
        <label className="checkbox-label">
          <input checked={replaceTarget} onChange={(event) => setReplaceTarget(event.target.checked)} type="checkbox" />
          <span>Replace the complete target URL{monitor?.has_target_query ? " (otherwise preserve its secret query)" : ""}</span>
        </label>
      )}
      <label className="wide-field">
        <span>Target URL</span>
        <input disabled={!replaceTarget} name="target-url" required={replaceTarget} type="url" value={targetUrl} onChange={(event) => setTargetUrl(event.target.value)} />
      </label>
      <label>
        <span>Expected status</span>
        <input max={599} min={100} required type="number" value={expectedStatus} onChange={(event) => setExpectedStatus(event.target.valueAsNumber)} />
      </label>
      <label>
        <span>Text condition</span>
        <select value={textCondition} onChange={(event) => setTextCondition(event.target.value)}>
          <option value="none">No text check</option>
          <option value="required">Fragment required</option>
          <option value="forbidden">Fragment forbidden</option>
        </select>
      </label>
      <label>
        <span>Text fragment</span>
        <input disabled={textCondition === "none"} maxLength={4096} required={textCondition !== "none"} value={textFragment} onChange={(event) => setTextFragment(event.target.value)} />
      </label>
      <label>
        <span>Interval (seconds)</span>
        <input max={2592000} min={30} required type="number" value={interval} onChange={(event) => setInterval(event.target.valueAsNumber)} />
      </label>
      <label>
        <span>Timeout (seconds)</span>
        <input max={60} min={1} required type="number" value={timeout} onChange={(event) => setTimeoutValue(event.target.valueAsNumber)} />
      </label>
      <label>
        <span>Failures before incident</span>
        <input max={100} min={1} required type="number" value={threshold} onChange={(event) => setThreshold(event.target.valueAsNumber)} />
      </label>
      <label className="wide-field">
        <span>Operator instruction</span>
        <textarea maxLength={1000} value={instruction} onChange={(event) => setInstruction(event.target.value)} />
      </label>
      <label className="wide-field">
        <span>Runbook URL</span>
        <input maxLength={2048} type="url" value={runbook} onChange={(event) => setRunbook(event.target.value)} />
      </label>

      {mode === "create" && (
        <fieldset className="secret-fields wide-field">
          <legend>Secret request headers</legend>
          <p className="muted">Values are submitted once and never returned.</p>
          {headers.map((header, index) => (
            <div className="header-row" key={header.id}>
              <label>
                <span>Header {index + 1} name</span>
                <input required value={header.name} onChange={(event) => setHeaders((current) => current.map((item) => item.id === header.id ? { ...item, name: event.target.value } : item))} />
              </label>
              <label>
                <span>Header {index + 1} value</span>
                <input autoComplete="off" required type="password" value={header.value} onChange={(event) => setHeaders((current) => current.map((item) => item.id === header.id ? { ...item, value: event.target.value } : item))} />
              </label>
              <Button onClick={() => setHeaders((current) => current.filter((item) => item.id !== header.id))} type="button">Remove header</Button>
            </div>
          ))}
          <Button onClick={() => setHeaders((current) => [...current, { id: ++nextHeaderID.current, name: "", value: "" }])} type="button">Add secret header</Button>
        </fieldset>
      )}
      <Button disabled={busy} type="submit">{busy ? "Saving…" : mode === "create" ? "Create monitor" : "Save configuration"}</Button>
    </form>
  );
}

type DetailProps = {
  project: Project;
  monitorKey: string;
  onBack: () => void;
  onRemoved: () => void;
  onSignedOut: () => void;
};

function MonitorDetail({ project, monitorKey, onBack, onRemoved, onSignedOut }: DetailProps) {
  const [monitor, setMonitor] = useState<Monitor>();
  const [checks, setChecks] = useState<Check[]>([]);
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [nextCheckCursor, setNextCheckCursor] = useState<number | null>(null);
  const [nextIncidentCursor, setNextIncidentCursor] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [testResult, setTestResult] = useState<TestResult>();
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [emailIncidentId, setEmailIncidentId] = useState<string | undefined>(() => {
    const link = monitorLink();
    return link?.projectKey === project.key && link.monitorType === "http" && link.monitorKey === monitorKey ? link.incidentId : undefined;
  });

  const paths = useMemo(() => ({ projectKey: project.key, monitorKey }), [monitorKey, project.key]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      const [detail, checkPage, incidentPage] = await Promise.all([
        api.GET("/api/projects/{projectKey}/http-monitors/{monitorKey}", { params: { path: paths } }),
        api.GET("/api/projects/{projectKey}/http-monitors/{monitorKey}/checks", { params: { path: paths, query: { limit: 20 } } }),
        api.GET("/api/projects/{projectKey}/http-monitors/{monitorKey}/incidents", { params: { path: paths, query: { limit: 20 } } }),
      ]);
      const unauthorized = [detail, checkPage, incidentPage].some((result) => result.response.status === 401);
      if (unauthorized) {
        onSignedOut();
        return;
      }
      if (!detail.data) setError(problemMessage(detail.error, detail.response.status));
      else if (!checkPage.data) setError(problemMessage(checkPage.error, checkPage.response.status));
      else if (!incidentPage.data) setError(problemMessage(incidentPage.error, incidentPage.response.status));
      else {
        setMonitor(detail.data);
        setChecks(checkPage.data.items);
        setIncidents(incidentPage.data.items);
        setNextCheckCursor(checkPage.data.next_before_sequence);
        setNextIncidentCursor(incidentPage.data.next_before_opening_sequence);
      }
    } catch {
      setError("The monitor detail could not be reached.");
    } finally {
      setLoading(false);
    }
  }, [onSignedOut, paths]);

  useEffect(() => {
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load]);

  async function mutationFailure(errorValue: unknown, status: number) {
    if (status === 401) {
      onSignedOut();
      return;
    }
    const message = problemMessage(errorValue, status);
    if (status === 409) await load();
    setError(message);
  }

  async function lifecycle(operation: "pause" | "resume" | "test" | "remove") {
    if (!monitor) return;
    setBusy(operation);
    setError(undefined);
    setNotice(undefined);
    try {
      if (operation === "test") {
        const result = await api.POST("/api/projects/{projectKey}/http-monitors/{monitorKey}/test", {
          params: { path: paths },
          headers: csrfHeaders,
        });
        if (result.data) {
          setMonitor(result.data.monitor);
          setTestResult(result.data);
          setNotice(result.data.succeeded ? "Immediate check succeeded." : "Immediate check completed with a failure.");
          await load();
        } else await mutationFailure(result.error, result.response.status);
      } else if (operation === "remove") {
        const result = await api.DELETE("/api/projects/{projectKey}/http-monitors/{monitorKey}", {
          params: { path: paths, query: { version: String(monitor.version) } },
          headers: csrfHeaders,
        });
        if (result.data) onRemoved();
        else await mutationFailure(result.error, result.response.status);
      } else {
        const result = operation === "pause"
          ? await api.POST("/api/projects/{projectKey}/http-monitors/{monitorKey}/pause", {
              params: { path: paths }, body: { version: monitor.version }, headers: csrfHeaders,
            })
          : await api.POST("/api/projects/{projectKey}/http-monitors/{monitorKey}/resume", {
              params: { path: paths }, body: { version: monitor.version }, headers: csrfHeaders,
            });
        if (result.data) {
          setMonitor(result.data);
          setNotice(operation === "pause" ? "Monitor paused." : "Monitor resumed; a fresh check is due.");
        } else await mutationFailure(result.error, result.response.status);
      }
    } catch {
      setError("The monitor change could not be completed.");
    } finally {
      setBusy(undefined);
    }
  }

  async function update(body: CreateMonitor | UpdateMonitor) {
    setBusy("update");
    setError(undefined);
    try {
      const result = await api.PUT("/api/projects/{projectKey}/http-monitors/{monitorKey}", {
        params: { path: paths }, body: body as UpdateMonitor, headers: csrfHeaders,
      });
      if (result.data) {
        setMonitor(result.data);
        setNotice("Configuration saved.");
      } else await mutationFailure(result.error, result.response.status);
    } catch {
      setError("The monitor configuration could not be saved.");
    } finally {
      setBusy(undefined);
    }
  }

  async function setHeader(name: string, value: string) {
    if (!monitor) return;
    setBusy("header");
    setError(undefined);
    try {
      const result = await api.PUT("/api/projects/{projectKey}/http-monitors/{monitorKey}/headers/{name}", {
        params: { path: { ...paths, name } },
        body: { value, version: monitor.version },
        headers: csrfHeaders,
      });
      if (result.data) {
        setMonitor(result.data);
        setNotice(`Header ${name} was stored. Its value will not be shown.`);
      } else await mutationFailure(result.error, result.response.status);
    } catch {
      setError("The secret header could not be stored.");
    } finally {
      setBusy(undefined);
    }
  }

  async function removeHeader(name: string) {
    if (!monitor) return;
    setBusy(`header:${name}`);
    setError(undefined);
    try {
      const result = await api.DELETE("/api/projects/{projectKey}/http-monitors/{monitorKey}/headers/{name}", {
        params: { path: { ...paths, name }, query: { version: String(monitor.version) } },
        headers: csrfHeaders,
      });
      if (result.data) {
        setMonitor(result.data);
        setNotice(`Header ${name} was removed.`);
      } else await mutationFailure(result.error, result.response.status);
    } catch {
      setError("The secret header could not be removed.");
    } finally {
      setBusy(undefined);
    }
  }

  async function moreChecks() {
    if (nextCheckCursor === null) return;
    setBusy("checks");
    setError(undefined);
    try {
      const result = await api.GET("/api/projects/{projectKey}/http-monitors/{monitorKey}/checks", {
        params: { path: paths, query: { before_sequence: nextCheckCursor, limit: 20 } },
      });
      if (result.data) {
        setChecks((current) => [...current, ...result.data!.items]);
        setNextCheckCursor(result.data.next_before_sequence);
      } else await mutationFailure(result.error, result.response.status);
    } catch {
      setError("Older checks could not be reached.");
    } finally {
      setBusy(undefined);
    }
  }

  async function moreIncidents() {
    if (nextIncidentCursor === null) return;
    setBusy("incidents");
    setError(undefined);
    try {
      const result = await api.GET("/api/projects/{projectKey}/http-monitors/{monitorKey}/incidents", {
        params: { path: paths, query: { before_opening_sequence: nextIncidentCursor, limit: 20 } },
      });
      if (result.data) {
        setIncidents((current) => [...current, ...result.data!.items]);
        setNextIncidentCursor(result.data.next_before_opening_sequence);
      } else await mutationFailure(result.error, result.response.status);
    } catch {
      setError("Older incidents could not be reached.");
    } finally {
      setBusy(undefined);
    }
  }

  if (loading && !monitor) return <div className="workspace"><p role="status">Loading monitor detail…</p></div>;
  if (!monitor) {
    return (
      <div className="workspace panel">
        <h1>Monitor unavailable</h1>
        <div className="error" role="alert">{error ?? "The monitor is unavailable."}</div>
        <div className="actions">
          <Button onClick={() => void load()} type="button">Try again</Button>
          <Button onClick={onBack} type="button">Back to monitors</Button>
        </div>
      </div>
    );
  }

  const latest = checks.find((check) => check.id === monitor.latest_result_id);
  const lastSuccess = checks.find((check) => check.id === monitor.latest_success_id);

  return (
    <div className="workspace">
      <header className="workspace-header">
        <div>
          <p className="eyebrow">{project.key} · {monitor.key}</p>
          <h1>{monitor.name}</h1>
          <p className={`state state-${monitor.state}`}>{monitorStateLabel(monitor)}</p>
        </div>
        <Button onClick={onBack} type="button">Back to monitors</Button>
      </header>

      {error && <div className="error" role="alert">{error}</div>}
      {notice && <div className="notice" role="status">{notice}</div>}
      <MaintenancePanel projectKey={project.key} monitorType="http" monitorKey={monitorKey} onSignedOut={onSignedOut} />
      {testResult && (
        <div className="notice test-result" role="status">
          Immediate check: {testResult.succeeded ? "success" : "failure"}; status {testResult.status_code ?? "none"}; {testResult.response_time_milliseconds} ms; reason {testResult.reason_code ?? "none"}; {testResult.applied_to_current_state ? "applied" : "history only"}.
        </div>
      )}

      <section aria-labelledby="facts-title" className="panel">
        <div className="section-heading">
          <h2 id="facts-title">Current facts</h2>
          <div className="actions">
            <Button disabled={busy !== undefined} onClick={() => void lifecycle("test")} type="button">Run test now</Button>
            {monitor.state === "paused"
              ? <Button disabled={busy !== undefined} onClick={() => void lifecycle("resume")} type="button">Resume</Button>
              : <Button disabled={busy !== undefined} onClick={() => void lifecycle("pause")} type="button">Pause</Button>}
          </div>
        </div>
        <dl className="fact-grid">
          <Fact label="Target" value={`${monitor.target_url}${monitor.has_target_query ? " (secret query configured)" : ""}`} />
          <Fact label="Latest result" value={latest ? `${latest.outcome} · ${formatDate(latest.completed_at)}` : monitor.latest_result_id ?? "No result yet"} />
          <Fact label="Last success" value={lastSuccess ? formatDate(lastSuccess.completed_at) : monitor.latest_success_id ?? "No success yet"} />
          <Fact label="Next run" value={monitor.next_check_at ? formatDate(monitor.next_check_at) : "Not scheduled"} />
          <Fact label="Response time" value={latest?.response_time_milliseconds === null || latest?.response_time_milliseconds === undefined ? "Not available" : `${latest.response_time_milliseconds} ms`} />
          <Fact label="Failure reason" value={latest?.failure_reason ?? "None"} />
          <Fact label="Expected status" value={String(monitor.expected_status_code)} />
          <Fact label="Version" value={String(monitor.version)} />
        </dl>
        {monitor.instruction && <div className="operator-note"><strong>Operator instruction</strong><p>{monitor.instruction}</p></div>}
        {monitor.runbook_url && <p><a href={monitor.runbook_url} rel="noreferrer" target="_blank">Open runbook</a></p>}
      </section>

      <section aria-labelledby="configuration-title" className="panel">
        <h2 id="configuration-title">Configuration</h2>
        <MonitorForm busy={busy === "update"} mode="edit" monitor={monitor} onSubmit={update} />
      </section>

      <section aria-labelledby="headers-title" className="panel">
        <h2 id="headers-title">Secret request headers</h2>
        <p className="muted">Only names and timestamps are visible. Setting a name replaces its hidden value.</p>
        {monitor.headers.length === 0 ? <div className="empty">No request headers configured.</div> : (
          <ul className="metadata-list">
            {monitor.headers.map((header) => (
              <li key={header.id}><span><code>{header.name}</code> · updated {formatDate(header.updated_at)}</span><Button disabled={busy !== undefined} onClick={() => void removeHeader(header.name)} type="button">Remove</Button></li>
            ))}
          </ul>
        )}
        <HeaderForm busy={busy === "header"} onSubmit={setHeader} />
      </section>

      <section aria-labelledby="checks-title" className="panel">
        <h2 id="checks-title">Check history</h2>
        {checks.length === 0 ? <div className="empty">No completed checks yet.</div> : (
          <ol className="history-list">
            {checks.map((check) => (
              <li key={check.id}>
                <strong>{check.outcome}</strong> · {formatDate(check.completed_at)} · {check.response_time_milliseconds ?? "—"} ms
                <span>{check.trigger} · status {check.status_code ?? "—"} · reason {check.failure_reason ?? "none"}</span>
              </li>
            ))}
          </ol>
        )}
        {nextCheckCursor !== null && <Button disabled={busy !== undefined} onClick={() => void moreChecks()} type="button">Load older checks</Button>}
      </section>

      <section aria-labelledby="incidents-title" className="panel">
        <h2 id="incidents-title">Incident history</h2>
        {incidents.length === 0 ? <div className="empty">No incidents.</div> : (
          <ol className="history-list">
            {incidents.map((incident) => (
              <li key={incident.id}>
                <strong>{incident.resolved_at ? "Resolved incident" : "Open incident"}</strong> · opened {formatDate(incident.opened_at)}
                <span>Original reason {incident.original_reason}; latest reason {incident.latest_reason}{incident.resolved_at ? `; resolved ${formatDate(incident.resolved_at)}` : ""}</span>
                <Button onClick={() => setEmailIncidentId(incident.id)} type="button">Email status for incident</Button>
              </li>
            ))}
          </ol>
        )}
        {nextIncidentCursor !== null && <Button disabled={busy !== undefined} onClick={() => void moreIncidents()} type="button">Load older incidents</Button>}
      </section>

      {emailIncidentId && <IncidentEmailPanel key={emailIncidentId} incidentId={emailIncidentId} monitorType="http" onSignedOut={onSignedOut} />}
      <DeliveryHistoryPanel projectKey={project.key} monitorType="http" monitorKey={monitorKey} onSignedOut={onSignedOut} />

      <section aria-labelledby="remove-title" className="panel danger-zone">
        <h2 id="remove-title">Remove monitor</h2>
        <p className="muted">Removal stops scheduling and hides the monitor while retaining its history and key.</p>
        {!confirmRemove ? (
          <Button className="danger" onClick={() => setConfirmRemove(true)} type="button">Remove monitor…</Button>
        ) : (
          <div className="actions confirmation" role="group" aria-label="Confirm monitor removal">
            <span>Remove {monitor.name}?</span>
            <Button className="danger" disabled={busy !== undefined} onClick={() => void lifecycle("remove")} type="button">Confirm removal</Button>
            <Button disabled={busy !== undefined} onClick={() => setConfirmRemove(false)} type="button">Cancel</Button>
          </div>
        )}
      </section>
    </div>
  );
}

function HeaderForm({ busy, onSubmit }: { busy: boolean; onSubmit: (name: string, value: string) => Promise<void> }) {
  const [name, setName] = useState("");
  const [value, setValue] = useState("");

  async function submit(event: FormEvent) {
    event.preventDefault();
    const submittedValue = value;
    setValue("");
    await onSubmit(name, submittedValue);
    setName("");
  }

  return (
    <form className="header-row header-form" onSubmit={submit}>
      <label><span>Header name</span><input required value={name} onChange={(event) => setName(event.target.value)} /></label>
      <label><span>New secret value</span><input autoComplete="off" required type="password" value={value} onChange={(event) => setValue(event.target.value)} /></label>
      <Button disabled={busy} type="submit">Set or replace header</Button>
    </form>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return <div><dt>{label}</dt><dd>{value}</dd></div>;
}

function monitorStateLabel(monitor: Monitor): string {
  if (monitor.state === "paused") return monitor.open_incident_id ? "Paused · incident remains open" : "Paused";
  if (monitor.open_incident_id) return `${monitor.state === "untested" ? "Untested" : "Incident open"} · ${monitor.consecutive_failures}/${monitor.failure_threshold} failures`;
  if (monitor.state === "failing") return `Failing below threshold · ${monitor.consecutive_failures}/${monitor.failure_threshold} failures`;
  if (monitor.state === "healthy") return "Healthy";
  return "Untested";
}

function formatDate(value: string): string {
  return new Date(value).toLocaleString();
}
