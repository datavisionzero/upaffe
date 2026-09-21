import { LogOut, Monitor, Moon, Settings, Sun } from "lucide-react";

import { Button } from "@/components/Button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/DropdownMenu";
import { useTheme, type Theme } from "@/theme/ThemeProvider";

const themes = [
  { id: "light", label: "Light", icon: Sun },
  { id: "dark", label: "Dark", icon: Moon },
  { id: "system", label: "System", icon: Monitor },
] as const;

export function AccountMenu({ email, error, signingOut, onOpenSettings, onSignOut }: {
  email: string;
  error?: string;
  signingOut: boolean;
  onOpenSettings: () => void;
  onSignOut: () => void;
}) {
  const { theme, setTheme } = useTheme();
  const initials = (email.split("@")[0] || email).slice(0, 2).toUpperCase();

  return <div className="account-menu">
    {error && <p className="account-error" role="alert">{error}</p>}
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button aria-label={`Account: ${email}`} className="account-trigger"
        type="button" variant="subtle" />}>
        <span aria-hidden="true">{initials}</span>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="account-popup">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="account-identity">
            <strong>Signed in</strong><span>{email}</span>
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuLabel>Appearance</DropdownMenuLabel>
          <DropdownMenuRadioGroup onValueChange={(value) => setTheme(value as Theme)} value={theme}>
            {themes.map((candidate) => <DropdownMenuRadioItem closeOnClick key={candidate.id} value={candidate.id}>
              <candidate.icon aria-hidden="true" size={16} />{candidate.label}
            </DropdownMenuRadioItem>)}
          </DropdownMenuRadioGroup>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={onOpenSettings}>
          <Settings aria-hidden="true" size={16} />Settings
        </DropdownMenuItem>
        <DropdownMenuItem disabled={signingOut} onClick={onSignOut}>
          <LogOut aria-hidden="true" size={16} />{signingOut ? "Signing out…" : "Sign out"}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  </div>;
}
