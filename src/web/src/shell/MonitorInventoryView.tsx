import { useEffect, useState, type FormEvent, type MouseEvent, type ReactNode } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { SelectField, TextField } from "@/components/Fields";
import { Alert, EmptyState, LoadingState, PageHeader, SectionHeading, StatusBadge } from "@/components/Presentation";
import { monitorStateTone } from "@/components/status";
import { monitorPath, newMonitorPath, projectPath } from "@/shell/routes";

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
  const [projectsKnown, setProjectsKnown] = useState(false);
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
        if (result.data) { setProjects(result.data); setProjectsKnown(true); }
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

  const filtered = !!(current.project || current.state || current.type || current.q);
  const onlyProject = !!current.project && !current.state && !current.type && !current.q;
  const paged = !!page && (page.offset > 0 || page.has_more);

  return <div className="workspace monitor-inventory">
    <PageHeader title="Monitors" detail="Find what each monitor covers and inspect its current evidence across projects." />
    <form className="panel inventory-filters" onSubmit={submit} role="search">
      <TextField label="Search monitors" maxLength={120} value={q} onChange={(event) => setQ(event.target.value)} placeholder="Name, purpose, project, or safe target" />
      <SelectField label="Project" value={project} onChange={(event) => setProject(event.target.value)}>
        <option value="">All projects</option>
        {project && !projects.some((value) => value.key === project) && <option value={project}>{project}</option>}
        {projects.map((value) => <option key={value.key} value={value.key}>{value.name} · {value.key}</option>)}
      </SelectField>
      <SelectField label="State" value={state} onChange={(event) => setState(event.target.value)}>
        <option value="">All states</option><option value="failing">Failing</option>
        <option value="untested">Untested</option><option value="paused">Paused</option>
        <option value="healthy">Healthy</option>
      </SelectField>
      <SelectField label="Type" value={type} onChange={(event) => setType(event.target.value)}>
        <option value="">All types</option><option value="http">HTTP check</option>
        <option value="push">Push report</option>
      </SelectField>
      <Button type="submit" variant="primary">Apply filters</Button>
    </form>

    {loading && <LoadingState>Loading monitor inventory…</LoadingState>}
    {error && <Alert tone="danger">{error}</Alert>}
    {page && <section className="panel" aria-labelledby="inventory-results-title">
      <SectionHeading title="Inventory" titleId="inventory-results-title" eyebrow={page.total === 0 ? filtered ? "No matching monitors" : "No monitors" :
        page.items.length === 0 ? `${page.total} matching monitors on earlier pages` :
          `Showing ${page.offset + 1}–${page.offset + page.items.length} of ${page.total}`} />
      {page.items.length === 0 ? page.total > 0
        ? <div className="inventory-empty">
            <EmptyState>This page is past the last result.</EmptyState>
            <Button onClick={() => onNavigate(destination({ ...current, offset: 0 }))} type="button">Go to the first page</Button>
          </div>
        : <InventoryEmpty filtered={filtered} link={link} onNavigate={onNavigate}
            project={onlyProject ? projects.find((value) => value.key === current.project) : undefined}
            projects={projects} projectsKnown={projectsKnown} /> :
        <table className="ui-table inventory-table">
          <thead><tr><th scope="col">Monitor and project</th><th scope="col">Purpose</th>
            <th scope="col">Method and cadence</th><th scope="col">State</th>
            <th scope="col">Latest evidence</th><th scope="col">Next due</th></tr></thead>
          <tbody>{page.items.map((item) => <tr key={`${item.type}:${item.id}`}>
            <td data-label="Monitor and project"><strong>{link(monitorPath(item.project_key, item.type as "http" | "push", item.key), item.name)}</strong>
              <small>{link(projectPath(item.project_key), item.project_name)} · {item.key}</small></td>
            <td data-label="Purpose"><span className="inventory-purpose">{item.purpose || "Purpose not documented"}</span></td>
            <td data-label="Method and cadence">{context(item)}</td>
            <td data-label="State"><StatusBadge tone={monitorStateTone(item.state)}>{item.state}</StatusBadge>
              {item.incident_open && <small>Incident open</small>}
              {item.overdue && <small>Overdue</small>}
              {item.maintenance_until && <small>Maintenance until {time(item.maintenance_until)}</small>}</td>
            <td data-label="Latest evidence">{time(item.latest_observation_at)}
              <small>Last success: {time(item.last_success_at)}</small></td>
            <td data-label="Next due">{item.next_due_at ? time(item.next_due_at) : "No active deadline"}</td>
          </tr>)}</tbody>
        </table>}
      {paged && <nav aria-label="Inventory pages" className="inventory-pages">
        <Button disabled={page.offset === 0} onClick={() => onNavigate(destination({ ...current, offset: Math.max(0, page.offset - pageSize) }))} type="button">Previous</Button>
        <Button disabled={!page.has_more} onClick={() => onNavigate(destination({ ...current, offset: page.offset + pageSize }))} type="button">Next</Button>
      </nav>}
    </section>}
  </div>;
}

