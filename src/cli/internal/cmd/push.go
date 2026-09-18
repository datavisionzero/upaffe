package cmd

import (
	"fmt"
	"io"
	"net/http"
	"strconv"
	"time"

	"github.com/datavisionzero/upaffe/src/cli/internal/api"
	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
	"github.com/spf13/cobra"
)

func newPush(output io.Writer, getenv environment) *cobra.Command {
	flags := &managementFlags{}
	command := &cobra.Command{Use: "push", Short: "Manage and inspect push monitors by immutable key"}
	bindManagementFlags(command, flags)
	command.AddCommand(newPushCreate(output, getenv, flags))
	command.AddCommand(newPushList(output, getenv, flags))
	command.AddCommand(newPushGet(output, getenv, flags))
	command.AddCommand(newPushUpdate(output, getenv, flags))
	command.AddCommand(newPushDelete(output, getenv, flags))
	command.AddCommand(newPushStateChange(output, getenv, flags, true))
	command.AddCommand(newPushStateChange(output, getenv, flags, false))
	command.AddCommand(newPushReports(output, getenv, flags))
	command.AddCommand(newPushIncidents(output, getenv, flags))
	command.AddCommand(newPushCredential(output, getenv, flags))
	return command
}

func newPushCreate(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var file string
	command := &cobra.Command{
		Use: "create <project-key>", Short: "Create a push monitor from a JSON document", Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			request, err := readMonitorInput[api.CreatePushMonitorRequest](command, file)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.CreatePushMonitorWithResponse(ctx, arguments[0], request)
			if err != nil {
				return unreachable(err)
			}
			var monitor *api.PushMonitorResponse
			switch response.StatusCode() {
			case http.StatusOK:
				monitor = response.JSON200
			case http.StatusCreated:
				monitor = response.JSON201
			}
			if monitor == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writePushMonitor(output, monitor, flags.asJSON)
		},
	}
	command.Flags().StringVar(&file, "file", "", "JSON configuration file, or - for stdin")
	return command
}

func newPushList(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use: "list <project-key>", Short: "List live push monitors and their current state", Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ListPushMonitorsWithResponse(ctx, arguments[0])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			if flags.asJSON {
				return writeJSON(output, response.JSON200)
			}
			for index := range *response.JSON200 {
				if err := writePushMonitorText(output, &(*response.JSON200)[index]); err != nil {
					return err
				}
			}
			return nil
		},
	}
}

func newPushGet(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use: "get <project-key> <monitor-key>", Short: "Read one push monitor and its current state", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ReadPushMonitorWithResponse(ctx, arguments[0], arguments[1])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writePushMonitor(output, response.JSON200, flags.asJSON)
		},
	}
}

func newPushUpdate(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var file string
	command := &cobra.Command{
		Use: "update <project-key> <monitor-key>", Short: "Replace push monitor configuration from a JSON document", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			request, err := readMonitorInput[api.UpdatePushMonitorRequest](command, file)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.UpdatePushMonitorWithResponse(ctx, arguments[0], arguments[1], request)
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writePushMonitor(output, response.JSON200, flags.asJSON)
		},
	}
	command.Flags().StringVar(&file, "file", "", "JSON configuration file, or - for stdin")
	return command
}

func newPushDelete(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var version int64
	command := &cobra.Command{
		Use: "delete <project-key> <monitor-key>", Short: "Remove a push monitor while retaining its history", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			if version <= 0 {
				return process.New(process.Usage, "a positive --version is required")
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			value := strconv.FormatInt(version, 10)
			response, err := client.RemovePushMonitorWithResponse(ctx, arguments[0], arguments[1], &api.RemovePushMonitorParams{Version: &value})
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writePushMonitor(output, response.JSON200, flags.asJSON)
		},
	}
	command.Flags().Int64Var(&version, "version", 0, "monitor version last read")
	return command
}

