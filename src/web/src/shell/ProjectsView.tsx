import { useCallback, useEffect, useState, type FormEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { TextField } from "@/components/Fields";
import { Alert, EmptyState, LoadingState, PageHeader, SectionHeading } from "@/components/Presentation";
import { projectPath, withReturn } from "@/shell/routes";

type Project = components["schemas"]["ProjectResponse"];
type Session = components["schemas"]["CurrentSessionResponse"];

type Props = {
  session: Session;
  onSignedOut: () => void;
  onNavigate: (path: string) => void;
};

export function ProjectsView({ session, onSignedOut, onNavigate }: Props) {
  const [deleted, setDeleted] = useState(false);
  const [projects, setProjects] = useState<Project[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const [key, setKey] = useState("");
  const [name, setName] = useState("");

  const load = useCallback(async () => {
    try {
      const { data, response, error: problem } = await api.GET("/api/projects", {
        params: { query: { deleted } },
      });
      if (data) {
        setProjects(data);
      }
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

  return (
    <div className="workspace projects-workspace">
      <PageHeader title="Projects" detail={`Signed in as ${session.email}`}
        actions={<Button onClick={() => onNavigate(withReturn("/settings/email", "/projects"))} type="button">Instance email</Button>} />

      <section aria-labelledby="create-title" className="panel">
        <h2 id="create-title">Create a project</h2>
        <form className="create-grid" onSubmit={create}>
          <TextField label="Immutable key" name="key" pattern="[a-z][a-z0-9-]{1,39}" placeholder="backup-jobs" required value={key} onChange={(event) => setKey(event.target.value)} />
          <TextField label="Display name" maxLength={100} name="name" placeholder="Backup jobs" required value={name} onChange={(event) => setName(event.target.value)} />
          <Button disabled={busy === "create"} type="submit" variant="primary">{busy === "create" ? "Creating…" : "Create"}</Button>
        </form>
      </section>

      <section aria-labelledby="list-title" className="panel">
        <SectionHeading title={deleted ? "Deleted projects" : "Live projects"} titleId="list-title"
          action={<Button onClick={() => {
            setError(undefined);
            setLoading(true);
            setDeleted((value) => !value);
          }} type="button">
            {deleted ? "Show live" : "Show deleted"}
          </Button>} />
        <p className="muted panel-description">Changes use the version currently shown.</p>

        {error && <Alert tone="danger">{error}</Alert>}
        {loading && <LoadingState>Loading projects…</LoadingState>}
        {!loading && projects.length === 0 && (
          <EmptyState>
            {deleted ? "No deleted projects." : "No projects yet. Create the first one above."}
          </EmptyState>
        )}
        {!loading && projects.length > 0 && (
          <div className="project-list">
            {projects.map((project) => (
              <ProjectRow
                key={project.id}
                busy={busy}
                onManage={() => onNavigate(projectPath(project.key))}
                onManageEmail={() => onNavigate(withReturn(`${projectPath(project.key)}/settings/email`, "/projects"))}
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
  onManageEmail: () => void;
  onMutate: (project: Project, operation: "rename" | "delete" | "restore", nextName?: string) => Promise<void>;
};

function ProjectRow({ project, busy, onManage, onManageEmail, onMutate }: RowProps) {
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
          <div className="actions"><Button className="manage-button" disabled={working} onClick={onManage} type="button">Manage monitors</Button><Button disabled={working} onClick={onManageEmail} type="button">Email and maintenance</Button></div>
          <form className="rename-row" onSubmit={(event) => {
            event.preventDefault();
            void onMutate(project, "rename", name);
          }}>
            <TextField label={`New display name for ${project.key}`} maxLength={100} required value={name} onChange={(event) => setName(event.target.value)} />
            <Button disabled={working || name === project.name} type="submit">Rename</Button>
            <Button variant="destructive" disabled={working} onClick={() => void onMutate(project, "delete")} type="button">Delete</Button>
          </form>
        </>
      )}
    </article>
  );
}
