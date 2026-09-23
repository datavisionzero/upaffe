import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemFieldErrors, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { SelectField, TextAreaField, TextField } from "@/components/Fields";
import { Alert, LoadingState, PageHeader } from "@/components/Presentation";
import { DeliveryHistoryPanel, MaintenancePanel } from "@/shell/EmailPanels";
import type { EmailTask } from "@/shell/routes";

type Settings = components["schemas"]["EmailConfigurationSnapshot"];
type Recipients = components["schemas"]["ProjectRecipientsResponse"];
type Project = components["schemas"]["ProjectResponse"];

function lines(value: string): string[] {
  return value.split(/[\n,]/).map((item) => item.trim()).filter(Boolean);
}

const emailTasks: { task: EmailTask; label: string; detail: string }[] = [
  { task: "delivery", label: "Delivery", detail: "Durable delivery history and SMTP acceptance across all projects." },
  { task: "relay", label: "Relay settings", detail: "The shared SMTP relay, sender, and public detail URL." },
  { task: "password", label: "SMTP password", detail: "Replace or clear the write-only relay password." },
  { task: "recipients", label: "Default recipients", detail: "Addresses copied to projects created from now on." },
  { task: "test", label: "Test email", detail: "Send one message to confirm the relay accepts mail." },
];

type TaskLink = (task: EmailTask, label: ReactNode, className?: string) => ReactNode;

export function InstanceEmailView({ task, taskLink, onBack, onSignedOut }: {
  task: EmailTask; taskLink: TaskLink; onBack: () => void; onSignedOut: () => void;
}) {
  const [settings, setSettings] = useState<Settings>();
  const [loading, setLoading] = useState(task !== "delivery");
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<ReactNode>();
  const current = emailTasks.find((item) => item.task === task)!;
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
  useEffect(() => {
    if (task === "delivery") return;
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load, task]);
  return <div className="workspace settings-workspace">
    <PageHeader title="Instance email" detail="One shared SMTP relay and defaults for new projects." actions={<Button onClick={onBack} type="button">Back to investigation</Button>} />
    <nav aria-label="Email tasks" className="task-nav">
      {emailTasks.map((item) => <span key={item.task}>{taskLink(item.task, item.label, "task-link")}</span>)}
    </nav>
    <p className="task-detail">{current.detail}</p>
    {task === "delivery" ? <DeliveryHistoryPanel onSignedOut={onSignedOut} /> : <>
      {loading && !settings && <LoadingState>Loading email settings…</LoadingState>}
      {error && <Alert tone="danger">{error} <Button onClick={() => void load()} type="button">Try again</Button></Alert>}
      {notice && <Alert>{notice}</Alert>}
      {settings && <SettingsTask key={settings.version} task={task} taskLink={taskLink} settings={settings}
        onSaved={(value, message) => { setSettings(value); setError(undefined); setNotice(message); }}
        onConflict={setError} onRefresh={load} onSignedOut={onSignedOut} />}
    </>}
  </div>;
}

