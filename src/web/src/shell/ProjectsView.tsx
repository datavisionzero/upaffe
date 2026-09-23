import { useCallback, useEffect, useMemo, useState, type FormEvent, type MouseEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { SelectField, TextField } from "@/components/Fields";
import { Alert, EmptyState, LoadingState, PageHeader } from "@/components/Presentation";
import { projectPath } from "@/shell/routes";

type Project = components["schemas"]["ProjectResponse"];
type ProjectState = "live" | "deleted";
type Sort = "name" | "key" | "updated";
type Order = "asc" | "desc";

type Props = {
  search: string;
  onSignedOut: () => void;
  onNavigate: (path: string) => void;
};

function readQuery(search: string) {
  const params = new URLSearchParams(search);
  const state: ProjectState = params.get("state") === "deleted" ? "deleted" : "live";
  const sortValue = params.get("sort");
  const sort: Sort = sortValue === "key" || sortValue === "updated" ? sortValue : "name";
  const order: Order = params.get("order") === "desc" ? "desc" : "asc";
  return { query: params.get("q")?.trim() ?? "", state, sort, order };
}

function projectsPath(query: string, state: ProjectState, sort: Sort, order: Order) {
  const params = new URLSearchParams();
  if (query) params.set("q", query);
  if (state === "deleted") params.set("state", state);
  if (sort !== "name") params.set("sort", sort);
  if (order !== "asc") params.set("order", order);
  const value = params.toString();
  return value ? `/projects?${value}` : "/projects";
}

export function ProjectsView({ search, onSignedOut, onNavigate }: Props) {
  const options = useMemo(() => readQuery(search), [search]);
  const [queryDraft, setQueryDraft] = useState(options.query);
  const [projects, setProjects] = useState<Project[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      const { data, response, error: problem } = await api.GET("/api/projects", {
        params: { query: { deleted: options.state === "deleted" } },
      });
      if (data) setProjects(data);
      else if (response.status === 401) onSignedOut();
      else setError(problemMessage(problem, response.status));
    } catch {
      setError("The project list could not be reached.");
    } finally {
      setLoading(false);
    }
  }, [options.state, onSignedOut]);

  useEffect(() => {
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load]);

  const visibleProjects = useMemo(() => {
    const needle = options.query.toLocaleLowerCase();
    return projects
      .filter((project) => !needle || project.key.toLocaleLowerCase().includes(needle)
        || project.name.toLocaleLowerCase().includes(needle))
      .sort((left, right) => {
        const leftValue = options.sort === "updated" ? left.updated_at : left[options.sort];
        const rightValue = options.sort === "updated" ? right.updated_at : right[options.sort];
        return leftValue.localeCompare(rightValue) * (options.order === "asc" ? 1 : -1);
      });
  }, [options, projects]);

  function navigate(next: Partial<typeof options>) {
    onNavigate(projectsPath(next.query ?? options.query, next.state ?? options.state,
      next.sort ?? options.sort, next.order ?? options.order));
  }

  async function mutate(project: Project, operation: "rename" | "delete" | "restore", nextName?: string) {
    setBusy(`${operation}:${project.key}`);
    setError(undefined);
    try {
      const result = operation === "rename"
        ? await api.PUT("/api/projects/{key}", {
            params: { path: { key: project.key } },
            body: { name: nextName ?? null, version: project.version },
            headers: csrfHeaders,
          })
        : operation === "delete"
          ? await api.DELETE("/api/projects/{key}", {
              params: { path: { key: project.key }, query: { version: String(project.version) } },
              headers: csrfHeaders,
            })
          : await api.POST("/api/projects/{key}/restore", {
              params: { path: { key: project.key } },
              body: { version: project.version },
              headers: csrfHeaders,
            });
      if (result.data) await load();
      else if (result.response.status === 401) onSignedOut();
      else {
        setError(problemMessage(result.error, result.response.status));
        await load();
      }
    } catch {
      setError("The project change could not be completed.");
    } finally {
      setBusy(undefined);
    }
  }

  return <div className="workspace projects-workspace">
    <PageHeader title="Projects" detail="Monitor groups and their current state."
      actions={<Button onClick={() => onNavigate("/projects/new")} type="button" variant="primary">New project</Button>} />

    <section aria-labelledby="project-results-title" className="project-inventory">
      <h2 className="visually-hidden" id="project-results-title">Project inventory</h2>
      <form className="project-toolbar" onSubmit={(event: FormEvent) => {
        event.preventDefault();
        navigate({ query: queryDraft.trim() });
      }}>
        <TextField label="Search projects" name="q" placeholder="Search by name or key"
          value={queryDraft} onChange={(event) => setQueryDraft(event.target.value)} />
        <Button type="submit">Search</Button>
        <SelectField label="Project state" value={options.state}
          onChange={(event) => navigate({ state: event.target.value as ProjectState })}>
          <option value="live">Live</option><option value="deleted">Deleted</option>
        </SelectField>
        <SelectField label="Sort projects" value={options.sort}
          onChange={(event) => navigate({ sort: event.target.value as Sort })}>
          <option value="name">Name</option><option value="key">Key</option><option value="updated">Last updated</option>
        </SelectField>
        <SelectField label="Sort order" value={options.order}
          onChange={(event) => navigate({ order: event.target.value as Order })}>
          <option value="asc">Ascending</option><option value="desc">Descending</option>
        </SelectField>
      </form>

      {!loading && !error && <p className="project-result-count">
        Showing {visibleProjects.length} of {projects.length} {options.state} projects
      </p>}
      {error && <Alert tone="danger">{error} <Button onClick={() => void load()} type="button">Try again</Button></Alert>}
      {loading && <LoadingState>Loading {options.state} projects…</LoadingState>}
      {!loading && !error && projects.length === 0 && <EmptyState>
        {options.state === "deleted" ? "No deleted projects." : "No live projects yet. Create the first project."}
      </EmptyState>}
      {!loading && !error && projects.length > 0 && visibleProjects.length === 0 && <EmptyState>
        No projects match this search.
      </EmptyState>}
      {!loading && !error && visibleProjects.length > 0 && <div className="project-list">
        {visibleProjects.map((project) => <ProjectRow key={project.id} busy={busy}
          onNavigate={onNavigate} onMutate={mutate} project={project} />)}
      </div>}
    </section>
  </div>;
}