func newPushStateChange(output io.Writer, getenv environment, flags *managementFlags, pause bool) *cobra.Command {
	verb := "resume"
	short := "Resume a push monitor with a fresh reporting window"
	if pause {
		verb, short = "pause", "Pause a push monitor"
	}
	var version int64
	command := &cobra.Command{
		Use: verb + " <project-key> <monitor-key>", Short: short, Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			if version <= 0 {
				return process.New(process.Usage, "a positive --version is required")
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			body := api.PushMonitorVersionRequest{Version: &version}
			var monitor *api.PushMonitorResponse
			var status int
			var responseBody []byte
			if pause {
				response, requestErr := client.PausePushMonitorWithResponse(ctx, arguments[0], arguments[1], body)
				if requestErr != nil {
					return unreachable(requestErr)
				}
				monitor, status, responseBody = response.JSON200, response.StatusCode(), response.Body
			} else {
				response, requestErr := client.ResumePushMonitorWithResponse(ctx, arguments[0], arguments[1], body)
				if requestErr != nil {
					return unreachable(requestErr)
				}
				monitor, status, responseBody = response.JSON200, response.StatusCode(), response.Body
			}
			if status != http.StatusOK || monitor == nil {
				return responseProblem(status, responseBody)
			}
			return writePushMonitor(output, monitor, flags.asJSON)
		},
	}
	command.Flags().Int64Var(&version, "version", 0, "monitor version last read")
	return command
}

func newPushReports(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var before int64
	var limit int32
	command := &cobra.Command{
		Use: "reports <project-key> <monitor-key>", Short: "List push reports newest first", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			params, err := pushReportHistoryParams(before, limit)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ListPushReportHistoryWithResponse(ctx, arguments[0], arguments[1], params)
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			if flags.asJSON {
				return writeJSON(output, response.JSON200)
			}
			for index := range response.JSON200.Items {
				if err := writePushReportText(output, &response.JSON200.Items[index]); err != nil {
					return err
				}
			}
			return nil
		},
	}
	command.Flags().Int64Var(&before, "before-sequence", 0, "exclusive sequence cursor")
	command.Flags().Int32Var(&limit, "limit", 0, "page size (1-100; API default when omitted)")
	return command
}

func newPushIncidents(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var before int64
	var limit int32
	command := &cobra.Command{
		Use: "incidents <project-key> <monitor-key>", Short: "List push incidents newest first", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			params, err := pushIncidentHistoryParams(before, limit)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ListPushIncidentHistoryWithResponse(ctx, arguments[0], arguments[1], params)
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			if flags.asJSON {
				return writeJSON(output, response.JSON200)
			}
			for index := range response.JSON200.Items {
				if err := writePushIncidentText(output, &response.JSON200.Items[index]); err != nil {
					return err
				}
			}
			return nil
		},
	}
	command.Flags().Int64Var(&before, "before-opening-sequence", 0, "exclusive incident opening-sequence cursor")
	command.Flags().Int32Var(&limit, "limit", 0, "page size (1-100; API default when omitted)")
	return command
}

func newPushCredential(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	command := &cobra.Command{Use: "credential", Short: "Manage the monitor-scoped reporting credential"}
	command.AddCommand(newPushCredentialIssue(output, getenv, flags))
	command.AddCommand(newPushCredentialGet(output, getenv, flags))
	command.AddCommand(newPushCredentialRotate(output, getenv, flags))
	command.AddCommand(newPushCredentialRevoke(output, getenv, flags))
	return command
}

func newPushCredentialIssue(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{Use: "issue <project-key> <monitor-key>", Short: "Issue and reveal a reporting credential once", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.IssueReportingCredentialWithResponse(ctx, arguments[0], arguments[1])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusCreated || response.JSON201 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeIssuedReportingCredential(output, response.JSON201, flags.asJSON)
		}}
}

func newPushCredentialGet(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{Use: "get <project-key> <monitor-key>", Short: "Read safe reporting credential metadata", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ReadReportingCredentialWithResponse(ctx, arguments[0], arguments[1])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeReportingCredential(output, response.JSON200, flags.asJSON)
		}}
}

func newPushCredentialRotate(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{Use: "rotate <project-key> <monitor-key>", Short: "Rotate and reveal a reporting credential once", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.RotateReportingCredentialWithResponse(ctx, arguments[0], arguments[1])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeIssuedReportingCredential(output, response.JSON200, flags.asJSON)
		}}
}

func newPushCredentialRevoke(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{Use: "revoke <project-key> <monitor-key>", Short: "Revoke the reporting credential immediately", Args: cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.RevokeReportingCredentialWithResponse(ctx, arguments[0], arguments[1])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusNoContent {
				return responseProblem(response.StatusCode(), response.Body)
			}
			result := struct {
				ProjectKey string `json:"project_key"`
				MonitorKey string `json:"monitor_key"`
				Revoked    bool   `json:"revoked"`
			}{arguments[0], arguments[1], true}
			if flags.asJSON {
				return writeJSON(output, result)
			}
			_, err = fmt.Fprintf(output, "%s/%s\trevoked\n", arguments[0], arguments[1])
			return err
		}}
}

