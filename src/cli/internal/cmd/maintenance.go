package cmd

import (
	"fmt"
	"io"
	"net/http"
	"strconv"
	"strings"

	"github.com/datavisionzero/upaffe/src/cli/internal/api"
	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
	"github.com/spf13/cobra"
)

func newMaintenance(output io.Writer, getenv environment) *cobra.Command {
	flags := &managementFlags{}
	command := &cobra.Command{Use: "maintenance", Short: "Manage timed project or monitor maintenance"}
	bindManagementFlags(command, flags)
	command.AddCommand(newMaintenanceAction(output, getenv, flags, "get"))
	command.AddCommand(newMaintenanceAction(output, getenv, flags, "start"))
	command.AddCommand(newMaintenanceAction(output, getenv, flags, "end"))
	return command
}

func newMaintenanceAction(output io.Writer, getenv environment, flags *managementFlags, action string) *cobra.Command {
	var scope string
	var version int64
	var duration int32
	command := &cobra.Command{Use: action + " <project-key> [monitor-key]", Args: cobra.RangeArgs(1, 2), RunE: func(command *cobra.Command, arguments []string) error {
		if scope != "project" && scope != "http" && scope != "push" {
			return process.New(process.Usage, "--scope must be project, http, or push")
		}
		if scope == "project" && len(arguments) != 1 || scope != "project" && len(arguments) != 2 {
			return process.New(process.Usage, "project scope needs one project key; monitor scope also needs a monitor key")
		}
		if action == "start" && (version < 0 || duration < 60 || duration > 2592000) {
			return process.New(process.Usage, "--version must be nonnegative and --duration-seconds must be 60-2592000")
		}
		if action == "end" && version <= 0 {
			return process.New(process.Usage, "a positive --version is required")
		}
		client, ctx, cancel, err := managementClient(command, flags, getenv)
		if err != nil {
			return err
		}
		defer cancel()
		var status int
		var body []byte
		var value *api.MaintenanceSnapshot
		project := arguments[0]
		monitor := ""
		if len(arguments) == 2 {
			monitor = arguments[1]
		}
		switch {
		case action == "get" && scope == "project":
			response, requestErr := client.ReadProjectMaintenanceWithResponse(ctx, project)
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		case action == "get" && scope == "http":
			response, requestErr := client.ReadHttpMonitorMaintenanceWithResponse(ctx, project, monitor)
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		case action == "get" && scope == "push":
			response, requestErr := client.ReadPushMonitorMaintenanceWithResponse(ctx, project, monitor)
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		case action == "start" && scope == "project":
			response, requestErr := client.StartProjectMaintenanceWithResponse(ctx, project, api.StartMaintenanceRequest{DurationSeconds: duration, Version: version})
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		case action == "start" && scope == "http":
			response, requestErr := client.StartHttpMonitorMaintenanceWithResponse(ctx, project, monitor, api.StartMaintenanceRequest{DurationSeconds: duration, Version: version})
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		case action == "start" && scope == "push":
			response, requestErr := client.StartPushMonitorMaintenanceWithResponse(ctx, project, monitor, api.StartMaintenanceRequest{DurationSeconds: duration, Version: version})
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		case action == "end" && scope == "project":
			text := strconv.FormatInt(version, 10)
			response, requestErr := client.EndProjectMaintenanceWithResponse(ctx, project, &api.EndProjectMaintenanceParams{Version: &text})
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		case action == "end" && scope == "http":
			text := strconv.FormatInt(version, 10)
			response, requestErr := client.EndHttpMonitorMaintenanceWithResponse(ctx, project, monitor, &api.EndHttpMonitorMaintenanceParams{Version: &text})
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		case action == "end" && scope == "push":
			text := strconv.FormatInt(version, 10)
			response, requestErr := client.EndPushMonitorMaintenanceWithResponse(ctx, project, monitor, &api.EndPushMonitorMaintenanceParams{Version: &text})
			if requestErr != nil {
				return unreachable(requestErr)
			}
			status, body, value = response.StatusCode(), response.Body, response.JSON200
		}
		if status != http.StatusOK || value == nil {
			return responseProblem(status, body)
		}
		return writeMaintenance(output, value, flags.asJSON)
	}}
	command.Flags().StringVar(&scope, "scope", "", "project, http, or push (required)")
	if action != "get" {
		command.Flags().Int64Var(&version, "version", -1, "maintenance scope version last read")
	}
	if action == "start" {
		command.Flags().Int32Var(&duration, "duration-seconds", 0, "finite duration (60-2592000 seconds)")
	}
	return command
}

func writeMaintenance(output io.Writer, value *api.MaintenanceSnapshot, asJSON bool) error {
	if asJSON {
		return writeJSON(output, value)
	}
	monitor := valueOrDash(value.MonitorKey)
	_, err := fmt.Fprintf(output, "%s/%s/%s\tversion=%d\tdirect=%t\teffective=%t\tscopes=%s\tends=%s\teffective_ends=%s\n", value.ProjectKey, value.ScopeType, monitor, value.Version, value.DirectActive, value.EffectiveActive, strings.Join(value.ActiveScopes, ","), timeValueOrDash(value.EndsAt), timeValueOrDash(value.EffectiveEndsAt))
	return err
}
