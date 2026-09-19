package cmd

import (
	"fmt"
	"io"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/datavisionzero/upaffe/src/cli/internal/api"
	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
	"github.com/google/uuid"
	"github.com/spf13/cobra"
)

func newEmail(output io.Writer, getenv environment) *cobra.Command {
	flags := &managementFlags{}
	command := &cobra.Command{Use: "email", Short: "Configure SMTP and inspect email delivery"}
	bindManagementFlags(command, flags)
	command.AddCommand(newEmailSettings(output, getenv, flags))
	command.AddCommand(newEmailPassword(output, getenv, flags))
	command.AddCommand(newEmailRecipients(output, getenv, flags, false))
	command.AddCommand(newEmailRecipients(output, getenv, flags, true))
	command.AddCommand(newEmailTest(output, getenv, flags))
	command.AddCommand(newEmailDeliveries(output, getenv, flags))
	command.AddCommand(newEmailSummary(output, getenv, flags))
	command.AddCommand(newEmailIncident(output, getenv, flags))
	return command
}

func newEmailSettings(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	group := &cobra.Command{Use: "settings", Short: "Read or update safe SMTP settings"}
	group.AddCommand(&cobra.Command{Use: "get", Args: cobra.NoArgs, RunE: func(command *cobra.Command, _ []string) error {
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		response, err := client.ReadEmailSettingsWithResponse(ctx)
		if err != nil {
			return unreachable(err)
		}
		return writeEmailSettingsResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
	}})
	var file string
	update := &cobra.Command{Use: "set --file PATH|-", Args: cobra.NoArgs, RunE: func(command *cobra.Command, _ []string) error {
		request, err := readDocumentInput[api.UpdateEmailSettingsRequest](command, file)
		if err != nil {
			return err
		}
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		response, err := client.UpdateEmailSettingsWithResponse(ctx, request)
		if err != nil {
			return unreachable(err)
		}
		return writeEmailSettingsResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
	}}
	update.Flags().StringVar(&file, "file", "", "JSON settings document, or - for stdin")
	group.AddCommand(update)
	return group
}

func newEmailPassword(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	group := &cobra.Command{Use: "password", Short: "Replace or clear the write-only SMTP password"}
	var file string
	set := &cobra.Command{Use: "set --file PATH|-", Args: cobra.NoArgs, RunE: func(command *cobra.Command, _ []string) error {
		request, err := readDocumentInput[api.SetSmtpPasswordRequest](command, file)
		if err != nil {
			return err
		}
		if request.Password == nil || *request.Password == "" {
			return process.New(process.Usage, "password document requires a password")
		}
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		response, err := client.ReplaceSmtpPasswordWithResponse(ctx, request)
		if err != nil {
			return unreachable(err)
		}
		return writeEmailSettingsResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
	}}
	set.Flags().StringVar(&file, "file", "", "JSON password and version document, or - for stdin")
	group.AddCommand(set)
	var version int64
	clear := &cobra.Command{Use: "clear", Args: cobra.NoArgs, RunE: func(command *cobra.Command, _ []string) error {
		if version <= 0 {
			return process.New(process.Usage, "a positive --version is required")
		}
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		value := strconv.FormatInt(version, 10)
		response, err := client.ClearSmtpPasswordWithResponse(ctx, &api.ClearSmtpPasswordParams{Version: &value})
		if err != nil {
			return unreachable(err)
		}
		return writeEmailSettingsResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
	}}
	clear.Flags().Int64Var(&version, "version", 0, "email settings version last read")
	group.AddCommand(clear)
	return group
}

func newEmailRecipients(output io.Writer, getenv environment, flags *managementFlags, project bool) *cobra.Command {
	name := "defaults"
	short := "Replace default recipients for future projects"
	args := cobra.NoArgs
	if project {
		name, short, args = "recipients", "Read or replace one project's recipients", cobra.ExactArgs(1)
	}
	group := &cobra.Command{Use: name, Short: short}
	group.AddCommand(&cobra.Command{Use: "get [project-key]", Args: args, RunE: func(command *cobra.Command, arguments []string) error {
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		if project {
			response, err := client.ReadProjectRecipientsWithResponse(ctx, arguments[0])
			if err != nil {
				return unreachable(err)
			}
			return writeProjectRecipientsResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
		}
		response, err := client.ReadEmailSettingsWithResponse(ctx)
		if err != nil {
			return unreachable(err)
		}
		return writeEmailSettingsResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
	}})
	var file string
	set := &cobra.Command{Use: "set [project-key] --file PATH|-", Args: args, RunE: func(command *cobra.Command, arguments []string) error {
		request, err := readDocumentInput[api.ReplaceRecipientsRequest](command, file)
		if err != nil {
			return err
		}
		if request.Recipients == nil {
			return process.New(process.Usage, "recipient document requires recipients")
		}
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		if project {
			response, err := client.ReplaceProjectRecipientsWithResponse(ctx, arguments[0], request)
			if err != nil {
				return unreachable(err)
			}
			return writeProjectRecipientsResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
		}
		response, err := client.ReplaceDefaultRecipientsWithResponse(ctx, request)
		if err != nil {
			return unreachable(err)
		}
		return writeEmailSettingsResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
	}}
	set.Flags().StringVar(&file, "file", "", "JSON recipients and version document, or - for stdin")
	group.AddCommand(set)
	return group
}

