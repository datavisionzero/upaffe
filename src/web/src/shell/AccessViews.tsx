import { useState, type FormEvent } from "react";

import { api } from "@/api/client";
import { problemMessage } from "@/api/problems";
import { Button } from "@/components/Button";

type BootstrapProps = {
  available: boolean;
  onEstablished: () => void;
  onRefresh: () => void;
};

export function BootstrapView({ available, onEstablished, onRefresh }: BootstrapProps) {
  const [email, setEmail] = useState("");
  const [proof, setProof] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();

  async function submit(event: FormEvent) {
    event.preventDefault();
    const body = { email, proof, password };
    setProof("");
    setPassword("");
    setError(undefined);
    setSubmitting(true);
    try {
      const { response, error: problem } = await api.POST("/api/bootstrap", { body });
      if (response.status === 204) onEstablished();
      else setError(problemMessage(problem, response.status));
    } catch {
      setError("The instance could not be reached.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section aria-labelledby="bootstrap-title" className="panel">
      <p className="eyebrow">First start</p>
      <h1 id="bootstrap-title">Establish the operator</h1>
      <p className="lede">Create the one human operator for this instance. The proof and password are cleared as soon as they are submitted.</p>

      {!available && (
        <div className="notice" role="status">
          No live bootstrap proof is available. Arm a fresh proof on the instance, then check again.
        </div>
      )}
      {error && <div className="error" role="alert">{error}</div>}

      <form className="form-stack" onSubmit={submit}>
        <label>
          <span>Email</span>
          <input autoComplete="username" name="email" required type="email" value={email} onChange={(event) => setEmail(event.target.value)} />
        </label>
        <label>
          <span>Bootstrap proof</span>
          <input autoComplete="off" name="proof" required type="password" value={proof} onChange={(event) => setProof(event.target.value)} />
        </label>
        <label>
          <span>Password</span>
          <input autoComplete="new-password" minLength={12} name="password" required type="password" value={password} onChange={(event) => setPassword(event.target.value)} />
        </label>
        <div className="actions">
          <Button disabled={!available || submitting} type="submit">{submitting ? "Establishing…" : "Establish operator"}</Button>
          {!available && <Button onClick={onRefresh} type="button">Check again</Button>}
        </div>
      </form>
    </section>
  );
}

type SignInProps = { onSignedIn: () => void };

export function SignInView({ onSignedIn }: SignInProps) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();

  async function submit(event: FormEvent) {
    event.preventDefault();
    const body = { email, password };
    setPassword("");
    setError(undefined);
    setSubmitting(true);
    try {
      const { response, error: problem } = await api.POST("/api/session", { body });
      if (response.status === 204) onSignedIn();
      else setError(problemMessage(problem, response.status));
    } catch {
      setError("The instance could not be reached.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section aria-labelledby="signin-title" className="panel panel-narrow">
      <p className="eyebrow">Operator access</p>
      <h1 id="signin-title">Sign in</h1>
      <p className="lede">Use the operator account established for this instance.</p>
      {error && <div className="error" role="alert">{error}</div>}
      <form className="form-stack" onSubmit={submit}>
        <label>
          <span>Email</span>
          <input autoComplete="username" name="email" required type="email" value={email} onChange={(event) => setEmail(event.target.value)} />
        </label>
        <label>
          <span>Password</span>
          <input autoComplete="current-password" name="password" required type="password" value={password} onChange={(event) => setPassword(event.target.value)} />
        </label>
        <Button disabled={submitting} type="submit">{submitting ? "Signing in…" : "Sign in"}</Button>
      </form>
    </section>
  );
}
