package cmd

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"strconv"
	"time"

	"github.com/datavisionzero/upaffe/src/cli/internal/api"
	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
	"github.com/spf13/cobra"
)

const maximumMonitorInputBytes = 1024 * 1024

type monitorView struct {
	api.HttpMonitorResponse
	LatestCheck *api.HttpCheckHistoryResponse `json:"latest_check,omitempty"`
}

func newMonitor(output io.Writer, getenv environment) *cobra.Command {
	flags := &managementFlags{}
	command := &cobra.Command{
		Use:   "monitor",
		Short: "Manage and inspect HTTP monitors by immutable key",
	}
	bindManagementFlags(command, flags)
	command.AddCommand(newMonitorCreate(output, getenv, flags))
	command.AddCommand(newMonitorList(output, getenv, flags))
	command.AddCommand(newMonitorGet(output, getenv, flags))
	command.AddCommand(newMonitorUpdate(output, getenv, flags))
	command.AddCommand(newMonitorDelete(output, getenv, flags))
	command.AddCommand(newMonitorPause(output, getenv, flags))
	command.AddCommand(newMonitorResume(output, getenv, flags))
	command.AddCommand(newMonitorTest(output, getenv, flags))
	command.AddCommand(newMonitorChecks(output, getenv, flags))
	command.AddCommand(newMonitorIncidents(output, getenv, flags))
	command.AddCommand(newMonitorHeader(output, getenv, flags))
	return command
}

func newMonitorCreate(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var file string
	command := &cobra.Command{
		Use:   "create <project-key>",
		Short: "Create an HTTP monitor from a JSON document",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			request, err := readDocumentInput[api.CreateHttpMonitorRequest](command, file)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.CreateHttpMonitorWithResponse(ctx, arguments[0], request)
			if err != nil {
				return unreachable(err)
			}
			var monitor *api.HttpMonitorResponse
			switch response.StatusCode() {
			case http.StatusOK:
				monitor = response.JSON200
			case http.StatusCreated:
				monitor = response.JSON201
			}
			if monitor == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeMonitor(output, monitor, nil, flags.asJSON)
		},
	}
	command.Flags().StringVar(&file, "file", "", "JSON configuration file, or - for stdin")
	return command
}

func newMonitorList(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use:   "list <project-key>",
		Short: "List live HTTP monitors and their current state",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ListHttpMonitorsWithResponse(ctx, arguments[0])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			views := make([]monitorView, 0, len(*response.JSON200))
			for index := range *response.JSON200 {
				monitor := &(*response.JSON200)[index]
				latest, err := latestFailedCheck(ctx, client, monitor)
				if err != nil {
					return err
				}
				views = append(views, monitorView{HttpMonitorResponse: *monitor, LatestCheck: latest})
			}
			if flags.asJSON {
				return writeJSON(output, views)
			}
			for index := range views {
				if err := writeMonitorText(output, &views[index].HttpMonitorResponse, views[index].LatestCheck); err != nil {
					return err
				}
			}
			return nil
		},
	}
}

func newMonitorGet(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use:   "get <project-key> <monitor-key>",
		Short: "Read one HTTP monitor and its current state",
		Args:  cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ReadHttpMonitorWithResponse(ctx, arguments[0], arguments[1])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			latest, err := latestFailedCheck(ctx, client, response.JSON200)
			if err != nil {
				return err
			}
			return writeMonitor(output, response.JSON200, latest, flags.asJSON)
		},
	}
}

func newMonitorUpdate(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var file string
	command := &cobra.Command{
		Use:   "update <project-key> <monitor-key>",
		Short: "Replace non-secret monitor configuration from a JSON document",
		Args:  cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			request, err := readDocumentInput[api.UpdateHttpMonitorRequest](command, file)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.UpdateHttpMonitorWithResponse(ctx, arguments[0], arguments[1], request)
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeMonitor(output, response.JSON200, nil, flags.asJSON)
		},
	}
	command.Flags().StringVar(&file, "file", "", "JSON configuration file, or - for stdin")
	return command
}

func newMonitorDelete(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return newMonitorVersionQueryCommand(
		"delete <project-key> <monitor-key>",
		"Remove an HTTP monitor while retaining its history",
		output,
		getenv,
		flags,
		func(client *api.ClientWithResponses, ctx context.Context, arguments []string, version string) (*api.HttpMonitorResponse, int, []byte, error) {
			response, err := client.RemoveHttpMonitorWithResponse(
				ctx, arguments[0], arguments[1], &api.RemoveHttpMonitorParams{Version: &version})
			if err != nil {
				return nil, 0, nil, err
			}
			return response.JSON200, response.StatusCode(), response.Body, nil
		})
}