func newEmailTest(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var recipient string
	command := &cobra.Command{Use: "test", Short: "Send one explicit SMTP test message", Args: cobra.NoArgs, RunE: func(command *cobra.Command, _ []string) error {
		if recipient == "" {
			return process.New(process.Usage, "--recipient is required")
		}
		client, ctx, cancel, err := managementClientWithTimeout(command, flags, getenv, 45*time.Second)
		if err != nil {
			return err
		}
		defer cancel()
		response, err := client.SendTestEmailWithResponse(ctx, api.TestEmailRequest{Recipient: &recipient})
		if err != nil {
			return unreachable(err)
		}
		if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
			return responseProblem(response.StatusCode(), response.Body)
		}
		if flags.asJSON {
			return writeJSON(output, response.JSON200)
		}
		_, err = fmt.Fprintf(output, "%s\t%s\n", response.JSON200.Status, response.JSON200.AcceptedAt.Format("2006-01-02T15:04:05Z07:00"))
		return err
	}}
	command.Flags().StringVar(&recipient, "recipient", "", "test recipient address")
	return command
}

func newEmailDeliveries(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var project, monitorType, monitorKey, incident, state string
	var limit, offset int32
	command := &cobra.Command{Use: "deliveries", Short: "Page per-recipient delivery history", Args: cobra.NoArgs, RunE: func(command *cobra.Command, _ []string) error {
		if limit < 0 || limit > 100 || offset < 0 || offset > 10000 {
			return process.New(process.Usage, "--limit must be 1-100 and --offset 0-10000")
		}
		params := &api.ListEmailDeliveriesParams{}
		if project != "" {
			params.ProjectKey = &project
		}
		if monitorType != "" {
			params.MonitorType = &monitorType
		}
		if monitorKey != "" {
			params.MonitorKey = &monitorKey
		}
		if state != "" {
			params.State = &state
		}
		if limit > 0 {
			params.Limit = &limit
		}
		if offset > 0 {
			params.Offset = &offset
		}
		if incident != "" {
			id, err := uuid.Parse(incident)
			if err != nil {
				return process.New(process.Usage, "--incident must be a UUID")
			}
			params.IncidentId = &id
		}
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		response, err := client.ListEmailDeliveriesWithResponse(ctx, params)
		if err != nil {
			return unreachable(err)
		}
		if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
			return responseProblem(response.StatusCode(), response.Body)
		}
		if flags.asJSON {
			return writeJSON(output, response.JSON200)
		}
		for _, row := range response.JSON200.Items {
			if err := writeDeliveryText(output, row); err != nil {
				return err
			}
		}
		return nil
	}}
	command.Flags().StringVar(&project, "project", "", "project key filter")
	command.Flags().StringVar(&monitorType, "monitor-type", "", "http or push")
	command.Flags().StringVar(&monitorKey, "monitor", "", "monitor key filter")
	command.Flags().StringVar(&incident, "incident", "", "incident UUID filter")
	command.Flags().StringVar(&state, "state", "", "stored delivery state filter")
	command.Flags().Int32Var(&limit, "limit", 0, "page size (1-100; API default when omitted)")
	command.Flags().Int32Var(&offset, "offset", 0, "page offset (0-10000)")
	return command
}

func newEmailSummary(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var project string
	command := &cobra.Command{Use: "summary", Short: "Read instance or project delivery counts", Args: cobra.NoArgs, RunE: func(command *cobra.Command, _ []string) error {
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		if project != "" {
			response, err := client.ReadProjectEmailSummaryWithResponse(ctx, project)
			if err != nil {
				return unreachable(err)
			}
			return writeEmailSummaryResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
		}
		response, err := client.ReadEmailDeliverySummaryWithResponse(ctx)
		if err != nil {
			return unreachable(err)
		}
		return writeEmailSummaryResult(output, flags.asJSON, response.StatusCode(), response.Body, response.JSON200)
	}}
	command.Flags().StringVar(&project, "project", "", "project key (otherwise instance summary)")
	return command
}

