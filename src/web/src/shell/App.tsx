import { useCallback, useEffect, useState } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { problemMessage } from "@/api/problems";
import { BootstrapView, SignInView } from "@/shell/AccessViews";
import { WorkspaceRouter } from "@/shell/WorkspaceRouter";
import { ThemeProvider } from "@/theme/ThemeProvider";

type Session = components["schemas"]["CurrentSessionResponse"];
type Screen =
  | { state: "loading" }
  | { state: "bootstrap" }
  | { state: "signin" }
  | { state: "projects"; session: Session }
  | { state: "failed"; reason: string };

export function App() {
  return <ThemeProvider><AppContent /></ThemeProvider>;
}

function AppContent() {
  const [screen, setScreen] = useState<Screen>({ state: "loading" });

  const enter = useCallback(async () => {
    try {
      const bootstrap = await api.GET("/api/bootstrap");
      if (!bootstrap.data) {
        setScreen({ state: "failed", reason: problemMessage(bootstrap.error, bootstrap.response.status) });
        return;
      }
      if (bootstrap.data.required) {
        setScreen({ state: "bootstrap" });
        return;
      }

      const session = await api.GET("/api/session");
      if (session.data) setScreen({ state: "projects", session: session.data });
      else if (session.response.status === 401) setScreen({ state: "signin" });
      else setScreen({ state: "failed", reason: problemMessage(session.error, session.response.status) });
    } catch {
      setScreen({ state: "failed", reason: "The instance could not be reached." });
    }
  }, []);

  useEffect(() => {
    const start = window.setTimeout(() => void enter(), 0);
    return () => window.clearTimeout(start);
  }, [enter]);

  const reenter = useCallback(() => {
    setScreen({ state: "loading" });
    void enter();
  }, [enter]);
  const signedOut = useCallback(() => setScreen({ state: "signin" }), []);

  const content = (
    <>
      {screen.state === "loading" && <p className="loading" role="status">Opening the instance…</p>}
      {screen.state === "bootstrap" && (
        <BootstrapView onRefresh={reenter} />
      )}
      {screen.state === "signin" && <SignInView onSignedIn={reenter} />}
      {screen.state === "projects" && <WorkspaceRouter onSignedOut={signedOut} session={screen.session} />}
      {screen.state === "failed" && (
        <section className="panel panel-narrow">
          <p className="eyebrow">Connection</p>
          <h1>Instance unavailable</h1>
          <p role="alert">{screen.reason}</p>
          <button className="retry" onClick={reenter} type="button">Try again</button>
        </section>
      )}
    </>
  );
  return screen.state === "projects"
    ? <div className="app-shell">{content}</div>
    : <main className="app-shell">{content}</main>;
}
