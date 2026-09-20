package cmd

import (
	"fmt"
	"io"
	"net/http"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/datavisionzero/upaffe/src/cli/internal/api"
	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
	"github.com/spf13/cobra"
)

func newProject(output io.Writer, getenv environment) *cobra.Command {
	flags := &managementFlags{}
	command := &cobra.Command{
		Use:   "project",
		Short: "Manage projects by immutable key",
	}
	bindManagementFlags(command, flags)
	command.AddCommand(newProjectCreate(output, getenv, flags))
	command.AddCommand(newProjectGet(output, getenv, flags))
	command.AddCommand(newProjectReport(output, getenv, flags))
	command.AddCommand(newProjectList(output, getenv, flags))
	command.AddCommand(newProjectRename(output, getenv, flags))
	command.AddCommand(newProjectDelete(output, getenv, flags))
	command.AddCommand(newProjectRestore(output, getenv, flags))
	return command
}

func newProjectCreate(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var key string
	var name string
	command := &cobra.Command{
		Use:   "create",
		Short: "Create a project idempotently",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			if key == "" || name == "" {
				return process.New(process.Usage, "--key and --name are required")
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.CreateProjectWithResponse(ctx, api.CreateProjectRequest{
				Key:  &key,
				Name: &name,
			})
			if err != nil {
				return unreachable(err)
			}
			var project *api.ProjectResponse
			switch response.StatusCode() {
			case http.StatusOK:
				project = response.JSON200
			case http.StatusCreated:
				project = response.JSON201
			}
			if project == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeProject(output, project, flags.asJSON)
		},
	}
	command.Flags().StringVar(&key, "key", "", "immutable project key")
	command.Flags().StringVar(&name, "name", "", "project display name")
	return command
}

func newProjectGet(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use:   "get <key>",
		Short: "Read a live or deleted project",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ReadProjectWithResponse(ctx, arguments[0])
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeProject(output, response.JSON200, flags.asJSON)
		},
	}
}

func newProjectReport(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use:   "report <key>",
		Short: "Read one safe project investigation report",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ReadProjectReportWithResponse(ctx, arguments[0])
			if err != nil {
				return responseOrTransportError(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			report := response.JSON200
			if report.Project.Key == "" || report.GeneratedAt.IsZero() ||
				report.Counts.Total != report.Counts.Http+report.Counts.Push ||
				int(report.Counts.Total) != len(report.Attention)+len(report.Healthy) {
				return process.New(process.Unexpected, "unexpected_response")
			}
			if flags.asJSON {
				return writeJSON(output, report)
			}
			return writeProjectReportText(output, report)
		},
	}
}