func newEmailIncident(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var monitorType string
	command := &cobra.Command{Use: "incident <uuid>", Short: "Read announcement and delivery status for one incident", Args: cobra.ExactArgs(1), RunE: func(command *cobra.Command, arguments []string) error {
		id, err := uuid.Parse(arguments[0])
		if err != nil {
			return process.New(process.Usage, "incident must be a UUID")
		}
		if monitorType != "http" && monitorType != "push" {
			return process.New(process.Usage, "--monitor-type must be http or push")
		}
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		response, err := client.ReadIncidentEmailStatusWithResponse(ctx, id, &api.ReadIncidentEmailStatusParams{MonitorType: &monitorType})
		if err != nil {
			return unreachable(err)
		}
		if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
			return responseProblem(response.StatusCode(), response.Body)
		}
		if flags.asJSON {
			return writeJSON(output, response.JSON200)
		}
		value := response.JSON200
		if _, err := fmt.Fprintf(output, "%s/%s/%s\t%s\topen=%t\tsuppression=%s\n", value.ProjectKey, value.MonitorType, value.MonitorKey, value.AnnouncementState, value.Open, valueOrDash(value.SuppressionReason)); err != nil {
			return err
		}
		for _, row := range value.Deliveries {
			if err := writeDeliveryText(output, row); err != nil {
				return err
			}
		}
		return nil
	}}
	command.Flags().StringVar(&monitorType, "monitor-type", "", "http or push (required)")
	return command
}

func writeEmailSettingsResult(output io.Writer, asJSON bool, status int, body []byte, value *api.EmailConfigurationSnapshot) error {
	if status != http.StatusOK || value == nil {
		return responseProblem(status, body)
	}
	if asJSON {
		return writeJSON(output, value)
	}
	_, err := fmt.Fprintf(output, "version=%d\thost=%s\tport=%s\tsecurity=%s\tsender=%s\tname=%s\tbase_url=%s\tusername=%s\tpassword_set=%t\tdefaults=%s\n", value.Version, valueOrDash(value.Host), int32OrDash(value.Port), value.Security, valueOrDash(value.SenderAddress), valueOrDash(value.SenderName), valueOrDash(value.PublicBaseUrl), valueOrDash(value.Username), value.HasPassword, strings.Join(value.DefaultRecipients, ","))
	return err
}

func writeProjectRecipientsResult(output io.Writer, asJSON bool, status int, body []byte, value *api.ProjectRecipientsResponse) error {
	if status != http.StatusOK || value == nil {
		return responseProblem(status, body)
	}
	if asJSON {
		return writeJSON(output, value)
	}
	_, err := fmt.Fprintf(output, "%s\tversion=%d\trecipients=%s\n", value.ProjectKey, value.Version, strings.Join(value.Recipients, ","))
	return err
}

func writeEmailSummaryResult(output io.Writer, asJSON bool, status int, body []byte, value *api.EmailDeliverySummary) error {
	if status != http.StatusOK || value == nil {
		return responseProblem(status, body)
	}
	if asJSON {
		return writeJSON(output, value)
	}
	scope := "instance"
	if value.ProjectKey != nil {
		scope = *value.ProjectKey
	}
	_, err := fmt.Fprintf(output, "%s\tpending=%d\tretrying=%d\tfailed=%d\tsmtp_accepted=%d\toldest_pending=%s\n", scope, value.PendingCount, value.RetryingCount, value.TerminalFailureCount, value.SmtpAcceptedCount, timeValueOrDash(value.OldestPendingAt))
	return err
}

func writeDeliveryText(output io.Writer, value api.EmailDeliveryStatus) error {
	_, err := fmt.Fprintf(output, "%s\t%s/%s/%s\t%s\t%s\t%s\tattempts=%d\terror=%s\tsuppression=%s\tlast=%s\tnext=%s\taccepted=%s\tterminal=%s\n", value.IncidentId, value.ProjectKey, value.MonitorType, value.MonitorKey, value.Kind, value.Recipient, value.State, value.AttemptCount, valueOrDash(value.LastErrorCode), valueOrDash(value.SuppressionReason), timeValueOrDash(value.LastAttemptAt), timeValueOrDash(value.NextAttemptAt), timeValueOrDash(value.AcceptedAt), timeValueOrDash(value.TerminalAt))
	return err
}

func int32OrDash(value *int32) string {
	if value == nil {
		return "-"
	}
	return strconv.FormatInt(int64(*value), 10)
}
