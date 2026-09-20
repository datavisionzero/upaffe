import { useEffect, useState, type FormEvent, type MouseEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { monitorPath, projectPath } from "@/shell/routes";

type Page = components["schemas"]["MonitorInventoryPage"];
type Item = Page["items"][number];
type Project = components["schemas"]["ProjectResponse"];

const pageSize = 25;

function filters(search: string) {
  const params = new URLSearchParams(search);
  const offset = Number(params.get("offset") ?? 0);
  return {
    project: params.get("project") ?? "",
    state: params.get("state") ?? "",
    type: params.get("type") ?? "",
    q: params.get("q") ?? "",
    offset: Number.isInteger(offset) && offset >= 0 ? offset : 0,
  };
}

function destination(values: ReturnType<typeof filters>) {
  const params = new URLSearchParams();
  if (values.project) params.set("project", values.project);
  if (values.state) params.set("state", values.state);
  if (values.type) params.set("type", values.type);
  if (values.q.trim()) params.set("q", values.q.trim());
  if (values.offset) params.set("offset", String(values.offset));
  const query = params.toString();
  return query ? `/monitors?${query}` : "/monitors";
}

function time(value: string | null) {
  return value ? new Date(value).toLocaleString() : "None recorded";
}

function context(item: Item) {
  if (item.type === "http") return <>HTTP check · every {item.interval_seconds}s
    {item.target_url && <span className="inventory-target"> · {item.target_url}</span>}</>;
  return <>{item.mode === "job_completion" ? "Job completion report" : "State report"}
    {` · every ${item.interval_seconds}s + ${item.tolerance_seconds ?? 0}s tolerance`}</>;
}

export function MonitorInventoryView({ search, onNavigate, onSignedOut }: {
  search: string;
  onNavigate: (path: string) => void;
  onSignedOut: () => void;
}) {
  const current = filters(search);
  const [project, setProject] = useState(current.project);
  const [state, setState] = useState(current.state);
  const [type, setType] = useState(current.type);
  const [q, setQ] = useState(current.q);
  const [projects, setProjects] = useState<Project[]>([]);
  const [page, setPage] = useState<Page>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  useEffect(() => {
    const next = filters(search);
    let active = true;
    const timer = window.setTimeout(async () => {
      setLoading(true); setError(undefined); setPage(undefined);
      try {
        const result = await api.GET("/api/monitors", { params: { query: {
          project: next.project || undefined, state: next.state || undefined,
          type: next.type || undefined, q: next.q || undefined,
          limit: pageSize, offset: next.offset,
        } } });
        if (!active) return;
        if (result.data) setPage(result.data);
        else if (result.response.status === 401) onSignedOut();
        else setError(problemMessage(result.error, result.response.status));
      } catch {
        if (active) setError("The monitor inventory could not be reached.");
      } finally { if (active) setLoading(false); }
    }, 0);
    return () => { active = false; window.clearTimeout(timer); };
  }, [search, onSignedOut]);

  useEffect(() => {
    let active = true;
    const timer = window.setTimeout(async () => {
      try {
        const result = await api.GET("/api/projects", { params: { query: { deleted: false } } });
        if (!active) return;
        if (result.data) setProjects(result.data);
        else if (result.response.status === 401) onSignedOut();
      } catch { /* The project key in the URL remains usable. */ }
    }, 0);
    return () => { active = false; window.clearTimeout(timer); };
  }, [onSignedOut]);

  function submit(event: FormEvent) {
    event.preventDefault();
    onNavigate(destination({ project, state, type, q, offset: 0 }));
  }

  function link(path: string, label: string) {
    return <a href={path} onClick={(event: MouseEvent<HTMLAnchorElement>) => {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      event.preventDefault(); onNavigate(path);
    }}>{label}</a>;
  }

  return <div className="workspace monitor-inventory">
    <header className="workspace-header">
      <div>
        <p className="eyebrow">Across projects</p>
        <h1>Monitors</h1>
        <p className="muted">Find what each monitor covers and inspect its current evidence.</p>
      </div>
    </header>
    <form className="panel inventory-filters" onSubmit={submit} role="search">
      <label><span>Search monitors</span><input maxLength={120} value={q} onChange={(event) => setQ(event.target.value)} placeholder="Name, purpose, project, or safe target" /></label>
      <label><span>Project</span><select value={project} onChange={(event) => setProject(event.target.value)}>
        <option value="">All projects</option>
        {project && !projects.some((value) => value.key === project) && <option value={project}>{project}</option>}
        {projects.map((value) => <option key={value.key} value={value.key}>{value.name} · {value.key}</option>)}
      </select></label>
      <label><span>State</span><select value={state} onChange={(event) => setState(event.target.value)}>
        <option value="">All states</option><option value="failing">Failing</option>
        <option value="untested">Untested</option><option value="paused">Paused</option>
        <option value="healthy">Healthy</option>
      </select></label>
      <label><span>Type</span><select value={type} onChange={(event) => setType(event.target.value)}>
        <option value="">All types</option><option value="http">HTTP check</option>
        <option value="push">Push report</option>
      </select></label>
      <Button type="submit">Apply filters</Button>
    </form>

    {loading && <p role="status">Loading monitor inventory…</p>}
    {error && <div className="error" role="alert">{error}</div>}
    {page && <section className="panel" aria-labelledby="inventory-results-title">
      <div className="section-heading"><div>
        <h2 id="inventory-results-title">Inventory</h2>
        <p className="muted">{page.total === 0 ? "No matching monitors" :
          page.items.length === 0 ? `${page.total} matching monitors on earlier pages` :
            `Showing ${page.offset + 1}–${page.offset + page.items.length} of ${page.total}`}</p>
      </div></div>
      {page.items.length === 0 ? <p className="empty" role="status">{page.total > 0
        ? "This page is empty. Return to an earlier page." : current.project || current.state || current.type || current.q
          ? "No monitors match these filters." : "No monitors configured yet."}</p> :
        <table className="inventory-table">
          <thead><tr><th scope="col">Monitor and project</th><th scope="col">Purpose</th>
            <th scope="col">Method and cadence</th><th scope="col">State</th>
            <th scope="col">Latest evidence</th><th scope="col">Next due</th></tr></thead>
          <tbody>{page.items.map((item) => <tr key={`${item.type}:${item.id}`}>
            <td data-label="Monitor and project"><strong>{link(monitorPath(item.project_key, item.type as "http" | "push", item.key), item.name)}</strong>
              <small>{link(projectPath(item.project_key), item.project_name)} · {item.key}</small></td>
            <td data-label="Purpose"><span className="inventory-purpose">{item.purpose || "Purpose not documented"}</span></td>
            <td data-label="Method and cadence">{context(item)}</td>
            <td data-label="State"><span className={`state state-${item.state}`}>{item.state}</span>
              {item.incident_open && <small>Incident open</small>}
              {item.overdue && <small>Overdue</small>}
              {item.maintenance_until && <small>Maintenance until {time(item.maintenance_until)}</small>}</td>
            <td data-label="Latest evidence">{time(item.latest_observation_at)}
              <small>Last success: {time(item.last_success_at)}</small></td>
            <td data-label="Next due">{item.next_due_at ? time(item.next_due_at) : "No active deadline"}</td>
          </tr>)}</tbody>
        </table>}
      <nav aria-label="Inventory pages" className="inventory-pages">
        <Button disabled={page.offset === 0} onClick={() => onNavigate(destination({ ...current, offset: Math.max(0, page.offset - pageSize) }))} type="button">Previous</Button>
        <Button disabled={!page.has_more} onClick={() => onNavigate(destination({ ...current, offset: page.offset + pageSize }))} type="button">Next</Button>
      </nav>
    </section>}
  </div>;
}
