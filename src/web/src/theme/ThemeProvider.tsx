/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";

export type Theme = "light" | "dark" | "system";

const storageKey = "upaffe-theme";
const darkQuery = "(prefers-color-scheme: dark)";
const ThemeContext = createContext<{ theme: Theme; setTheme: (theme: Theme) => void } | null>(null);

function isTheme(value: string | null): value is Theme {
  return value === "light" || value === "dark" || value === "system";
}

function readTheme(): Theme {
  try {
    const value = window.localStorage.getItem(storageKey);
    return isTheme(value) ? value : "system";
  } catch {
    return "system";
  }
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setSelectedTheme] = useState<Theme>(readTheme);

  const setTheme = useCallback((next: Theme) => {
    setSelectedTheme(next);
    try {
      window.localStorage.setItem(storageKey, next);
    } catch {
      // Private browsing can disable storage; the current choice still works.
    }
  }, []);

  useEffect(() => {
    const preference = window.matchMedia?.(darkQuery);
    const apply = () => {
      const dark = theme === "dark" || (theme === "system" && !!preference?.matches);
      document.documentElement.classList.toggle("dark", dark);
      document.documentElement.classList.toggle("light", !dark);
    };
    apply();
    if (theme === "system") preference?.addEventListener?.("change", apply);
    return () => preference?.removeEventListener?.("change", apply);
  }, [theme]);

  useEffect(() => {
    const onStorage = (event: StorageEvent) => {
      if (event.key === storageKey) setSelectedTheme(isTheme(event.newValue) ? event.newValue : "system");
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, []);

  return <ThemeContext.Provider value={{ theme, setTheme }}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) throw new Error("useTheme requires ThemeProvider");
  return context;
}
