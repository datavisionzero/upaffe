import { useCallback, useRef, useState, type FormEvent } from "react";

import { api } from "@/api/client";
import { csrfHeaders, problemFieldErrors, problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";
import { DiscardDialog } from "@/components/DiscardGuard";
import { useDiscardGuard } from "@/components/useDiscardGuard";
import { TextField } from "@/components/Fields";
import { Alert, PageHeader } from "@/components/Presentation";
import { projectPath } from "@/shell/routes";

export function NewProjectView({ onSignedOut, onNavigate }: {
  onSignedOut: () => void;
  onNavigate: (path: string) => void;
}) {
  const [key, setKey] = useState("");
  const [name, setName] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const keyInput = useRef<HTMLInputElement>(null);
  const leave = useCallback(() => onNavigate("/projects"), [onNavigate]);
  const guard = useDiscardGuard({ dirty: key.length > 0 || name.length > 0, pending, onLeave: leave });

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
        setFieldErrors(problemFieldErrors(problem));
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
        placeholder="backup-jobs" ref={keyInput} required value={key} onChange={(event) => setKey(event.target.value)} />
      <TextField error={fieldErrors.name} label="Display name" maxLength={100} name="name"
        placeholder="Backup jobs" required value={name} onChange={(event) => setName(event.target.value)} />
      <div className="actions">
        <Button disabled={pending} onClick={guard.cancel} type="button">Cancel</Button>
        <Button pending={pending} type="submit" variant="primary">{pending ? "Creating…" : "Create project"}</Button>
      </div>
    </form>

    <DiscardDialog description="The key and display name you entered will be lost." onDiscard={leave}
      onOpenChange={guard.setOpen} open={guard.open} returnFocus={keyInput} title="Discard new project?" />
  </div>;
}
