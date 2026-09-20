package cmd

import (
	"fmt"
	"io"
	"net/http"
	"strconv"

	"github.com/datavisionzero/upaffe/src/cli/internal/api"
	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
	"github.com/spf13/cobra"
)

func newInventory(output io.Writer, getenv environment) *cobra.Command {
	flags := &managementFlags{}
	var project, state, monitorType, search string
	var limit, offset int32
	command := &cobra.Command{
		Use: "inventory", Short: "Search HTTP and push monitors across projects", Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			if limit < 1 || limit > 100 || offset < 0 || offset > 1000000 {
				return process.New(process.Usage, "limit must be 1-100 and offset must be 0-1000000")
			}
			params := &api.ReadMonitorInventoryParams{Limit: &limit, Offset: &offset}
			if project != "" {
				params.Project = &project
			}
			if state != "" {
				params.State = &state
			}
			if monitorType != "" {
				params.Type = &monitorType
			}
			if search != "" {
				params.Q = &search
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ReadMonitorInventoryWithResponse(ctx, params)
			if err != nil {
				return unreachable(err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			if flags.asJSON {
				return writeJSON(output, response.JSON200)
			}
			for _, item := range response.JSON200.Items {
				if _, err := fmt.Fprintf(output,
					"%s/%s\tproject=%s\tname=%s\tpurpose=%s\ttype=%s\tmode=%s\ttarget=%s\tinterval_seconds=%d\ttolerance_seconds=%s\tstate=%s\toverdue=%t\tincident_open=%t\tmaintenance_until=%s\tlatest_observation=%s\tlast_success=%s\tnext_due=%s\n",
					item.ProjectKey, item.Key, strconv.Quote(item.ProjectName), strconv.Quote(item.Name),
					quoteOptional(item.Purpose), item.Type, quoteOptional(item.Mode), quoteOptional(item.TargetUrl),
					item.IntervalSeconds, formatInt(item.ToleranceSeconds), item.State, item.Overdue,
					item.IncidentOpen, formatTime(item.MaintenanceUntil),
					formatTime(item.LatestObservationAt), formatTime(item.LastSuccessAt),
					formatTime(item.NextDueAt)); err != nil {
					return err
				}
			}
			_, err = fmt.Fprintf(output, "page\ttotal=%d\tlimit=%d\toffset=%d\thas_more=%t\n",
				response.JSON200.Total, response.JSON200.Limit, response.JSON200.Offset, response.JSON200.HasMore)
			return err
		},
	}
	bindManagementFlags(command, flags)
	command.Flags().StringVar(&project, "project", "", "restrict to one project key")
	command.Flags().StringVar(&state, "state", "", "healthy, failing, untested, or paused")
	command.Flags().StringVar(&monitorType, "type", "", "http or push")
	command.Flags().StringVar(&search, "search", "", "search project, monitor, purpose, or safe target")
	command.Flags().Int32Var(&limit, "limit", 50, "page size (1-100)")
	command.Flags().Int32Var(&offset, "offset", 0, "zero-based page offset")
	return command
}