function SettingsTask({ task, taskLink, settings, onSaved, onConflict, onRefresh, onSignedOut }: {
  task: Exclude<EmailTask, "delivery">; taskLink: TaskLink; settings: Settings;
  onSaved: (value: Settings, message: ReactNode) => void; onConflict: (message: string) => void;
  onRefresh: () => Promise<void>; onSignedOut: () => void;
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
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<string>();
  const nextTest = <> {taskLink("test", "Send a test email", "text-link")} to confirm the relay accepts it.</>;

  async function handle<T>(operation: string, request: Promise<{ data?: T; error?: unknown; response: Response }>, success: (value: T) => void) {
    setBusy(operation); setError(undefined); setFieldErrors({}); setNotice(undefined);
    try {
      const result = await request;
      if (result.data) success(result.data);
      else if (result.response.status === 401) onSignedOut();
      else {
        const message = problemMessage(result.error, result.response.status);
        if (result.response.status === 409) { await onRefresh(); onConflict(message); }
        else { setFieldErrors(problemFieldErrors(result.error)); setError(message); }
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
    }, headers: csrfHeaders }), (value) => onSaved(value, <>SMTP settings saved.{nextTest}</>));
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
    }), (value) => onSaved(value, <>SMTP password replaced.{nextTest}</>));
  }

  function clearPassword() {
    void handle("SMTP password", api.DELETE("/api/email/password", {
      params: { query: { version: String(settings.version) } }, headers: csrfHeaders,
    }), (value) => onSaved(value, "SMTP password cleared."));
  }

  async function sendTest(event: FormEvent) {
    event.preventDefault();
    setBusy("Test email"); setError(undefined); setFieldErrors({}); setNotice(undefined);
    try {
      const result = await api.POST("/api/email/test", { body: { recipient: testRecipient }, headers: csrfHeaders });
      if (result.data) setNotice("Accepted by SMTP. Inbox delivery is not confirmed.");
      else if (result.response.status === 401) onSignedOut();
      else { setFieldErrors(problemFieldErrors(result.error)); setError(problemMessage(result.error, result.response.status)); }
    } catch { setError("The test email could not be submitted."); }
    finally { setBusy(undefined); }
  }

  const status = <p className="muted">Version {settings.version}. Password: {settings.has_password ? "configured" : "not configured"}.</p>;
  return <>
    {error && <Alert tone="danger">{error}</Alert>}
    {notice && <Alert>{notice}</Alert>}
    {task === "relay" && <section className="panel" aria-labelledby="smtp-title"><h2 id="smtp-title">Relay settings</h2>
      {status}
      <form className="monitor-form" onSubmit={saveSettings}>
        <TextField error={fieldErrors.host} label="Relay host" required value={host} onChange={(event) => setHost(event.target.value)} />
        <TextField error={fieldErrors.port} label="Relay port" min={1} max={65535} required type="number" value={Number.isNaN(port) ? "" : port} onChange={(event) => setPort(event.target.valueAsNumber)} />
        <SelectField error={fieldErrors.security} label="Security" value={security} onChange={(event) => setSecurity(event.target.value)}><option value="starttls">STARTTLS</option><option value="tls">TLS from start</option><option value="none">None</option></SelectField>
        <TextField error={fieldErrors.sender_address} label="Sender address" required type="email" value={sender} onChange={(event) => setSender(event.target.value)} />
        <TextField error={fieldErrors.sender_name} label="Sender name" value={senderName} onChange={(event) => setSenderName(event.target.value)} />
        <TextField error={fieldErrors.public_base_url} label="Public detail URL" required type="url" value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} />
        <TextField error={fieldErrors.username} label="Authentication username" autoComplete="off" value={username} onChange={(event) => setUsername(event.target.value)} />
        <Button pending={busy === "SMTP settings"} disabled={busy !== undefined} type="submit" variant="primary">Save SMTP settings</Button>
      </form>
      <p className="muted">The password is managed separately: {taskLink("password", "SMTP password", "text-link")}.</p>
    </section>}
    {task === "password" && <section className="panel" aria-labelledby="password-title"><h2 id="password-title">SMTP password</h2>
      {status}
      <p className="muted">The saved value is never displayed. Replace it explicitly, then send a test message.</p>
      <form className="actions" onSubmit={savePassword}><TextField error={fieldErrors.password} label="New SMTP password" autoComplete="new-password" required type="password" value={password} onChange={(event) => setPassword(event.target.value)} /><Button pending={busy === "SMTP password"} disabled={busy !== undefined} type="submit" variant="primary">Replace password</Button></form>
      {settings.has_password && <Button disabled={busy !== undefined} onClick={clearPassword} type="button" variant="destructive">Clear password</Button>}
    </section>}
    {task === "recipients" && <section className="panel" aria-labelledby="defaults-title"><h2 id="defaults-title">Default recipients</h2>
      <p className="muted">Version {settings.version}. Copied to new projects only; existing projects keep their own recipients.</p>
      <form onSubmit={saveDefaults}><TextAreaField description="One address per line." error={fieldErrors.recipients} label="Recipients" value={defaults} onChange={(event) => setDefaults(event.target.value)} /><div className="actions"><Button pending={busy === "Default recipients"} disabled={busy !== undefined} type="submit" variant="primary">Save defaults</Button></div></form>
    </section>}
    {task === "test" && <section className="panel" aria-labelledby="test-title"><h2 id="test-title">Test email</h2>
      <p className="muted">Sends through the saved relay. Acceptance by SMTP does not confirm inbox delivery; check the {taskLink("delivery", "delivery history", "text-link")} for later attempts.</p>
      <form className="actions" onSubmit={(event) => void sendTest(event)}><TextField error={fieldErrors.recipient} label="Test recipient" required type="email" value={testRecipient} onChange={(event) => setTestRecipient(event.target.value)} /><Button pending={busy === "Test email"} disabled={busy !== undefined} type="submit" variant="primary">Send test email</Button></form>
    </section>}
  </>;
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

  return <div className="workspace settings-workspace">
    <PageHeader title="Email and maintenance" detail={`${project.name} · ${project.key}`} actions={<Button onClick={onBack} type="button">Back to investigation</Button>} />
    <DeliveryHistoryPanel projectKey={project.key} onSignedOut={onSignedOut} />
    <section className="panel" aria-labelledby="project-recipients-title"><h2 id="project-recipients-title">Project recipients</h2><p className="muted">Live list for future alerts. An empty list opts this project out.</p>
      {loading && !recipients && <LoadingState>Loading recipients…</LoadingState>}
      {error && <Alert tone="danger">{error}</Alert>}
      {notice && <Alert>{notice}</Alert>}
      {recipients && <form onSubmit={(event) => void save(event)}><p>Version {recipients.version}</p><label><span>One address per line</span><textarea value={draft} onChange={(event) => setDraft(event.target.value)} /></label><div className="actions"><Button disabled={busy} type="submit" variant="primary">Save project recipients</Button><Button disabled={loading} onClick={() => void load()} type="button">Refresh recipients</Button></div></form>}
    </section>
    <MaintenancePanel projectKey={project.key} onSignedOut={onSignedOut} />
  </div>;
}