type monitorVersionQueryCall func(
	*api.ClientWithResponses,
	context.Context,
	[]string,
	string,
) (*api.HttpMonitorResponse, int, []byte, error)

func newMonitorVersionQueryCommand(
	use string,
	short string,
	output io.Writer,
	getenv environment,
	flags *managementFlags,
	call monitorVersionQueryCall,
) *cobra.Command {
	var version int64
	command := &cobra.Command{
		Use:   use,
		Short: short,
		Args:  cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			if version <= 0 {
				return process.New(process.Usage, "a positive --version is required")
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			monitor, status, body, err := call(client, ctx, arguments, strconv.FormatInt(version, 10))
			if err != nil {
				return unreachable(err)
			}
			if status != http.StatusOK || monitor == nil {
				return responseProblem(status, body)
			}
			return writeMonitor(output, monitor, nil, flags.asJSON)
		},
	}
	command.Flags().Int64Var(&version, "version", 0, "monitor version last read")
	return command
}

func newMonitorPause(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return newMonitorVersionBodyCommand("pause <project-key> <monitor-key>", "Pause an HTTP monitor", output, getenv, flags, true)
}

func newMonitorResume(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return newMonitorVersionBodyCommand("resume <project-key> <monitor-key>", "Resume an HTTP monitor and make a fresh check due", output, getenv, flags, false)
}

func newMonitorVersionBodyCommand(
	use string,
	short string,
	output io.Writer,
	getenv environment,
	flags *managementFlags,
	pause bool,
) *cobra.Command {
	var version int64
	command := &cobra.Command{
		Use:   use,
		Short: short,
		Args:  cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			if version <= 0 {
				return process.New(process.Usage, "a positive --version is required")
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			var monitor *api.HttpMonitorResponse
			var status int
			var body []byte
			if pause {
				response, requestErr := client.PauseHttpMonitorWithResponse(
					ctx, arguments[0], arguments[1], api.HttpMonitorVersionRequest{Version: version})
				if requestErr != nil {
					return unreachable(requestErr)
				}
				monitor, status, body = response.JSON200, response.StatusCode(), response.Body
			} else {
				response, requestErr := client.ResumeHttpMonitorWithResponse(
					ctx, arguments[0], arguments[1], api.HttpMonitorVersionRequest{Version: version})
				if requestErr != nil {
					return unreachable(requestErr)
				}
				monitor, status, body = response.JSON200, response.StatusCode(), response.Body
			}
			if status != http.StatusOK || monitor == nil {
				return responseProblem(status, body)
			}
			return writeMonitor(output, monitor, nil, flags.asJSON)
		},
	}
	command.Flags().Int64Var(&version, "version", 0, "monitor version last read")
	return command
}

func newMonitorTest(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use:   "test <project-key> <monitor-key>",
		Short: "Run one immediate bounded HTTP check",
		Args:  cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClientWithTimeout(command, flags, getenv, 75*time.Second)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.TestHttpMonitorWithResponse(ctx, arguments[0], arguments[1])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			if flags.asJSON {
				err = writeJSON(output, response.JSON200)
			} else {
				err = writeMonitorTestText(output, response.JSON200)
			}
			if err != nil {
				return err
			}
			if !response.JSON200.Succeeded {
				reason := "check_failed"
				if response.JSON200.ReasonCode != nil {
					reason = *response.JSON200.ReasonCode
				}
				return process.New(process.CheckFailed, "HTTP check failed: %s", reason)
			}
			return nil
		},
	}
}

