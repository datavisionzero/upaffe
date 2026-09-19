import { useCallback, useEffect, useState, type FormEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";

type Maintenance = components["schemas"]["MaintenanceSnapshot"];
type Delivery = components["schemas"]["EmailDeliveryStatus"];
type Summary = components["schemas"]["EmailDeliverySummary"];
type IncidentStatus = components["schemas"]["IncidentEmailStatus"];
type Scope = { projectKey: string; monitorType?: "http" | "push"; monitorKey?: string };

function date(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString() : "—";
}

export function MaintenancePanel({ projectKey, monitorType, monitorKey, onSignedOut }: Scope & { onSignedOut: () => void }) {
  const [snapshot, setSnapshot] = useState<Maintenance>();
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [duration, setDuration] = useState(60);
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [now, setNow] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = monitorType === "http" && monitorKey
        ? await api.GET("/api/projects/{projectKey}/http-monitors/{monitorKey}/maintenance", { params: { path: { projectKey, monitorKey } } })
        : monitorType === "push" && monitorKey
          ? await api.GET("/api/projects/{projectKey}/push-monitors/{monitorKey}/maintenance", { params: { path: { projectKey, monitorKey } } })
          : await api.GET("/api/projects/{projectKey}/maintenance", { params: { path: { projectKey } } });
      if (result.data) { setSnapshot(result.data); setError(undefined); }
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch { setError("Maintenance could not be reached."); }
    finally { setLoading(false); }
  }, [monitorKey, monitorType, onSignedOut, projectKey]);

  useEffect(() => {
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load]);
  useEffect(() => {
    const start = window.setTimeout(() => setNow(Date.now()), 0);
    const interval = window.setInterval(() => setNow(Date.now()), 30000);
    return () => { window.clearTimeout(start); window.clearInterval(interval); };
  }, []);

  async function change(action: "start" | "end") {
    if (!snapshot) return;
    setBusy(true); setError(undefined); setNotice(undefined);
    try {
      const projectPath = { projectKey };
      const monitorPath = { projectKey, monitorKey: monitorKey ?? "" };
      const body = { version: snapshot.version, duration_seconds: duration * 60 };
      const query = { version: String(snapshot.version) };
      const result = action === "start"
        ? monitorType === "http"
          ? await api.POST("/api/projects/{projectKey}/http-monitors/{monitorKey}/maintenance", { params: { path: monitorPath }, body, headers: csrfHeaders })
          : monitorType === "push"
            ? await api.POST("/api/projects/{projectKey}/push-monitors/{monitorKey}/maintenance", { params: { path: monitorPath }, body, headers: csrfHeaders })
            : await api.POST("/api/projects/{projectKey}/maintenance", { params: { path: projectPath }, body, headers: csrfHeaders })
        : monitorType === "http"
          ? await api.DELETE("/api/projects/{projectKey}/http-monitors/{monitorKey}/maintenance", { params: { path: monitorPath, query }, headers: csrfHeaders })
          : monitorType === "push"
            ? await api.DELETE("/api/projects/{projectKey}/push-monitors/{monitorKey}/maintenance", { params: { path: monitorPath, query }, headers: csrfHeaders })
            : await api.DELETE("/api/projects/{projectKey}/maintenance", { params: { path: projectPath, query }, headers: csrfHeaders });
      if (result.data) {
        setSnapshot(result.data);
        setNotice(action === "start" ? "Maintenance window saved. Monitoring continues." : "Direct maintenance ended. Other active scopes still apply.");
      } else if (result.response.status === 401) onSignedOut();
      else {
        const message = problemMessage(result.error, result.response.status);
        if (result.response.status === 409) await load();
        setError(message);
      }
    } catch { setError("The maintenance change could not be completed."); }
    finally { setBusy(false); }
  }

  const effective = snapshot?.effective_active && (now === null || !snapshot.effective_ends_at || Date.parse(snapshot.effective_ends_at) > now);
  const direct = snapshot?.direct_active && (now === null || !snapshot.ends_at || Date.parse(snapshot.ends_at) > now);
  const title = monitorType ? `${monitorType.toUpperCase()} monitor maintenance` : "Project maintenance";
  return <section className="panel" aria-label={title}>
    <div className="section-heading"><div><h2>{title}</h2><p className="muted">Email is held while maintenance applies. Checks, reports, and incident history continue.</p></div><Button disabled={loading} onClick={() => void load()} type="button">Refresh maintenance</Button></div>
    {loading && !snapshot && <p role="status">Loading maintenance…</p>}
    {error && <div className="error" role="alert">{error}</div>}
    {notice && <p className="notice" role="status">{notice}</p>}
    {snapshot && <>
      <p><strong>Effective: {effective ? "Active" : "Inactive"}</strong>{effective ? ` · until ${date(snapshot.effective_ends_at)}` : ""}</p>
      <p>Direct scope: {direct ? `active until ${date(snapshot.ends_at)}` : "inactive"} · version {snapshot.version}</p>
      {monitorType && <p>Active scopes: {effective ? snapshot.active_scopes.join(", ") : "none"}</p>}
      <form className="actions" onSubmit={(event: FormEvent) => { event.preventDefault(); void change("start"); }}>
        <label><span>Duration in minutes</span><input aria-label={`${title} duration in minutes`} min={1} max={43200} required type="number" value={duration} onChange={(event) => setDuration(event.target.valueAsNumber)} /></label>
        <Button disabled={busy} type="submit">{direct ? "Extend maintenance" : "Start maintenance"}</Button>
        {direct && <Button disabled={busy} onClick={() => void change("end")} type="button">End direct maintenance</Button>}
      </form>
    </>}
  </section>;
}