func pushReportHistoryParams(before int64, limit int32) (*api.ListPushReportHistoryParams, error) {
	if before < 0 || limit < 0 || limit > 100 {
		return nil, process.New(process.Usage, "cursors must be positive and --limit must be between 1 and 100")
	}
	params := &api.ListPushReportHistoryParams{}
	if before > 0 {
		params.BeforeSequence = &before
	}
	if limit > 0 {
		params.Limit = &limit
	}
	return params, nil
}

func pushIncidentHistoryParams(before int64, limit int32) (*api.ListPushIncidentHistoryParams, error) {
	if before < 0 || limit < 0 || limit > 100 {
		return nil, process.New(process.Usage, "cursors must be positive and --limit must be between 1 and 100")
	}
	params := &api.ListPushIncidentHistoryParams{}
	if before > 0 {
		params.BeforeOpeningSequence = &before
	}
	if limit > 0 {
		params.Limit = &limit
	}
	return params, nil
}

func writePushMonitor(output io.Writer, monitor *api.PushMonitorResponse, asJSON bool) error {
	if asJSON {
		return writeJSON(output, monitor)
	}
	return writePushMonitorText(output, monitor)
}

func writePushMonitorText(output io.Writer, monitor *api.PushMonitorResponse) error {
	_, err := fmt.Fprintf(output, "%s/%s\t%s\t%s\t%d\t%s\t%s\tlast_received=%s\tlast_report=%s\tlast_success=%s\tdeadline=%s\tincident=%s\tcredential=%t\n",
		monitor.ProjectKey, monitor.Key, strconv.Quote(monitor.Name), monitor.Id, monitor.Version, monitor.Mode, monitor.State,
		timeValueOrDash(monitor.LastReceivedAt), uuidValueOrDash(monitor.LatestReportId), uuidValueOrDash(monitor.LatestSuccessId),
		timeValueOrDash(monitor.NextDeadlineAt), uuidValueOrDash(monitor.OpenIncidentId), monitor.HasReportingCredential)
	return err
}

func writeReportingCredential(output io.Writer, credential *api.ReportingCredentialResponse, asJSON bool) error {
	if asJSON {
		return writeJSON(output, credential)
	}
	_, err := fmt.Fprintf(output, "%s\tcreated=%s\trotated=%s\trevoked=%s\n", credential.Id,
		credential.CreatedAt.Format(time.RFC3339Nano), timeValueOrDash(credential.RotatedAt), timeValueOrDash(credential.RevokedAt))
	return err
}

func writeIssuedReportingCredential(output io.Writer, credential *api.IssuedReportingCredentialResponse, asJSON bool) error {
	if asJSON {
		return writeJSON(output, credential)
	}
	_, err := fmt.Fprintf(output, "%s\ttoken=%s\treport_url=%s\tprevious_valid_until=%s\n", credential.Id,
		credential.Token, credential.ReportUrl, timeValueOrDash(credential.PreviousValidUntil))
	return err
}

func writePushReportText(output io.Writer, report *api.PushReportHistoryResponse) error {
	_, err := fmt.Fprintf(output, "%d\t%s\treason=%s\tapplied=%t\tobserved=%s\treceived=%s\treport=%s\tid=%s\n",
		report.Sequence, report.Outcome, valueOrDash(report.Reason), report.Applicable,
		report.ObservedAt.Format(time.RFC3339Nano), report.ReceivedAt.Format(time.RFC3339Nano), report.ReportId, report.Id)
	return err
}

func writePushIncidentText(output io.Writer, incident *api.PushIncidentHistoryResponse) error {
	state := "open"
	if incident.ResolvedAt != nil {
		state = "resolved"
	}
	_, err := fmt.Fprintf(output, "%d\t%s\toriginal=%s\tlatest=%s\topened=%s\tresolved=%s\tid=%s\n",
		incident.OpeningSequence, state, incident.OriginalReason, incident.LatestReason,
		incident.OpenedAt.Format(time.RFC3339Nano), timeValueOrDash(incident.ResolvedAt), incident.Id)
	return err
}

func timeValueOrDash(value *time.Time) string {
	if value == nil {
		return "-"
	}
	return value.Format(time.RFC3339Nano)
}

func uuidValueOrDash[T fmt.Stringer](value *T) string {
	if value == nil {
		return "-"
	}
	return (*value).String()
}
