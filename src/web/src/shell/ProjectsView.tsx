import { useCallback, useEffect, useState, type FormEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { MonitorsView } from "@/shell/MonitorsView";

type Project = components["schemas"]["ProjectResponse"];
type Session = components["schemas"]["CurrentSessionResponse"];

type Props = {
  session: Session;
  onSignedOut: () => void;
};

export function ProjectsView({ session, onSignedOut }: Props) {
  const [deleted, setDeleted] = useState(false);
  const [projects, setProjects] = useState<Project[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [key, setKey] = useState("");
  const [name, setName] = useState("");
  const [selectedProject, setSelectedProject] = useState<Project>();

  const load = useCallback(async () => {
    try {
      const { data, response, error: problem } = await api.GET("/api/projects", {
        params: { query: { deleted } },
      });
      if (data) setProjects(data);
      else if (response.status === 401) onSignedOut();
      else setError(problemMessage(problem, response.status));
    } catch {
      setError("The project list could not be reached.");
    } finally {
      setLoading(false);
    }
  }, [deleted, onSignedOut]);

  useEffect(() => {
    const start = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(start);
  }, [load]);

  async function create(event: FormEvent) {
    event.preventDefault();
    setBusy("create");
    setError(undefined);
    try {
      const { data, response, error: problem } = await api.POST("/api/projects", {
        body: { key, name },
        headers: csrfHeaders,
      });
      if (data) {
        setKey("");
        setName("");
        if (deleted) {
          setLoading(true);
          setDeleted(false);
        } else await load();
      } else if (response.status === 401) onSignedOut();
      else setError(problemMessage(problem, response.status));
    } catch {
      setError("The project could not be created.");
    } finally {
      setBusy(undefined);
    }
  }

  async function mutate(
    project: Project,
    operation: "rename" | "delete" | "restore",
    nextName?: string,
  ) {
    setBusy(`${operation}:${project.key}`);
    setError(undefined);
    try {
      const result =
        operation === "rename"
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

  async function signOut() {
    setBusy("signout");
    try {
      const { response, error: problem } = await api.DELETE("/api/session", { headers: csrfHeaders });
      if (response.status === 204 || response.status === 401) onSignedOut();
      else setError(problemMessage(problem, response.status));
    } catch {
      setError("Sign-out could not be completed.");
    } finally {
      setBusy(undefined);
    }
  }

  if (selectedProject) {
    return (
      <MonitorsView
        onBack={() => setSelectedProject(undefined)}
        onSignedOut={onSignedOut}
        project={selectedProject}
      />
    );
  }

  return (
    <div className="workspace">
      <header className="workspace-header">
        <div>
          <p className="eyebrow">upaffe</p>
          <h1>Projects</h1>
          <p className="muted">Signed in as {session.email}</p>
        </div>
        <Button disabled={busy === "signout"} onClick={signOut} type="button">Sign out</Button>
      </header>

      <section aria-labelledby="create-title" className="panel">
        <h2 id="create-title">Create a project</h2>
        <form className="create-grid" onSubmit={create}>
          <label>
            <span>Immutable key</span>
            <input name="key" pattern="[a-z][a-z0-9-]{1,39}" placeholder="backup-jobs" required value={key} onChange={(event) => setKey(event.target.value)} />
          </label>
          <label>
            <span>Display name</span>
            <input maxLength={100} name="name" placeholder="Backup jobs" required value={name} onChange={(event) => setName(event.target.value)} />
          </label>
          <Button disabled={busy === "create"} type="submit">{busy === "create" ? "Creating…" : "Create"}</Button>
        </form>
      </section>

      <section aria-labelledby="list-title" className="panel">
        <div className="section-heading">
          <div>
            <h2 id="list-title">{deleted ? "Deleted projects" : "Live projects"}</h2>
            <p className="muted">Changes use the version currently shown.</p>
          </div>
          <Button onClick={() => {
            setError(undefined);
            setLoading(true);
            setDeleted((value) => !value);
          }} type="button">
            {deleted ? "Show live" : "Show deleted"}
          </Button>
        </div>

        {error && <div className="error" role="alert">{error}</div>}
        {loading && <p role="status">Loading projects…</p>}
        {!loading && projects.length === 0 && (
          <div className="empty" role="status">
            {deleted ? "No deleted projects." : "No projects yet. Create the first one above."}
          </div>
        )}
        {!loading && projects.length > 0 && (
          <div className="project-list">
            {projects.map((project) => (
              <ProjectRow
                key={project.id}
                busy={busy}
                onManage={() => setSelectedProject(project)}
                onMutate={mutate}
                project={project}
              />
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

type RowProps = {
  project: Project;
  busy?: string;
  onManage: () => void;
  onMutate: (project: Project, operation: "rename" | "delete" | "restore", nextName?: string) => Promise<void>;
};

function ProjectRow({ project, busy, onManage, onMutate }: RowProps) {
  const [name, setName] = useState(project.name);
  const isDeleted = project.deleted_at !== null;
  const working = busy?.endsWith(`:${project.key}`) ?? false;

  return (
    <article className="project-card">
      <div className="project-facts">
        <div>
          <h3>{project.name}</h3>
          <code>{project.key}</code>
        </div>
        <span className="version">version {project.version}</span>
      </div>
      {isDeleted ? (
        <div className="actions">
          <span className="muted">Deleted {new Date(project.deleted_at!).toLocaleString()}</span>
          <Button disabled={working} onClick={() => void onMutate(project, "restore")} type="button">Restore</Button>
        </div>
      ) : (
        <>
          <Button className="manage-button" disabled={working} onClick={onManage} type="button">Manage monitors</Button>
          <form className="rename-row" onSubmit={(event) => {
            event.preventDefault();
            void onMutate(project, "rename", name);
          }}>
            <label>
              <span className="sr-only">New display name for {project.key}</span>
              <input aria-label={`New display name for ${project.key}`} maxLength={100} required value={name} onChange={(event) => setName(event.target.value)} />
            </label>
            <Button disabled={working || name === project.name} type="submit">Rename</Button>
            <Button className="danger" disabled={working} onClick={() => void onMutate(project, "delete")} type="button">Delete</Button>
          </form>
        </>
      )}
    </article>
  );
}
