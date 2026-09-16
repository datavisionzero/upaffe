import { useCallback, useEffect, useState } from "react";

import { api } from "@/api/client";

export type Instance =
  | { state: "asking" }
  | { state: "answered"; version: string }
  | { state: "refused"; reason: string }
  | { state: "unreachable"; reason: string };

export function useInstance(): { instance: Instance; ask: () => void } {
  const [instance, setInstance] = useState<Instance>({ state: "asking" });

  const request = useCallback(() => {
    let abandoned = false;
    api
      .GET("/api/version")
      .then(({ data, response }) => {
        if (abandoned) return;
        setInstance(
          data
            ? { state: "answered", version: data.version }
            : { state: "refused", reason: `The instance refused the request (HTTP ${response.status}).` },
        );
      })
      .catch((failure: unknown) => {
        if (!abandoned) {
          setInstance({
            state: "unreachable",
            reason: failure instanceof Error ? failure.message : "The request failed.",
          });
        }
      });
    return () => {
      abandoned = true;
    };
  }, []);

  useEffect(() => request(), [request]);

  const ask = useCallback(() => {
    setInstance({ state: "asking" });
    request();
  }, [request]);

  return { instance, ask };
}
