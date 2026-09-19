import { useCallback, useEffect, useState } from "react";

export type AppRoute =
  | { kind: "projects" }
  | { kind: "dashboard" }
  | { kind: "settings" }
  | { kind: "project"; projectKey: string; section: "overview" | "http" | "push" | "email"; monitorKey?: string }
  | { kind: "missing" };

const validKey = /^[a-z][a-z0-9-]{1,39}$/;

function key(value: string): string | undefined {
  try {
    const decoded = decodeURIComponent(value);
    return validKey.test(decoded) ? decoded : undefined;
  } catch { return undefined; }
}

export function parseRoute(pathname: string): AppRoute {
  if (pathname === "/" || pathname === "/dashboard") return { kind: "dashboard" };
  if (pathname === "/projects") return { kind: "projects" };
  if (pathname === "/settings" || pathname === "/settings/email") return { kind: "settings" };
  const parts = pathname.split("/");
  if (parts[1] !== "projects" || !parts[2]) return { kind: "missing" };
  const projectKey = key(parts[2]);
  if (!projectKey) return { kind: "missing" };
  if (parts.length === 3) return { kind: "project", projectKey, section: "overview" };
  if (parts.length === 4 && parts[3] === "http-monitors")
    return { kind: "project", projectKey, section: "http" };
  if (parts.length === 4 && parts[3] === "push-monitors")
    return { kind: "project", projectKey, section: "push" };
  if (parts.length === 5 && parts[3] === "settings" && parts[4] === "email")
    return { kind: "project", projectKey, section: "email" };
  if (parts.length === 5 && (parts[3] === "http-monitors" || parts[3] === "push-monitors")) {
    const monitorKey = key(parts[4]);
    if (monitorKey) return { kind: "project", projectKey,
      section: parts[3] === "http-monitors" ? "http" : "push", monitorKey };
  }
  return { kind: "missing" };
}

export const projectPath = (projectKey: string) => `/projects/${encodeURIComponent(projectKey)}`;
export const monitorPath = (projectKey: string, type: "http" | "push", monitorKey: string) =>
  `${projectPath(projectKey)}/${type}-monitors/${encodeURIComponent(monitorKey)}`;

export function useAppRoute() {
  const [path, setPath] = useState(() => window.location.pathname + window.location.search);
  useEffect(() => {
    const sync = () => setPath(window.location.pathname + window.location.search);
    window.addEventListener("popstate", sync);
    return () => window.removeEventListener("popstate", sync);
  }, []);
  const navigate = useCallback((destination: string) => {
    if (window.location.pathname + window.location.search === destination) return;
    window.history.pushState({}, "", destination);
    setPath(window.location.pathname + window.location.search);
    document.documentElement.scrollTop = 0;
  }, []);
  return { route: parseRoute(path.split("?", 1)[0]), path, navigate };
}