export function DeliveryHistoryPanel({ projectKey, monitorType, monitorKey, onSignedOut }: Scope & { onSignedOut: () => void }) {
  const [items, setItems] = useState<Delivery[]>([]);
  const [summary, setSummary] = useState<Summary>();
  const [offset, setOffset] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  const load = useCallback(async (nextOffset = 0) => {
    setLoading(true);
    try {
      const [page, counts] = await Promise.all([
        api.GET("/api/email/deliveries", { params: { query: { project_key: projectKey, monitor_type: monitorType, monitor_key: monitorKey, limit: 20, offset: nextOffset } } }),
        api.GET("/api/projects/{key}/email-summary", { params: { path: { key: projectKey } } }),
      ]);
      if (page.response.status === 401 || counts.response.status === 401) { onSignedOut(); return; }
      if (!page.data) { setError(problemMessage(page.error, page.response.status)); return; }
      if (!counts.data) { setError(problemMessage(counts.error, counts.response.status)); return; }
      setItems((current) => nextOffset === 0 ? page.data!.items : [...current, ...page.data!.items]);
      setOffset(nextOffset + page.data.items.length);
      setHasMore(page.data.has_more);
      setSummary(counts.data);
      setError(undefined);
    } catch { setError("Email deliveries could not be reached."); }
    finally { setLoading(false); }
  }, [monitorKey, monitorType, onSignedOut, projectKey]);

  useEffect(() => {
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load]);

  return <section className="panel" aria-label="Email delivery">
    <div className="section-heading"><div><h2>Email delivery</h2><p className="muted">SMTP acceptance confirms relay submission, not inbox delivery. History is retained for 90 days.</p></div><Button disabled={loading} onClick={() => void load()} type="button">Refresh delivery</Button></div>
    {summary && <p>Pending {summary.pending_count} · retrying {summary.retrying_count} · terminal failures {summary.terminal_failure_count} · SMTP accepted {summary.smtp_accepted_count}</p>}
    {error && <div className="error" role="alert">{error}</div>}
    {loading && items.length === 0 && <p role="status">Loading email deliveries…</p>}
    {!loading && items.length === 0 && <div className="empty">No email deliveries recorded.</div>}
    {items.length > 0 && <ol className="history-list">{items.map((item) => <li key={item.id}>
      <strong>{item.kind} · {item.state}</strong> · {item.recipient}
      <span>{item.monitor_type}/{item.monitor_key} · incident {item.incident_id} · attempts {item.attempt_count} · created {date(item.created_at)}</span>
      {(item.last_error_code || item.suppression_reason) && <span>{item.last_error_code ? `Failure code ${item.last_error_code}` : ""}{item.suppression_reason ? ` · Suppressed by ${item.suppression_reason}` : ""}</span>}
      {item.next_attempt_at && <span>Next attempt {date(item.next_attempt_at)}</span>}
    </li>)}</ol>}
    {hasMore && <Button disabled={loading} onClick={() => void load(offset)} type="button">Load older deliveries</Button>}
  </section>;
}

export function IncidentEmailPanel({ incidentId, monitorType, onSignedOut }: { incidentId: string; monitorType: "http" | "push"; onSignedOut: () => void }) {
  const [status, setStatus] = useState<IncidentStatus>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);
  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await api.GET("/api/email/incidents/{incidentId}", { params: { path: { incidentId }, query: { monitor_type: monitorType } } });
      if (result.data) { setStatus(result.data); setError(undefined); }
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch { setError("Incident email status could not be reached."); }
    finally { setLoading(false); }
  }, [incidentId, monitorType, onSignedOut]);
  useEffect(() => { const start = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(start); }, [load]);
  return <section className="panel" aria-label="Incident email status">
    <div className="section-heading"><h2>Incident email status</h2><Button disabled={loading} onClick={() => void load()} type="button">Refresh email status</Button></div>
    {loading && !status && <p role="status">Loading incident email status…</p>}
    {error && <div className="error" role="alert">{error}</div>}
    {status && <>
      <p><strong>Announcement: {status.announcement_state}</strong>{status.suppression_reason ? ` · ${status.suppression_reason}` : ""}</p>
      {status.deliveries.length === 0 ? <div className="empty">No recipient delivery was queued.</div> : <ol className="history-list">{status.deliveries.map((item) => <li key={item.id}>
        <strong>{item.kind} · {item.state}</strong> · {item.recipient}
        <span>Attempts {item.attempt_count} · last {date(item.last_attempt_at)} · next {date(item.next_attempt_at)} · SMTP accepted {date(item.accepted_at)}</span>
        {item.last_error_code && <span>Failure code {item.last_error_code}</span>}
        {item.suppression_reason && <span>Suppressed by {item.suppression_reason}</span>}
      </li>)}</ol>}
    </>}
  </section>;
}
