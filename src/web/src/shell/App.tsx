import { Button } from "@/components/Button";
import { useInstance } from "@/shell/useInstance";

export function App() {
  const { instance, ask } = useInstance();

  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col justify-center gap-8 px-5 py-12">
      <header className="flex flex-col gap-2">
        <h1 className="text-3xl font-semibold tracking-tight">upaffe</h1>
        <p className="text-muted text-balance">
          The monitoring foundation is connected. Projects and monitors arrive in later epics.
        </p>
      </header>

      <section aria-labelledby="instance" className="border-line flex flex-col gap-3 rounded-lg border p-5">
        <h2 id="instance" className="text-sm font-medium tracking-wide uppercase">
          Technical connection
        </h2>

        {instance.state === "asking" && <p role="status">Asking the instance…</p>}
        {instance.state === "answered" && (
          <p role="status">
            The API answered as version <span className="text-accent font-mono">{instance.version}</span>.
          </p>
        )}
        {instance.state === "refused" && <p role="alert">{instance.reason}</p>}
        {instance.state === "unreachable" && (
          <p role="alert">Nothing answered at this address. {instance.reason}</p>
        )}

        {instance.state !== "asking" && (
          <div>
            <Button onClick={ask}>Ask again</Button>
          </div>
        )}
      </section>

      <footer className="text-muted text-sm">
        This shell and the <code className="font-mono">ua</code> CLI use the same checked-in API contract.
      </footer>
    </main>
  );
}
