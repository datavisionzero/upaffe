import { useCallback, useEffect, useState, type FormEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { DeliveryHistoryPanel, MaintenancePanel } from "@/shell/EmailPanels";

type Settings = components["schemas"]["EmailConfigurationSnapshot"];
type Recipients = components["schemas"]["ProjectRecipientsResponse"];
type Project = components["schemas"]["ProjectResponse"];

function lines(value: string): string[] {
  return value.split(/[\n,]/).map((item) => item.trim()).filter(Boolean);
}

export function InstanceEmailView({ onBack, onSignedOut }: { onBack: () => void; onSignedOut: () => void }) {
  const [settings, setSettings] = useState<Settings>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await api.GET("/api/email/settings");
      if (result.data) { setSettings(result.data); setError(undefined); }
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch { setError("Email settings could not be reached."); }
    finally { setLoading(false); }
  }, [onSignedOut]);
  useEffect(() => { const start = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(start); }, [load]);
  return <div className="workspace">
    <header className="workspace-header"><div><p className="eyebrow">upaffe</p><h1>Instance email</h1><p className="muted">One shared SMTP relay and defaults for new projects.</p></div><Button onClick={onBack} type="button">Back to projects</Button></header>
    {loading && !settings && <p role="status">Loading email settings…</p>}
    {error && <div className="error" role="alert">{error}</div>}
    {notice && <p className="notice" role="status">{notice}</p>}
    {settings && <SettingsForms key={settings.version} settings={settings} onSaved={(value, message) => { setSettings(value); setError(undefined); setNotice(message); }} onConflict={setError} onRefresh={load} onSignedOut={onSignedOut} />}
    <InstanceDeliverySummary onSignedOut={onSignedOut} />
  </div>;
}

function SettingsForms({ settings, onSaved, onConflict, onRefresh, onSignedOut }: {
  settings: Settings; onSaved: (value: Settings, message: string) => void; onConflict: (message: string) => void; onRefresh: () => Promise<void>; onSignedOut: () => void;
}) {
  const [host, setHost] = useState(settings.host ?? "");
  const [port, setPort] = useState(settings.port ?? 587);
  const [security, setSecurity] = useState(settings.security);
  const [sender, setSender] = useState(settings.sender_address ?? "");
  const [senderName, setSenderName] = useState(settings.sender_name ?? "");
  const [baseUrl, setBaseUrl] = useState(settings.public_base_url ?? "");
  const [username, setUsername] = useState(settings.username ?? "");
  const [defaults, setDefaults] = useState(settings.default_recipients.join("\n"));
  const [password, setPassword] = useState("");
  const [testRecipient, setTestRecipient] = useState("");
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();

  async function handle<T>(operation: string, request: Promise<{ data?: T; error?: unknown; response: Response }>, success: (value: T) => void) {
    setBusy(operation); setError(undefined); setNotice(undefined);
    try {
      const result = await request;
      if (result.data) success(result.data);
      else if (result.response.status === 401) onSignedOut();
      else {
        const message = problemMessage(result.error, result.response.status);
        if (result.response.status === 409) { await onRefresh(); onConflict(message); }
        else setError(message);
      }
    } catch { setError(`${operation} could not be completed.`); }
    finally { setBusy(undefined); }
  }

  function saveSettings(event: FormEvent) {
    event.preventDefault();
    void handle("SMTP settings", api.PUT("/api/email/settings", { body: {
      version: settings.version, host: host || null, port, security,
      sender_address: sender || null, sender_name: senderName || null,
      public_base_url: baseUrl || null, username: username || null,
    }, headers: csrfHeaders }), (value) => onSaved(value, "SMTP settings saved."));
  }

  function saveDefaults(event: FormEvent) {
    event.preventDefault();
    void handle("Default recipients", api.PUT("/api/email/default-recipients", {
      body: { version: settings.version, recipients: lines(defaults) }, headers: csrfHeaders,
    }), (value) => onSaved(value, "Default recipients saved."));
  }

  function savePassword(event: FormEvent) {
    event.preventDefault();
    const submitted = password;
    setPassword("");
    void handle("SMTP password", api.PUT("/api/email/password", {
      body: { version: settings.version, password: submitted }, headers: csrfHeaders,
    }), (value) => onSaved(value, "SMTP password replaced."));
  }

  function clearPassword() {
    void handle("SMTP password", api.DELETE("/api/email/password", {
      params: { query: { version: String(settings.version) } }, headers: csrfHeaders,
    }), (value) => onSaved(value, "SMTP password cleared."));
  }

  async function sendTest(event: FormEvent) {
    event.preventDefault();
    setBusy("Test email"); setError(undefined); setNotice(undefined);
    try {
      const result = await api.POST("/api/email/test", { body: { recipient: testRecipient }, headers: csrfHeaders });
      if (result.data) setNotice("Accepted by SMTP. Inbox delivery is not confirmed.");
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch { setError("The test email could not be submitted."); }
    finally { setBusy(undefined); }
  }

  return <>
    {error && <div className="error" role="alert">{error}</div>}
    {notice && <p className="notice" role="status">{notice}</p>}
    <section className="panel" aria-labelledby="smtp-title"><h2 id="smtp-title">SMTP settings</h2>
      <p className="muted">Version {settings.version}. Password: {settings.has_password ? "configured" : "not configured"}.</p>
      <form className="monitor-form" onSubmit={saveSettings}>
        <label><span>Relay host</span><input required value={host} onChange={(event) => setHost(event.target.value)} /></label>
        <label><span>Relay port</span><input min={1} max={65535} required type="number" value={port} onChange={(event) => setPort(event.target.valueAsNumber)} /></label>
        <label><span>Security</span><select value={security} onChange={(event) => setSecurity(event.target.value)}><option value="starttls">STARTTLS</option><option value="tls">TLS from start</option><option value="none">None</option></select></label>
        <label><span>Sender address</span><input required type="email" value={sender} onChange={(event) => setSender(event.target.value)} /></label>
        <label><span>Sender name</span><input value={senderName} onChange={(event) => setSenderName(event.target.value)} /></label>
        <label><span>Public detail URL</span><input required type="url" value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} /></label>
        <label><span>Authentication username</span><input autoComplete="off" value={username} onChange={(event) => setUsername(event.target.value)} /></label>
        <Button disabled={busy !== undefined} type="submit">Save SMTP settings</Button>
      </form>
    </section>
    <section className="panel" aria-labelledby="password-title"><h2 id="password-title">SMTP password</h2>
      <p className="muted">The saved value is never displayed. Replace it explicitly, then send a test message.</p>
      <form className="actions" onSubmit={savePassword}><label><span>New SMTP password</span><input autoComplete="new-password" required type="password" value={password} onChange={(event) => setPassword(event.target.value)} /></label><Button disabled={busy !== undefined} type="submit">Replace password</Button></form>
      {settings.has_password && <Button disabled={busy !== undefined} onClick={clearPassword} type="button">Clear password</Button>}
    </section>
    <section className="panel" aria-labelledby="defaults-title"><h2 id="defaults-title">Default recipients</h2>
      <p className="muted">Copied to new projects only. Enter one address per line.</p>
      <form onSubmit={saveDefaults}><label><span>Recipients</span><textarea value={defaults} onChange={(event) => setDefaults(event.target.value)} /></label><div className="actions"><Button disabled={busy !== undefined} type="submit">Save defaults</Button></div></form>
    </section>
    <section className="panel" aria-labelledby="test-title"><h2 id="test-title">Test the relay</h2>
      <form className="actions" onSubmit={(event) => void sendTest(event)}><label><span>Test recipient</span><input required type="email" value={testRecipient} onChange={(event) => setTestRecipient(event.target.value)} /></label><Button disabled={busy !== undefined} type="submit">Send test email</Button></form>
    </section>
  </>;
}

