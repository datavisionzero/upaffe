import { Input } from "@base-ui/react/input";
import { useId, type ComponentProps, type ReactNode } from "react";

type FieldDetails = { label: string; description?: string; error?: string };

function useFieldIds(id?: string) {
  const generated = useId();
  const fieldId = id ?? generated;
  return { fieldId, descriptionId: `${fieldId}-description`, errorId: `${fieldId}-error` };
}

function FieldMessages({ description, error, descriptionId, errorId }: {
  description?: string; error?: string; descriptionId: string; errorId: string;
}) {
  return <>
    {description && <span className="ui-field-description" id={descriptionId}>{description}</span>}
    {error && <span className="ui-field-error" id={errorId} role="alert">{error}</span>}
  </>;
}

export function TextField({ label, description, error, id, className = "", ...props }:
  ComponentProps<"input"> & FieldDetails) {
  const ids = useFieldIds(id);
  const describedBy = [props["aria-describedby"], description && ids.descriptionId, error && ids.errorId]
    .filter(Boolean).join(" ") || undefined;
  return <div className={`ui-field ${className}`}>
    <label htmlFor={ids.fieldId}>{label}</label>
    <Input {...props} aria-describedby={describedBy} aria-invalid={!!error || undefined}
      aria-errormessage={error ? ids.errorId : undefined}
      className="ui-input" id={ids.fieldId} />
    <FieldMessages {...ids} description={description} error={error} />
  </div>;
}

export function SelectField({ label, description, error, id, className = "", children, ...props }:
  ComponentProps<"select"> & FieldDetails & { children: ReactNode }) {
  const ids = useFieldIds(id);
  const describedBy = [props["aria-describedby"], description && ids.descriptionId, error && ids.errorId]
    .filter(Boolean).join(" ") || undefined;
  return <div className={`ui-field ${className}`}>
    <label htmlFor={ids.fieldId}>{label}</label>
    <select {...props} aria-describedby={describedBy} aria-errormessage={error ? ids.errorId : undefined}
      aria-invalid={!!error || undefined}
      className="ui-input" id={ids.fieldId}>{children}</select>
    <FieldMessages {...ids} description={description} error={error} />
  </div>;
}

export function CheckboxField({ label, description, error, id, className = "", ...props }:
  Omit<ComponentProps<"input">, "type"> & FieldDetails) {
  const ids = useFieldIds(id);
  const describedBy = [props["aria-describedby"], description && ids.descriptionId, error && ids.errorId]
    .filter(Boolean).join(" ") || undefined;
  return <div className={`ui-field ${className}`}>
    <label className="ui-checkbox" htmlFor={ids.fieldId}>
      <input {...props} aria-describedby={describedBy} aria-errormessage={error ? ids.errorId : undefined}
        aria-invalid={!!error || undefined}
        id={ids.fieldId} type="checkbox" />
      <span>{label}</span>
    </label>
    <FieldMessages {...ids} description={description} error={error} />
  </div>;
}