/** Explains why the inventory is empty and offers the next useful step. */
function InventoryEmpty({ filtered, project, projects, projectsKnown, link, onNavigate }: {
  filtered: boolean;
  project?: Project;
  projects: Project[];
  projectsKnown: boolean;
  link: (path: string, label: string) => ReactNode;
  onNavigate: (path: string) => void;
}) {
  const [target, setTarget] = useState("");
  const chosen = target || projects[0]?.key || "";

  if (project) return <div className="inventory-empty">
    <EmptyState>{project.name} has no monitors yet.</EmptyState>
    <div className="actions">
      <Button onClick={() => onNavigate(newMonitorPath(project.key, "http"))} type="button" variant="primary">New HTTP monitor</Button>
      <Button onClick={() => onNavigate(newMonitorPath(project.key, "push"))} type="button">New push monitor</Button>
      <Button onClick={() => onNavigate("/monitors")} type="button" variant="subtle">Show all projects</Button>
    </div>
  </div>;

  if (filtered) return <div className="inventory-empty">
    <EmptyState>No monitors match these filters.</EmptyState>
    <Button onClick={() => onNavigate("/monitors")} type="button">Reset filters</Button>
  </div>;

  if (projectsKnown && projects.length === 0) return <div className="inventory-empty">
    <EmptyState>No projects yet. Monitors belong to a project, so create one first.</EmptyState>
    <Button onClick={() => onNavigate("/projects/new")} type="button" variant="primary">Create a project</Button>
  </div>;

  if (!projectsKnown) return <div className="inventory-empty">
    <EmptyState>No monitors configured yet. Open a project from {link("/projects", "Projects")} to add one.</EmptyState>
  </div>;

  return <div className="inventory-empty">
    <EmptyState>No monitors configured yet. {projects.length > 1
      ? "Choose a project and the kind of monitor to create."
      : `Choose the kind of monitor to create in ${projects[0].name}.`}</EmptyState>
    <form className="actions inventory-create" onSubmit={(event: FormEvent) => event.preventDefault()}>
      {projects.length > 1 && <SelectField label="Project for the new monitor" value={chosen}
        onChange={(event) => setTarget(event.target.value)}>
        {projects.map((value) => <option key={value.key} value={value.key}>{value.name} · {value.key}</option>)}
      </SelectField>}
      <Button onClick={() => onNavigate(newMonitorPath(chosen, "http"))} type="button" variant="primary">
        {projects.length > 1 ? "New HTTP monitor" : `New HTTP monitor in ${projects[0].name}`}</Button>
      <Button onClick={() => onNavigate(newMonitorPath(chosen, "push"))} type="button">
        {projects.length > 1 ? "New push monitor" : `New push monitor in ${projects[0].name}`}</Button>
    </form>
  </div>;
}