function InstanceDeliverySummary({ onSignedOut }: { onSignedOut: () => void }) {
  const [summary, setSummary] = useState<components["schemas"]["EmailDeliverySummary"]>();
  const [error, setError] = useState<string>();
  const load = useCallback(async () => {
    try {
      const result = await api.GET("/api/email/deliveries/summary");
      if (result.data) { setSummary(result.data); setError(undefined); }
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch { setError("Delivery summary could not be reached."); }
  }, [onSignedOut]);
  useEffect(() => { const start = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(start); }, [load]);
  return <section className="panel" aria-label="Instance email delivery"><div className="section-heading"><h2>Instance email delivery</h2><Button onClick={() => void load()} type="button">Refresh summary</Button></div>
    {error && <p className="error" role="alert">{error}</p>}
    {!summary && !error && <p role="status">Loading delivery summary…</p>}
    {summary && <p>Pending {summary.pending_count} · retrying {summary.retrying_count} · terminal failures {summary.terminal_failure_count} · SMTP accepted {summary.smtp_accepted_count}</p>}
  </section>;
}

export function ProjectEmailView({ project, onBack, onSignedOut }: { project: Project; onBack: () => void; onSignedOut: () => void }) {
  const [recipients, setRecipients] = useState<Recipients>();
  const [draft, setDraft] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await api.GET("/api/projects/{key}/recipients", { params: { path: { key: project.key } } });
      if (result.data) { setRecipients(result.data); setDraft(result.data.recipients.join("\n")); setError(undefined); }
      else if (result.response.status === 401) onSignedOut();
      else setError(problemMessage(result.error, result.response.status));
    } catch { setError("Project recipients could not be reached."); }
    finally { setLoading(false); }
  }, [onSignedOut, project.key]);
  useEffect(() => { const start = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(start); }, [load]);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (!recipients) return;
    setBusy(true); setError(undefined); setNotice(undefined);
    try {
      const result = await api.PUT("/api/projects/{key}/recipients", {
        params: { path: { key: project.key } },
        body: { version: recipients.version, recipients: lines(draft) }, headers: csrfHeaders,
      });
      if (result.data) { setRecipients(result.data); setNotice("Project recipients saved."); }
      else if (result.response.status === 401) onSignedOut();
      else { const message = problemMessage(result.error, result.response.status); if (result.response.status === 409) await load(); setError(message); }
    } catch { setError("Project recipients could not be saved."); }
    finally { setBusy(false); }
  }

  return <div className="workspace">
    <header className="workspace-header"><div><p className="eyebrow">Project · {project.key}</p><h1>Email and maintenance</h1><p className="muted">{project.name}</p></div><Button onClick={onBack} type="button">Back to projects</Button></header>
    <section className="panel" aria-labelledby="project-recipients-title"><h2 id="project-recipients-title">Project recipients</h2><p className="muted">Live list for future alerts. An empty list opts this project out.</p>
      {loading && !recipients && <p role="status">Loading recipients…</p>}
      {error && <div className="error" role="alert">{error}</div>}
      {notice && <p className="notice" role="status">{notice}</p>}
      {recipients && <form onSubmit={(event) => void save(event)}><p>Version {recipients.version}</p><label><span>One address per line</span><textarea value={draft} onChange={(event) => setDraft(event.target.value)} /></label><div className="actions"><Button disabled={busy} type="submit">Save project recipients</Button><Button disabled={loading} onClick={() => void load()} type="button">Refresh recipients</Button></div></form>}
    </section>
    <MaintenancePanel projectKey={project.key} onSignedOut={onSignedOut} />
    <DeliveryHistoryPanel projectKey={project.key} onSignedOut={onSignedOut} />
  </div>;
}
