import { ChevronsUpDown, FolderOpen } from "lucide-react";

import type { components } from "@/api/schema";
import { Button } from "@/components/Button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@/components/DropdownMenu";

type Project = components["schemas"]["ProjectResponse"];

export function ProjectSwitcher({ projects, currentKey, currentName, loading, error, onSwitch }: {
  projects: Project[];
  currentKey: string;
  currentName?: string;
  loading: boolean;
  error?: string;
  onSwitch: (key: string) => void;
}) {
  const currentAvailable = projects.some((project) => project.key === currentKey);
  const label = loading ? "Loading project…" : currentName ?? currentKey;

  return <DropdownMenu>
    <DropdownMenuTrigger render={<Button aria-label="Switch project" className="project-switcher-trigger"
      type="button" variant="subtle" />}>
      <FolderOpen aria-hidden="true" size={16} />
      <span>{label}</span>
      <ChevronsUpDown aria-hidden="true" size={14} />
    </DropdownMenuTrigger>
    <DropdownMenuContent align="start" className="project-switcher-popup">
      <DropdownMenuGroup>
        <DropdownMenuLabel>Projects</DropdownMenuLabel>
        {loading && <DropdownMenuItem disabled>Loading projects…</DropdownMenuItem>}
        {!loading && error && <DropdownMenuItem disabled>Projects unavailable</DropdownMenuItem>}
        {!loading && !error && projects.length === 0 && <DropdownMenuItem disabled>No available projects</DropdownMenuItem>}
        {!loading && projects.length > 0 && <DropdownMenuRadioGroup
          onValueChange={(value) => { if (typeof value === "string" && value !== currentKey) onSwitch(value); }}
          value={currentAvailable ? currentKey : ""}>
          {projects.map((project) => <DropdownMenuRadioItem closeOnClick key={project.key} value={project.key}>
            <span className="project-switcher-name">{project.name}</span>
            <code>{project.key}</code>
          </DropdownMenuRadioItem>)}
        </DropdownMenuRadioGroup>}
      </DropdownMenuGroup>
    </DropdownMenuContent>
  </DropdownMenu>;
}
