import { useEffect, useState, type MouseEvent } from "react";
import type { components } from "@/api/schema";

import { api } from "@/api/client";
import { problemMessage } from "@/api/problems";
import { DashboardView } from "@/shell/DashboardView";
import { InstanceEmailView, ProjectEmailView } from "@/shell/EmailSettingsView";
import { MonitorsView } from "@/shell/MonitorsView";
import { ProjectsView } from "@/shell/ProjectsView";
import { PushMonitorsView } from "@/shell/PushMonitorsView";
import { monitorPath, projectPath, useAppRoute } from "@/shell/routes";

type Session = components["schemas"]["CurrentSessionResponse"];
type Project = components["schemas"]["ProjectResponse"];
type ProjectLoad = { key: string; project?: Project; loading: boolean; error?: string };

export function WorkspaceRouter({ session, onSignedOut }: {
  session: Session; onSignedOut: () => void;
}) {
  const { route, path, navigate } = useAppRoute();
  const projectKey = route.kind === "project" ? route.projectKey : undefined;
  const monitorKey = route.kind === "project" ? route.monitorKey : undefined;
  const [projectLoad, setProjectLoad] = useState<ProjectLoad>();

  useEffect(() => {
    if (!projectKey) return;
    let active = true;
    const start = window.setTimeout(async () => {
      setProjectLoad({ key: projectKey, loading: true });
      try {
        const result = await api.GET("/api/projects", { params: { query: { deleted: false } } });
        if (!active) return;
        if (result.data) setProjectLoad({ key: projectKey, loading: false,
          project: result.data.find((value) => value.key === projectKey) });
        else if (result.response.status === 401) onSignedOut();
        else setProjectLoad({ key: projectKey, loading: false,
          error: problemMessage(result.error, result.response.status) });
      } catch {
        if (active) setProjectLoad({ key: projectKey, loading: false,
          error: "The project could not be reached." });
      }
    }, 0);
    return () => { active = false; window.clearTimeout(start); };
  }, [projectKey, onSignedOut]);

  useEffect(() => {
    const title = route.kind === "project" ? monitorKey ?? projectLoad?.project?.name ?? "Project"
      : route.kind === "dashboard" ? "Dashboard"
      : route.kind === "settings" ? "Settings"
      : route.kind === "missing" ? "Page unavailable" : "Projects";
    document.title = `${title} · upaffe`;
    const focus = () => {
      const heading = document.querySelector<HTMLElement>(".workspace h1");
      if (!heading) return false;
      heading.tabIndex = -1;
      heading.focus();
      return true;
    };
    let observer: MutationObserver | undefined;
    let expiry: number | undefined;
    const timer = window.setTimeout(() => {
      if (focus()) return;
      observer = new MutationObserver(() => { if (focus()) observer?.disconnect(); });
      observer.observe(document.body, { childList: true, subtree: true });
      expiry = window.setTimeout(() => observer?.disconnect(), 5000);
    }, 0);
    return () => { window.clearTimeout(timer); observer?.disconnect(); if (expiry) window.clearTimeout(expiry); };
  }, [path, route.kind, monitorKey, projectLoad?.project?.name]);

  function link(path: string, label: string, current: boolean) {
    return <a aria-current={current ? "page" : undefined} href={path}
      onClick={(event: MouseEvent<HTMLAnchorElement>) => {
        if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        navigate(path);
      }}>{label}</a>;
  }

  let content;
  if (route.kind === "dashboard") {
    content = <DashboardView onNavigate={navigate} onSignedOut={onSignedOut} />;
  } else if (route.kind === "projects") {
    content = <ProjectsView onNavigate={navigate} onSignedOut={onSignedOut} session={session} />;
  } else if (route.kind === "settings") {
    content = <InstanceEmailView onBack={() => navigate("/projects")} onSignedOut={onSignedOut} />;
  } else if (route.kind === "missing") {
    content = <div className="workspace"><section className="panel">
      <h1>Page unavailable</h1><p>The requested page does not exist.</p>
      {link("/projects", "Go to projects", false)}
    </section></div>;
  } else {
    const project = projectLoad?.key === route.projectKey ? projectLoad.project : undefined;
    if (projectLoad?.key !== route.projectKey || projectLoad.loading) {
      content = <div className="workspace"><p role="status">Loading project…</p></div>;
    } else if (!project) {
      content = <div className="workspace"><section className="panel">
        <h1>Project unavailable</h1>
        <p role={projectLoad.error ? "alert" : undefined}>
          {projectLoad.error ?? "This project is missing or has been deleted."}
        </p>
        {link("/projects", "Go to projects", false)}
      </section></div>;
    } else if (route.section === "email") {
      content = <ProjectEmailView project={project} onBack={() => navigate(projectPath(project.key))}
        onSignedOut={onSignedOut} />;
    } else if (route.section === "push") {
      content = <PushMonitorsView key={`${project.key}:push:${route.monitorKey ? path : ""}`} project={project}
        routeMonitorKey={route.monitorKey}
        onBack={() => navigate("/projects")}
        onBackToList={() => navigate(`${projectPath(project.key)}/push-monitors`)}
        onOpenHttp={() => navigate(projectPath(project.key))}
        onOpenMonitor={(key) => navigate(monitorPath(project.key, "push", key))}
        onSignedOut={onSignedOut} />;
    } else {
      content = <MonitorsView key={`${project.key}:http:${route.monitorKey ? path : ""}`} project={project}
        routeMonitorKey={route.monitorKey}
        onBack={() => navigate("/projects")}
        onBackToList={() => navigate(`${projectPath(project.key)}/http-monitors`)}
        onOpenPush={() => navigate(`${projectPath(project.key)}/push-monitors`)}
        onOpenMonitor={(key) => navigate(monitorPath(project.key, "http", key))}
        onSignedOut={onSignedOut} />;
    }
  }

  return <>
    <nav aria-label="Primary" className="primary-nav">
      <span className="brand">upaffe</span>
      {link("/dashboard", "Dashboard", route.kind === "dashboard")}
      {link("/projects", "Projects", route.kind === "projects")}
      {link("/settings/email", "Settings", route.kind === "settings")}
    </nav>
    {route.kind === "project" && <nav aria-label="Breadcrumb" className="breadcrumbs">
      {link("/projects", "Projects", false)}<span aria-hidden="true">/</span>
      {link(projectPath(route.projectKey), projectLoad?.project?.name ?? route.projectKey,
        route.section === "http" && !route.monitorKey)}
      {route.section === "email" && <><span aria-hidden="true">/</span><span aria-current="page">Settings</span></>}
      {route.section === "push" && <><span aria-hidden="true">/</span>
        {link(`${projectPath(route.projectKey)}/push-monitors`, "Push monitors", !route.monitorKey)}</>}
      {route.monitorKey && <><span aria-hidden="true">/</span><span aria-current="page">{route.monitorKey}</span></>}
    </nav>}
    {content}
  </>;
}