type RowProps = {
  project: Project;
  busy?: string;
  onNavigate: (path: string) => void;
  onMutate: (project: Project, operation: "rename" | "delete" | "restore", nextName?: string) => Promise<void>;
};

function ProjectRow({ project, busy, onNavigate, onMutate }: RowProps) {
  const [renaming, setRenaming] = useState(false);
  const [name, setName] = useState(project.name);
  // The API omits absent timestamps, so a live project has no deletion time at all.
  const isDeleted = project.deleted_at != null;
  const deletedAt = isDeleted ? Date.parse(project.deleted_at!) : NaN;
  const working = busy?.endsWith(`:${project.key}`) ?? false;

  function open(event: MouseEvent<HTMLAnchorElement>) {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    onNavigate(projectPath(project.key));
  }

  return <article className="project-card">
    <div className="project-facts">
      <div>
        {isDeleted ? <h3>{project.name}</h3> : <h3><a href={projectPath(project.key)} onClick={open}>{project.name}</a></h3>}
        <code>{project.key}</code>
      </div>
      <span className="version">version {project.version}</span>
    </div>
    {isDeleted ? <div className="actions">
      <span className="muted">{Number.isNaN(deletedAt) ? "Deleted" : `Deleted ${new Date(deletedAt).toLocaleString()}`}</span>
      <Button disabled={working} onClick={() => void onMutate(project, "restore")} pending={busy === `restore:${project.key}`} type="button">Restore</Button>
    </div> : <>
      <p className="project-summary">Open this project to manage its monitors, maintenance, and email delivery.</p>
      {renaming ? <form className="rename-row" onSubmit={(event) => {
        event.preventDefault();
        void onMutate(project, "rename", name).then(() => setRenaming(false));
      }}>
        <TextField autoFocus label={`New display name for ${project.key}`} maxLength={100} required
          value={name} onChange={(event) => setName(event.target.value)} />
        <Button disabled={working || name === project.name} pending={busy === `rename:${project.key}`} type="submit">Save</Button>
        <Button disabled={working} onClick={() => { setName(project.name); setRenaming(false); }} type="button">Cancel</Button>
      </form> : <div className="project-row-actions">
        <Button disabled={working} onClick={() => setRenaming(true)} type="button" variant="subtle">Rename</Button>
        <ConfirmDialog confirmLabel="Delete project" description={`Delete ${project.name}? Its monitoring data will remain available for restoration.`}
          onConfirm={() => onMutate(project, "delete")} pending={busy === `delete:${project.key}`}
          title="Delete project" triggerLabel="Delete" />
      </div>}
    </>}
  </article>;
}
