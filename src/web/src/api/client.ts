import createClient from "openapi-fetch";
import type { paths } from "./schema";

/** The only way the web application reaches the instance that served it. */
export const api = createClient<paths>({
  baseUrl: window.location.origin,
  credentials: "same-origin",
  fetch: (request) => globalThis.fetch(request),
});
