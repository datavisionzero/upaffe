export function MonitorPurpose({ purpose }: { purpose: string | null | undefined }) {
  return <p className={`card-purpose${purpose ? "" : " card-purpose-missing"}`}>
    {purpose || "Purpose not documented"}
  </p>;
}