func writeProjectReportText(output io.Writer, report *api.ProjectReport) error {
	var body strings.Builder
	fmt.Fprintf(&body, "project\tkey=%s\tname=%s\tat=%s\n",
		strconv.Quote(report.Project.Key), strconv.Quote(report.Project.Name),
		report.GeneratedAt.Format(time.RFC3339Nano))
	fmt.Fprintf(&body, "counts\ttotal=%d\thealthy=%d\tfailing=%d\tuntested=%d\tpaused=%d\n",
		report.Counts.Total, report.Counts.Healthy, report.Counts.Failing,
		report.Counts.Untested, report.Counts.Paused)
	if report.ProjectMaintenance != nil {
		fmt.Fprintf(&body, "project_maintenance\tuntil=%s\n",
			report.ProjectMaintenance.EndsAt.Format(time.RFC3339Nano))
	}
	fmt.Fprintf(&body, "email\tconfigured=%t\thost=%s\tport=%s\tsecurity=%s\tsender=%s\tpublic_base_url=%s\thas_password=%t\trecipients=%d\tpending=%d\tretrying=%d\tterminal_failure=%d\taccepted_by_smtp=%d\n",
		report.Email.Configured, quoteOptional(report.Email.Host),
		formatInt(report.Email.Port), quoteOptional(report.Email.Security),
		quoteOptional(report.Email.SenderAddress), quoteOptional(report.Email.PublicBaseUrl),
		report.Email.HasPassword, len(report.Email.Recipients),
		report.Email.Delivery.PendingCount, report.Email.Delivery.RetryingCount,
		report.Email.Delivery.TerminalFailureCount,
		report.Email.Delivery.SmtpAcceptedCount)
	for _, recipient := range report.Email.Recipients {
		fmt.Fprintf(&body, "recipient\taddress=%s\n", strconv.Quote(recipient))
	}
	attention := append([]api.ReportMonitor(nil), report.Attention...)
	sort.SliceStable(attention, func(left, right int) bool {
		return reportTextPriority(attention[left]) < reportTextPriority(attention[right])
	})
	for _, monitor := range attention {
		fmt.Fprintf(&body, "monitor\ttype=%s\tkey=%s\tname=%s\tpurpose=%s\tstate=%s\tmode=%s\toverdue=%t\tnext_due=%s\tlast_received=%s\tlast_success=%s\n",
			strconv.Quote(monitor.Type), strconv.Quote(monitor.Key),
			strconv.Quote(monitor.Name), quoteOptional(monitor.Purpose), strconv.Quote(monitor.State),
			quoteOptional(monitor.Mode),
			monitor.Overdue, formatTime(monitor.NextDueAt),
			formatTime(monitor.LastReceivedAt), formatResult(monitor.LastSuccess))
		fmt.Fprintf(&body, "settings\tkey=%s\ttarget=%s\texpected_status=%s\ttext_condition=%s\tinterval_seconds=%d\ttimeout_seconds=%s\tfailure_threshold=%s\tfailure_count=%s\ttolerance_seconds=%s\n",
			strconv.Quote(monitor.Key), quoteOptional(monitor.TargetUrl),
			formatInt(monitor.ExpectedStatusCode), quoteOptional(monitor.TextCondition),
			monitor.IntervalSeconds,
			formatInt(monitor.TimeoutSeconds), formatInt(monitor.FailureThreshold),
			formatInt(monitor.FailureCount), formatInt(monitor.ToleranceSeconds))
		if monitor.LatestResult != nil {
			fmt.Fprintf(&body, "diagnostic\tkey=%s\tresult=%s\n",
				strconv.Quote(monitor.Key), formatResult(monitor.LatestResult))
		}
		if monitor.Incident != nil {
			fmt.Fprintf(&body, "incident\tkey=%s\tid=%s\tage_seconds=%d\topened=%s\tcause=%s\tlatest_reason=%s\n",
				strconv.Quote(monitor.Key), monitor.Incident.Id,
				monitor.Incident.AgeSeconds,
				monitor.Incident.OpenedAt.Format(time.RFC3339Nano),
				strconv.Quote(monitor.Incident.OriginalReason),
				strconv.Quote(monitor.Incident.LatestReason))
		}
		if monitor.EffectiveMaintenanceUntil != nil {
			fmt.Fprintf(&body, "maintenance\tkey=%s\tdirect=%t\tuntil=%s\n",
				strconv.Quote(monitor.Key), monitor.DirectMaintenance != nil,
				formatTime(monitor.EffectiveMaintenanceUntil))
		}
		if monitor.Instruction != nil || monitor.RunbookUrl != nil {
			fmt.Fprintf(&body, "operator_guidance\tkey=%s\tinstruction=%s\trunbook_url=%s\n",
				strconv.Quote(monitor.Key), quoteOptional(monitor.Instruction),
				quoteOptional(monitor.RunbookUrl))
		}
	}
	for _, monitor := range report.Healthy {
		fmt.Fprintf(&body, "healthy\ttype=%s\tkey=%s\tname=%s\tpurpose=%s\tlast_success=%s\tnext_due=%s\tdirect_maintenance=%t\teffective_maintenance_until=%s\n",
			strconv.Quote(monitor.Type), strconv.Quote(monitor.Key),
			strconv.Quote(monitor.Name), quoteOptional(monitor.Purpose), formatTime(monitor.LastSuccessAt),
			formatTime(monitor.NextDueAt), monitor.DirectMaintenance != nil,
			formatTime(monitor.EffectiveMaintenanceUntil))
	}
	_, err := io.WriteString(output, body.String())
	return err
}

