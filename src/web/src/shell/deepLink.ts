export type MonitorLink = { projectKey: string; monitorType: "http" | "push"; monitorKey: string; incidentId?: string };

/** The public detail URL used by incident email is resolved after sign-in. */
export function monitorLink(): MonitorLink | undefined {
  const match = /^\/projects\/([^/]+)\/(http|push)-monitors\/([^/]+)$/.exec(window.location.pathname);
  if (!match) return undefined;
  try {
    const incident = new URLSearchParams(window.location.search).get("incident");
    return {
      projectKey: decodeURIComponent(match[1]),
      monitorType: match[2] as "http" | "push",
      monitorKey: decodeURIComponent(match[3]),
      incidentId: incident && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(incident) ? incident : undefined,
    };
  } catch { return undefined; }
}
