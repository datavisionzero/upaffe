import { useEffect, useRef, useState, type MouseEvent } from "react";
import type { components } from "@/api/schema";
import { Activity, ChevronRight, FolderOpen, Gauge, Globe, Mail, Menu, Radio, Settings, X, type LucideIcon } from "lucide-react";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { AccountMenu } from "@/shell/AccountMenu";
import { DashboardView } from "@/shell/DashboardView";
import { InstanceEmailView, ProjectEmailView } from "@/shell/EmailSettingsView";
import { MonitorsView } from "@/shell/MonitorsView";
import { MonitorInventoryView } from "@/shell/MonitorInventoryView";
import { ProjectOverviewView } from "@/shell/ProjectOverviewView";
import { ProjectsView } from "@/shell/ProjectsView";
import { PushMonitorsView } from "@/shell/PushMonitorsView";
import { monitorPath, projectPath, returnPath, useAppRoute, withReturn } from "@/shell/routes";

type Session = components["schemas"]["CurrentSessionResponse"];
type Project = components["schemas"]["ProjectResponse"];
type ProjectLoad = { key: string; project?: Project; loading: boolean; error?: string };

export function WorkspaceRouter({ session, onSignedOut }: {
  session: Session; onSignedOut: () => void;
}) {
  const { route, path, navigate } = useAppRoute();
  const search = path.includes("?") ? path.slice(path.indexOf("?")) : "";
  const projectKey = route.kind === "project" ? route.projectKey : undefined;
  const monitorKey = route.kind === "project" ? route.monitorKey : undefined;
  const [projectLoad, setProjectLoad] = useState<ProjectLoad>();
  const [projectOptions, setProjectOptions] = useState<Project[]>([]);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [narrow, setNarrow] = useState(() => window.matchMedia?.("(max-width: 800px)")?.matches ?? false);
  const [signingOut, setSigningOut] = useState(false);
  const [shellError, setShellError] = useState<string>();
  const navToggle = useRef<HTMLButtonElement>(null);
  const sidebar = useRef<HTMLElement>(null);

  useEffect(() => {
    const preference = window.matchMedia?.("(max-width: 800px)");
    const onChange = () => {
      setNarrow(!!preference?.matches);
      if (!preference?.matches) setMobileOpen(false);
    };
    preference?.addEventListener?.("change", onChange);
    return () => preference?.removeEventListener?.("change", onChange);
  }, []);

  useEffect(() => {
    if (!projectKey) return;
    let active = true;
    const start = window.setTimeout(async () => {
      setProjectLoad({ key: projectKey, loading: true });
      try {
        const result = await api.GET("/api/projects", { params: { query: { deleted: false } } });
        if (!active) return;
        if (result.data) {
          setProjectOptions(result.data);
          setProjectLoad({ key: projectKey, loading: false,
            project: result.data.find((value) => value.key === projectKey) });
        }
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
    if (!mobileOpen) return;
    const focus = window.setTimeout(() => sidebar.current?.querySelector<HTMLAnchorElement>("nav a")?.focus(), 0);
    const onEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setMobileOpen(false);
        window.setTimeout(() => navToggle.current?.focus(), 0);
      } else if (event.key === "Tab" && narrow) {
        const controls = Array.from(sidebar.current?.querySelectorAll<HTMLElement>("a,button,select") ?? [])
          .filter((element) => !element.hasAttribute("disabled"));
        const first = controls[0];
        const last = controls.at(-1);
        if (!first || !last) return;
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault(); last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault(); first.focus();
        }
      }
    };
    window.addEventListener("keydown", onEscape);
    return () => { window.clearTimeout(focus); window.removeEventListener("keydown", onEscape); };
  }, [mobileOpen, narrow]);

  async function signOut() {
    setSigningOut(true);
    setShellError(undefined);
    try {
      const { response, error } = await api.DELETE("/api/session", { headers: csrfHeaders });
      if (response.status === 204 || response.status === 401) onSignedOut();
      else setShellError(problemMessage(error, response.status));
    } catch {
      setShellError("Sign-out could not be completed.");
    } finally {
      setSigningOut(false);
    }
  }

  useEffect(() => {
    const title = route.kind === "project" ? monitorKey ?? projectLoad?.project?.name ?? "Project"
      : route.kind === "dashboard" ? "Dashboard"
      : route.kind === "inventory" ? "Monitors"
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

  function link(path: string, label: string, current: boolean, Icon?: LucideIcon) {
    return <a aria-current={current ? "page" : undefined} className={Icon ? "sidebar-link" : undefined} href={path}
      onClick={(event: MouseEvent<HTMLAnchorElement>) => {
        if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        const sameDestination = window.location.pathname + window.location.search === path;
        setMobileOpen(false);
        navigate(path);
        if (sameDestination && mobileOpen) window.setTimeout(() => navToggle.current?.focus(), 0);
      }}>{Icon && <Icon aria-hidden="true" size={16} strokeWidth={1.8} />}{label}</a>;
  }

  function navLink(path: string, label: string, current: boolean, Icon: LucideIcon) {
    return link(path, label, current, Icon);
  }

  let content;
  if (route.kind === "dashboard") {
    content = <DashboardView onNavigate={navigate} onSignedOut={onSignedOut} />;
  } else if (route.kind === "projects") {
    content = <ProjectsView onNavigate={navigate} onSignedOut={onSignedOut} session={session} />;
  } else if (route.kind === "inventory") {
    content = <MonitorInventoryView key={search} search={search} onNavigate={navigate} onSignedOut={onSignedOut} />;
  } else if (route.kind === "settings") {
    content = <InstanceEmailView onBack={() => navigate(returnPath(search, "/projects"))}
      onSignedOut={onSignedOut} />;
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
      content = <ProjectEmailView project={project}
        onBack={() => navigate(returnPath(search, projectPath(project.key)))}
        onSignedOut={onSignedOut} />;
    } else if (route.section === "overview") {
      content = <ProjectOverviewView key={project.key} project={project} onNavigate={navigate}
        onSignedOut={onSignedOut} />;
    } else if (route.section === "push") {
      content = <PushMonitorsView key={`${project.key}:push:${route.monitorKey ? path : ""}`} project={project}
        routeMonitorKey={route.monitorKey}
        onBack={() => navigate("/projects")}
        onBackToList={() => navigate(`${projectPath(project.key)}/push-monitors`)}
        onOpenHttp={() => navigate(`${projectPath(project.key)}/http-monitors`)}
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

  const locationLabel = route.kind === "project" ? projectLoad?.project?.name ?? route.projectKey
    : route.kind === "dashboard" ? "Dashboard" : route.kind === "inventory" ? "Monitors"
    : route.kind === "settings" ? "Settings" : route.kind === "missing" ? "Page unavailable" : "Projects";

  return <div className="workspace-layout">
    {mobileOpen && <button aria-label="Close navigation" className="sidebar-scrim" onClick={() => {
      setMobileOpen(false); window.setTimeout(() => navToggle.current?.focus(), 0);
    }} type="button" />}
    <aside aria-hidden={narrow && !mobileOpen} aria-label={narrow && mobileOpen ? "Navigation" : undefined}
      aria-modal={narrow && mobileOpen ? true : undefined} className="app-sidebar" data-open={mobileOpen}
      id="workspace-nav" inert={narrow && !mobileOpen} ref={sidebar}
      role={narrow && mobileOpen ? "dialog" : undefined}>
      <div className="sidebar-brand"><span aria-hidden="true" className="brand-mark" />upaffe</div>
      <nav aria-label="Primary" className="sidebar-nav">
        <p className="sidebar-label">Overview</p>
        {navLink("/dashboard", "Dashboard", route.kind === "dashboard", Gauge)}
        {navLink("/projects", "Projects", route.kind === "projects", FolderOpen)}
        {navLink("/monitors", "Monitors", route.kind === "inventory", Activity)}
        {route.kind === "project" && <>
          <p className="sidebar-label sidebar-project-name">{projectLoad?.project?.name ?? route.projectKey}</p>
          {navLink(projectPath(route.projectKey), "Project overview", route.section === "overview", Gauge)}
          {navLink(`${projectPath(route.projectKey)}/http-monitors`, "HTTP monitors", route.section === "http", Globe)}
          {navLink(`${projectPath(route.projectKey)}/push-monitors`, "Push monitors", route.section === "push", Radio)}
          {navLink(`${projectPath(route.projectKey)}/settings/email`, "Project email", route.section === "email", Mail)}
        </>}
        <p className="sidebar-label">Instance</p>
        {navLink(route.kind === "settings" ? "/settings/email" : withReturn("/settings/email", path),
          "Settings", route.kind === "settings", Settings)}
      </nav>
      <div className="sidebar-footer">
        <p className="sidebar-operator">{session.email}</p>
      </div>
    </aside>
    <div aria-hidden={narrow && mobileOpen} className="workspace-body" inert={narrow && mobileOpen}>
      <header className="workspace-topbar">
        <button aria-controls="workspace-nav" aria-expanded={mobileOpen} aria-label={mobileOpen ? "Close menu" : "Open menu"}
          className="nav-toggle" onClick={() => setMobileOpen((open) => !open)} ref={navToggle} type="button">
          {mobileOpen ? <X aria-hidden="true" size={18} /> : <Menu aria-hidden="true" size={18} />}
        </button>
        <span className="topbar-location">{locationLabel}</span>
        {route.kind === "project" && <label className="project-switcher">Project
          <select aria-label="Switch project" disabled={projectLoad?.key !== route.projectKey || projectLoad.loading}
            onChange={(event) => navigate(projectPath(event.target.value))}
            value={projectOptions.some((project) => project.key === route.projectKey) ? route.projectKey : ""}>
            <option value="">{projectLoad?.loading ? "Loading project…" : "Project unavailable"}</option>
            {projectOptions.map((project) => <option key={project.key} value={project.key}>{project.name}</option>)}
          </select>
        </label>}
        <AccountMenu email={session.email} error={shellError} signingOut={signingOut}
          onOpenSettings={() => navigate(route.kind === "settings" ? "/settings/email" : withReturn("/settings/email", path))}
          onSignOut={() => void signOut()} />
      </header>
      {route.kind === "project" && <nav aria-label="Breadcrumb" className="breadcrumbs">
        {link("/projects", "Projects", false)}<ChevronRight aria-hidden="true" size={13} />
        {link(projectPath(route.projectKey), projectLoad?.project?.name ?? route.projectKey,
          route.section === "overview")}
        {route.section === "http" && <><ChevronRight aria-hidden="true" size={13} />
          {link(`${projectPath(route.projectKey)}/http-monitors`, "HTTP monitors", !route.monitorKey)}</>}
        {route.section === "email" && <><ChevronRight aria-hidden="true" size={13} /><span aria-current="page">Settings</span></>}
        {route.section === "push" && <><ChevronRight aria-hidden="true" size={13} />
          {link(`${projectPath(route.projectKey)}/push-monitors`, "Push monitors", !route.monitorKey)}</>}
        {route.monitorKey && <><ChevronRight aria-hidden="true" size={13} /><span aria-current="page">{route.monitorKey}</span></>}
      </nav>}
      <main className="workspace-main" id="main-content">{content}</main>
    </div>
  </div>;
}