func formatResult(result *api.ReportResult) string {
	if result == nil {
		return "-"
	}
	reason := "-"
	if result.Reason != nil {
		reason = strconv.Quote(*result.Reason)
	}
	return fmt.Sprintf("%s,%s,%s,%s", result.Id,
		strconv.Quote(result.Outcome), reason,
		result.ObservedAt.Format(time.RFC3339Nano))
}

func formatTime(value *time.Time) string {
	if value == nil {
		return "-"
	}
	return value.Format(time.RFC3339Nano)
}

func quoteOptional(value *string) string {
	if value == nil {
		return "-"
	}
	return strconv.Quote(*value)
}

func formatInt(value *int32) string {
	if value == nil {
		return "-"
	}
	return strconv.FormatInt(int64(*value), 10)
}

func reportTextPriority(monitor api.ReportMonitor) int {
	if monitor.State == "failing" {
		return 0
	}
	if monitor.Overdue {
		return 1
	}
	if monitor.State == "untested" {
		return 2
	}
	if monitor.State == "paused" {
		return 3
	}
	return 4
}

func newProjectList(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var deleted bool
	command := &cobra.Command{
		Use:   "list",
		Short: "List live projects or deleted projects",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ListProjectsWithResponse(
				ctx,
				&api.ListProjectsParams{Deleted: &deleted})
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
				if err := writeProjectText(output, &(*response.JSON200)[index]); err != nil {
					return err
				}
			}
			return nil
		},
	}
	command.Flags().BoolVar(&deleted, "deleted", false, "list deleted projects instead of live projects")
	return command
}

func newProjectRename(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var name string
	var version int64
	command := &cobra.Command{
		Use:   "rename <key>",
		Short: "Rename a project at the version last read",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			if name == "" || version <= 0 {
				return process.New(process.Usage, "--name and a positive --version are required")
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.RenameProjectWithResponse(
				ctx,
				arguments[0],
				api.RenameProjectRequest{Name: &name, Version: version})
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeProject(output, response.JSON200, flags.asJSON)
		},
	}
	command.Flags().StringVar(&name, "name", "", "new project display name")
	command.Flags().Int64Var(&version, "version", 0, "project version last read")
	return command
}

func newProjectDelete(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var version int64
	command := &cobra.Command{
		Use:   "delete <key>",
		Short: "Soft-delete a project at the version last read",
		Args:  cobra.ExactArgs(1),
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
			response, err := client.DeleteProjectWithResponse(
				ctx,
				arguments[0],
				&api.DeleteProjectParams{Version: &value})
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeProject(output, response.JSON200, flags.asJSON)
		},
	}
	command.Flags().Int64Var(&version, "version", 0, "project version last read")
	return command
}

func newProjectRestore(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var version int64
	command := &cobra.Command{
		Use:   "restore <key>",
		Short: "Restore a project at the version last read",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			if version <= 0 {
				return process.New(process.Usage, "a positive --version is required")
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.RestoreProjectWithResponse(
				ctx,
				arguments[0],
				api.ProjectVersionRequest{Version: version})
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			return writeProject(output, response.JSON200, flags.asJSON)
		},
	}
	command.Flags().Int64Var(&version, "version", 0, "project version last read")
	return command
}

func writeProject(output io.Writer, project *api.ProjectResponse, asJSON bool) error {
	if asJSON {
		return writeJSON(output, project)
	}
	return writeProjectText(output, project)
}

func writeProjectText(output io.Writer, project *api.ProjectResponse) error {
	state := "live"
	deletedAt := "-"
	if project.DeletedAt != nil {
		state = "deleted"
		deletedAt = project.DeletedAt.Format(time.RFC3339Nano)
	}
	_, err := fmt.Fprintf(
		output,
		"%s\t%s\t%s\t%d\t%s\t%s\t%s\t%s\n",
		project.Key,
		strconv.Quote(project.Name),
		project.Id,
		project.Version,
		state,
		project.CreatedAt.Format(time.RFC3339Nano),
		project.UpdatedAt.Format(time.RFC3339Nano),
		deletedAt)
	return err
}

func unreachable(err error) error {
	return responseOrTransportError(err)
}