func newMonitorChecks(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var before int64
	var limit int32
	command := &cobra.Command{
		Use:   "checks <project-key> <monitor-key>",
		Short: "List completed HTTP checks newest first",
		Args:  cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			params, err := checkHistoryParams(before, limit)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ListHttpCheckHistoryWithResponse(ctx, arguments[0], arguments[1], params)
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
				if err := writeCheckText(output, &response.JSON200.Items[index]); err != nil {
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

func newMonitorIncidents(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var before int64
	var limit int32
	command := &cobra.Command{
		Use:   "incidents <project-key> <monitor-key>",
		Short: "List HTTP incidents newest first",
		Args:  cobra.ExactArgs(2),
		RunE: func(command *cobra.Command, arguments []string) error {
			params, err := incidentHistoryParams(before, limit)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ListIncidentHistoryWithResponse(ctx, arguments[0], arguments[1], params)
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
				if err := writeIncidentText(output, &response.JSON200.Items[index]); err != nil {
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

func newMonitorHeader(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	command := &cobra.Command{Use: "header", Short: "Set or remove write-only request headers"}
	command.AddCommand(newMonitorHeaderSet(output, getenv, flags))
	command.AddCommand(newMonitorHeaderRemove(output, getenv, flags))
	return command
}

func newMonitorHeaderSet(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var file string
	command := &cobra.Command{
		Use:   "set <project-key> <monitor-key> <header-name>",
		Short: "Set a write-only header from a JSON document",
		Args:  cobra.ExactArgs(3),
		RunE: func(command *cobra.Command, arguments []string) error {
			request, err := readDocumentInput[api.SetHttpMonitorHeaderRequest](command, file)
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.SetHttpMonitorHeaderWithResponse(
				ctx, arguments[0], arguments[1], arguments[2], request)
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeMonitor(output, response.JSON200, nil, flags.asJSON)
		},
	}
	command.Flags().StringVar(&file, "file", "", "JSON header value and version file, or - for stdin")
	return command
}

func newMonitorHeaderRemove(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var version int64
	command := &cobra.Command{
		Use:   "remove <project-key> <monitor-key> <header-name>",
		Short: "Remove one write-only request header",
		Args:  cobra.ExactArgs(3),
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
			response, err := client.RemoveHttpMonitorHeaderWithResponse(
				ctx,
				arguments[0],
				arguments[1],
				arguments[2],
				&api.RemoveHttpMonitorHeaderParams{Version: &value})
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeMonitor(output, response.JSON200, nil, flags.asJSON)
		},
	}
	command.Flags().Int64Var(&version, "version", 0, "monitor version last read")
	return command
}

func readDocumentInput[T any](command *cobra.Command, file string) (T, error) {
	var result T
	if file == "" {
		return result, process.New(process.Usage, "--file is required (use - for stdin)")
	}
	var reader io.Reader
	var closeFile func() error
	if file == "-" {
		reader = command.InOrStdin()
	} else {
		handle, err := os.Open(file)
		if err != nil {
			return result, process.New(process.Usage, "cannot open document input file: %v", err)
		}
		reader = handle
		closeFile = handle.Close
	}
	if closeFile != nil {
		defer closeFile()
	}
	content, err := io.ReadAll(io.LimitReader(reader, maximumMonitorInputBytes+1))
	if err != nil {
		return result, process.New(process.Usage, "cannot read document input")
	}
	if len(content) > maximumMonitorInputBytes {
		return result, process.New(process.Usage, "document input exceeds 1 MiB")
	}
	decoder := json.NewDecoder(bytes.NewReader(content))
	decoder.DisallowUnknownFields()
	var decoded *T
	if err := decoder.Decode(&decoded); err != nil || decoded == nil {
		return result, process.New(process.Usage, "document input is not a valid JSON document")
	}
	var trailing any
	if err := decoder.Decode(&trailing); err != io.EOF {
		return result, process.New(process.Usage, "document input must contain exactly one JSON document")
	}
	return *decoded, nil
}

func latestFailedCheck(
	ctx context.Context,
	client *api.ClientWithResponses,
	monitor *api.HttpMonitorResponse,
) (*api.HttpCheckHistoryResponse, error) {
	if monitor.ConsecutiveFailures == 0 || monitor.LatestResultId == nil {
		return nil, nil
	}
	limit := int32(100)
	var before *int64
	for {
		response, err := client.ListHttpCheckHistoryWithResponse(
			ctx,
			monitor.ProjectKey,
			monitor.Key,
			&api.ListHttpCheckHistoryParams{BeforeSequence: before, Limit: &limit})
		if err != nil {
			return nil, unreachable(err)
		}
		if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
			return nil, responseProblem(response.StatusCode(), response.Body)
		}
		for index := range response.JSON200.Items {
			item := &response.JSON200.Items[index]
			if item.Id == *monitor.LatestResultId {
				return item, nil
			}
		}
		next := response.JSON200.NextBeforeSequence
		if next == nil || (before != nil && *next >= *before) {
			return nil, nil
		}
		before = next
	}
}

func checkHistoryParams(before int64, limit int32) (*api.ListHttpCheckHistoryParams, error) {
	if before < 0 || limit < 0 || limit > 100 {
		return nil, process.New(process.Usage, "cursors must be positive and --limit must be between 1 and 100")
	}
	params := &api.ListHttpCheckHistoryParams{}
	if before > 0 {
		params.BeforeSequence = &before
	}
	if limit > 0 {
		params.Limit = &limit
	}
	return params, nil
}

func incidentHistoryParams(before int64, limit int32) (*api.ListIncidentHistoryParams, error) {
	if before < 0 || limit < 0 || limit > 100 {
		return nil, process.New(process.Usage, "cursors must be positive and --limit must be between 1 and 100")
	}
	params := &api.ListIncidentHistoryParams{}
	if before > 0 {
		params.BeforeOpeningSequence = &before
	}
	if limit > 0 {
		params.Limit = &limit
	}
	return params, nil
}

func writeMonitor(output io.Writer, monitor *api.HttpMonitorResponse, latest *api.HttpCheckHistoryResponse, asJSON bool) error {
	if asJSON {
		return writeJSON(output, monitorView{HttpMonitorResponse: *monitor, LatestCheck: latest})
	}
	return writeMonitorText(output, monitor, latest)
}

func writeMonitorText(output io.Writer, monitor *api.HttpMonitorResponse, latest *api.HttpCheckHistoryResponse) error {
	incident := "-"
	if monitor.OpenIncidentId != nil {
		incident = monitor.OpenIncidentId.String()
	}
	next := "-"
	if monitor.NextCheckAt != nil {
		next = monitor.NextCheckAt.Format(time.RFC3339Nano)
	}
	lastFailure := "-"
	if latest != nil {
		reason := valueOrDash(latest.FailureReason)
		status := int32ValueOrDash(latest.StatusCode)
		duration := int32ValueOrDash(latest.ResponseTimeMilliseconds)
		lastFailure = fmt.Sprintf("%s/status=%s/duration_ms=%s", reason, status, duration)
	}
	_, err := fmt.Fprintf(
		output,
		"%s/%s\t%s\t%s\t%d\t%s\tfailures=%d/%d\tincident=%s\tlast_failure=%s\tnext=%s\n",
		monitor.ProjectKey,
		monitor.Key,
		strconv.Quote(monitor.Name),
		monitor.Id,
		monitor.Version,
		monitor.State,
		monitor.ConsecutiveFailures,
		monitor.FailureThreshold,
		incident,
		lastFailure,
		next)
	return err
}

func writeMonitorTestText(output io.Writer, result *api.HttpMonitorTestResponse) error {
	_, err := fmt.Fprintf(
		output,
		"%t\tstatus=%s\tduration_ms=%d\treason=%s\tapplied=%t\tcheck=%s\n",
		result.Succeeded,
		int32ValueOrDash(result.StatusCode),
		result.ResponseTimeMilliseconds,
		valueOrDash(result.ReasonCode),
		result.AppliedToCurrentState,
		result.CheckId)
	return err
}

func writeCheckText(output io.Writer, check *api.HttpCheckHistoryResponse) error {
	_, err := fmt.Fprintf(
		output,
		"%d\t%s\treason=%s\tstatus=%s\tduration_ms=%s\t%s\t%s\t%s\n",
		check.Sequence,
		check.Outcome,
		valueOrDash(check.FailureReason),
		int32ValueOrDash(check.StatusCode),
		int32ValueOrDash(check.ResponseTimeMilliseconds),
		check.Trigger,
		check.CompletedAt.Format(time.RFC3339Nano),
		check.Id)
	return err
}

func writeIncidentText(output io.Writer, incident *api.IncidentHistoryResponse) error {
	state := "open"
	resolvedAt := "-"
	if incident.ResolvedAt != nil {
		state = "resolved"
		resolvedAt = incident.ResolvedAt.Format(time.RFC3339Nano)
	}
	_, err := fmt.Fprintf(
		output,
		"%d\t%s\toriginal=%s\tlatest=%s\t%s\t%s\t%s\n",
		incident.OpeningSequence,
		state,
		incident.OriginalReason,
		incident.LatestReason,
		incident.OpenedAt.Format(time.RFC3339Nano),
		resolvedAt,
		incident.Id)
	return err
}

func valueOrDash(value *string) string {
	if value == nil || *value == "" {
		return "-"
	}
	return *value
}

func int32ValueOrDash(value *int32) string {
	if value == nil {
		return "-"
	}
	return strconv.FormatInt(int64(*value), 10)
}
