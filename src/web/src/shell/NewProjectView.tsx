import { Dialog } from "@base-ui/react/dialog";
import { useEffect, useState, type FormEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { TextField } from "@/components/Fields";
import { Alert, PageHeader } from "@/components/Presentation";
import { projectPath } from "@/shell/routes";

type Problem = components["schemas"]["ProblemResponse"];

export function NewProjectView({ onSignedOut, onNavigate }: {
  onSignedOut: () => void;
  onNavigate: (path: string) => void;
}) {
  const [key, setKey] = useState("");
  const [name, setName] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [discardOpen, setDiscardOpen] = useState(false);
  const dirty = key.length > 0 || name.length > 0;

  useEffect(() => {
    const unload = (event: BeforeUnloadEvent) => {
      if (!dirty || pending) return;
      event.preventDefault();
    };
    const cancel = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || discardOpen || pending) return;
      event.preventDefault();
      if (dirty) setDiscardOpen(true);
      else onNavigate("/projects");
    };
    window.addEventListener("beforeunload", unload);
    window.addEventListener("keydown", cancel, true);
    return () => {
      window.removeEventListener("beforeunload", unload);
      window.removeEventListener("keydown", cancel, true);
    };
  }, [dirty, discardOpen, pending, onNavigate]);

  function cancel() {
    if (dirty) setDiscardOpen(true);
    else onNavigate("/projects");
  }

  async function create(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setError(undefined);
    setFieldErrors({});
    try {
      const { data, response, error: problem } = await api.POST("/api/projects", {
        body: { key, name }, headers: csrfHeaders,
      });
      if (data) onNavigate(projectPath(data.key));
      else if (response.status === 401) onSignedOut();
      else {
        const errors = (problem as Problem | undefined)?.errors;
        setFieldErrors(Object.fromEntries(Object.entries(errors ?? {}).map(([field, messages]) => [field, messages[0]])));
        setError(problemMessage(problem, response.status));
      }
    } catch {
      setError("The project could not be created.");
    } finally {
      setPending(false);
    }
  }

  return <div className="workspace new-project-workspace">
    <PageHeader title="New project" detail="Create a separate monitoring boundary with a permanent key." />
    <form className="new-project-form" onSubmit={create}>
      {error && <Alert tone="danger">{error}</Alert>}
      <TextField autoFocus description="Lowercase letters, numbers, and hyphens. This cannot be changed later."
        error={fieldErrors.key} label="Immutable key" name="key" pattern="[a-z][a-z0-9-]{1,39}"
        placeholder="backup-jobs" required value={key} onChange={(event) => setKey(event.target.value)} />
      <TextField error={fieldErrors.name} label="Display name" maxLength={100} name="name"
        placeholder="Backup jobs" required value={name} onChange={(event) => setName(event.target.value)} />
      <div className="actions">
        <Button disabled={pending} onClick={cancel} type="button">Cancel</Button>
        <Button pending={pending} type="submit" variant="primary">{pending ? "Creating…" : "Create project"}</Button>
      </div>
    </form>

    <Dialog.Root open={discardOpen} onOpenChange={setDiscardOpen}>
      <Dialog.Portal>
        <Dialog.Backdrop className="ui-dialog-backdrop" />
        <Dialog.Popup className="ui-dialog-popup">
          <Dialog.Title>Discard new project?</Dialog.Title>
          <Dialog.Description>The key and display name you entered will be lost.</Dialog.Description>
          <div className="ui-dialog-actions">
            <Dialog.Close render={<Button type="button" />}>Keep editing</Dialog.Close>
            <Button onClick={() => onNavigate("/projects")} type="button" variant="destructive">Discard</Button>
          </div>
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  </div>;
}
