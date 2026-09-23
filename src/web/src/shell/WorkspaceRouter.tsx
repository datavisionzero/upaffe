import { useEffect, useRef, useState, type MouseEvent, type ReactNode } from "react";
import { Dialog } from "@base-ui/react/dialog";
import type { components } from "@/api/schema";
import { Activity, ChevronRight, FolderOpen, Gauge, Globe, Mail, Menu, Radio, Settings, X, type LucideIcon } from "lucide-react";

import { api } from "@/api/client";
import { csrfHeaders, problemMessage } from "@/api/problems";
import { AccountMenu } from "@/shell/AccountMenu";
import { DashboardView } from "@/shell/DashboardView";
import { InstanceEmailView, ProjectEmailView } from "@/shell/EmailSettingsView";
import { MonitorsView, NewHttpMonitorView } from "@/shell/MonitorsView";
import { MonitorInventoryView } from "@/shell/MonitorInventoryView";
import { NewProjectView } from "@/shell/NewProjectView";
import { ProjectOverviewView } from "@/shell/ProjectOverviewView";
import { ProjectSwitcher } from "@/shell/ProjectSwitcher";
import { ProjectsView } from "@/shell/ProjectsView";
import { NewPushMonitorView, PushMonitorsView } from "@/shell/PushMonitorsView";
import { emailTaskPath, monitorListPath, monitorPath, newMonitorPath, projectPath, returnPath, useAppRoute, withReturn } from "@/shell/routes";

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
  const sectionTitle = route.kind !== "project" ? undefined
    : route.create ? `New ${route.section === "http" ? "HTTP" : "push"} monitor`
    : route.section === "http" ? "HTTP monitors" : route.section === "push" ? "Push monitors" : undefined;
  const [projectLoad, setProjectLoad] = useState<ProjectLoad>();
  const [projectOptions, setProjectOptions] = useState<Project[]>([]);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [narrow, setNarrow] = useState(() => window.matchMedia?.("(max-width: 800px)")?.matches ?? false);
  const [signingOut, setSigningOut] = useState(false);
  const [shellError, setShellError] = useState<string>();
  const layout = useRef<HTMLDivElement>(null);
  const sidebar = useRef<HTMLDivElement>(null);

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
    const title = route.kind === "project" ? monitorKey ?? sectionTitle ?? projectLoad?.project?.name ?? "Project"
      : route.kind === "dashboard" ? "Dashboard"
      : route.kind === "inventory" ? "Monitors"
      : route.kind === "settings" ? "Settings"
      : route.kind === "new-project" ? "New project"
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
  }, [path, route.kind, monitorKey, sectionTitle, projectLoad?.project?.name]);

  function link(path: string, label: ReactNode, current: boolean, Icon?: LucideIcon, className?: string) {
    return <a aria-current={current ? "page" : undefined} className={className ?? (Icon ? "sidebar-link" : undefined)} href={path}
      onClick={(event: MouseEvent<HTMLAnchorElement>) => {
        if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        setMobileOpen(false);
        navigate(path);
      }}>{Icon && <Icon aria-hidden="true" size={16} strokeWidth={1.8} />}{label}</a>;
  }

  function navLink(path: string, label: string, current: boolean, Icon: LucideIcon) {
    return link(path, label, current, Icon);
  }

  let content;
  if (route.kind === "dashboard") {
    content = <DashboardView onNavigate={navigate} onSignedOut={onSignedOut} />;
  } else if (route.kind === "projects") {
    content = <ProjectsView key={search} search={search} onNavigate={navigate} onSignedOut={onSignedOut} />;
  } else if (route.kind === "new-project") {
    content = <NewProjectView onNavigate={navigate} onSignedOut={onSignedOut} />;
  } else if (route.kind === "inventory") {
    content = <MonitorInventoryView key={search} search={search} onNavigate={navigate} onSignedOut={onSignedOut} />;
  } else if (route.kind === "settings") {
    content = <InstanceEmailView key={route.task} task={route.task}
      taskLink={(task, label, className) => link(emailTaskPath(task) + search, label, task === route.task, undefined, className)}
      onBack={() => navigate(returnPath(search, "/projects"))}
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
    } else if (route.section === "push" && route.create) {
      content = <NewPushMonitorView key={`${project.key}:new-push`} project={project}
        onCancel={() => navigate(monitorListPath(project.key, "push"))}
        onCreated={(key) => navigate(monitorPath(project.key, "push", key))}
        onSignedOut={onSignedOut} />;
    } else if (route.section === "push") {
      content = <PushMonitorsView key={`${project.key}:push:${route.monitorKey ? path : ""}`} project={project}
        routeMonitorKey={route.monitorKey}
        onBackToList={() => navigate(monitorListPath(project.key, "push"))}
        onCreate={() => navigate(newMonitorPath(project.key, "push"))}
        onOpenHttp={() => navigate(monitorListPath(project.key, "http"))}
        onOpenMonitor={(key) => navigate(monitorPath(project.key, "push", key))}
        onSignedOut={onSignedOut} />;
    } else if (route.create) {
      content = <NewHttpMonitorView key={`${project.key}:new-http`} project={project}
        onCancel={() => navigate(monitorListPath(project.key, "http"))}
        onCreated={(key) => navigate(monitorPath(project.key, "http", key))}
        onSignedOut={onSignedOut} />;
    } else {
      content = <MonitorsView key={`${project.key}:http:${route.monitorKey ? path : ""}`} project={project}
        routeMonitorKey={route.monitorKey}
        onBackToList={() => navigate(monitorListPath(project.key, "http"))}
        onCreate={() => navigate(newMonitorPath(project.key, "http"))}
        onOpenPush={() => navigate(monitorListPath(project.key, "push"))}
        onOpenMonitor={(key) => navigate(monitorPath(project.key, "http", key))}
        onSignedOut={onSignedOut} />;
    }
  }

  const locationLabel = route.kind === "project" ? projectLoad?.project?.name ?? route.projectKey
    : route.kind === "dashboard" ? "Dashboard" : route.kind === "inventory" ? "Monitors"
    : route.kind === "settings" ? "Settings" : route.kind === "new-project" ? "New project"
    : route.kind === "missing" ? "Page unavailable" : "Projects";

  return <Dialog.Root modal={narrow} onOpenChange={(open) => { if (narrow) setMobileOpen(open); }}
    open={narrow ? mobileOpen : true}>
  <div className="workspace-layout" ref={layout}>
    <Dialog.Portal className="sidebar-portal" container={layout}>
      <Dialog.Backdrop className="sidebar-scrim" />
      <Dialog.Popup className="app-sidebar" finalFocus={narrow ? true : false} id="workspace-nav"
        initialFocus={narrow ? () => sidebar.current?.querySelector<HTMLAnchorElement>("nav a") ?? true : false}
        ref={sidebar}>
        <Dialog.Title className="visually-hidden">Navigation</Dialog.Title>
        <div className="sidebar-brand"><span aria-hidden="true" className="brand-mark" />upaffe
          <Dialog.Close aria-label="Close navigation" className="sidebar-close" type="button"><X aria-hidden="true" size={18} /></Dialog.Close>
        </div>
        <nav aria-label="Primary" className="sidebar-nav">
        <p className="sidebar-label">Overview</p>
        {navLink("/dashboard", "Dashboard", route.kind === "dashboard", Gauge)}
        {navLink("/projects", "Projects", route.kind === "projects" || route.kind === "new-project", FolderOpen)}
        {navLink("/monitors", "Monitors", route.kind === "inventory", Activity)}
        {route.kind === "project" && <>
          <p className="sidebar-label sidebar-project-name">{projectLoad?.project?.name ?? route.projectKey}</p>
          {navLink(projectPath(route.projectKey), "Project overview", route.section === "overview", Gauge)}
          {navLink(`${projectPath(route.projectKey)}/http-monitors`, "HTTP monitors", route.section === "http", Globe)}
          {navLink(`${projectPath(route.projectKey)}/push-monitors`, "Push monitors", route.section === "push", Radio)}
          {navLink(`${projectPath(route.projectKey)}/settings/email`, "Project email", route.section === "email", Mail)}
        </>}
        <p className="sidebar-label">Instance</p>
        {navLink(route.kind === "settings" ? `/settings/email${search}` : withReturn("/settings/email", path),
          "Settings", route.kind === "settings", Settings)}
        </nav>
        <div className="sidebar-footer">
          <p className="sidebar-operator">{session.email}</p>
        </div>
      </Dialog.Popup>
    </Dialog.Portal>
    <div className="workspace-body">
      <header className="workspace-topbar">
        <Dialog.Trigger aria-controls="workspace-nav" aria-label="Open menu" className="nav-toggle" type="button">
          <Menu aria-hidden="true" size={18} />
        </Dialog.Trigger>
        {route.kind === "project" ? <ProjectSwitcher currentKey={route.projectKey}
          currentName={projectLoad?.project?.name} error={projectLoad?.error}
          loading={projectLoad?.key !== route.projectKey || projectLoad.loading}
          onSwitch={(key) => navigate(projectPath(key))} projects={projectOptions} />
          : <span className="topbar-location">{locationLabel}</span>}
        <AccountMenu email={session.email} error={shellError} signingOut={signingOut}
          onOpenSettings={() => navigate(route.kind === "settings" ? `/settings/email${search}` : withReturn("/settings/email", path))}
          onSignOut={() => void signOut()} />
      </header>
      {route.kind === "project" && <nav aria-label="Breadcrumb" className="breadcrumbs">
        {link("/projects", "Projects", false)}<ChevronRight aria-hidden="true" size={13} />
        {link(projectPath(route.projectKey), projectLoad?.project?.name ?? route.projectKey,
          route.section === "overview")}
        {route.section === "http" && <><ChevronRight aria-hidden="true" size={13} />
          {link(monitorListPath(route.projectKey, "http"), "HTTP monitors", !route.monitorKey && !route.create)}</>}
        {route.section === "email" && <><ChevronRight aria-hidden="true" size={13} /><span aria-current="page">Settings</span></>}
        {route.section === "push" && <><ChevronRight aria-hidden="true" size={13} />
          {link(monitorListPath(route.projectKey, "push"), "Push monitors", !route.monitorKey && !route.create)}</>}
        {route.create && <><ChevronRight aria-hidden="true" size={13} /><span aria-current="page">New {route.section === "http" ? "HTTP" : "push"} monitor</span></>}
        {route.monitorKey && <><ChevronRight aria-hidden="true" size={13} /><span aria-current="page">{route.monitorKey}</span></>}
      </nav>}
      <main className="workspace-main" id="main-content">{content}</main>
    </div>
  </div>
  </Dialog.Root>;
}
