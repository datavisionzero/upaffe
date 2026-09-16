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

func newProject(output io.Writer, getenv environment) *cobra.Command {
	flags := &managementFlags{}
	command := &cobra.Command{
		Use:   "project",
		Short: "Manage projects by immutable key",
	}
	bindManagementFlags(command, flags)
	command.AddCommand(newProjectCreate(output, getenv, flags))
	command.AddCommand(newProjectGet(output, getenv, flags))
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
	return process.New(process.Unreachable, "instance is unreachable: %v", err)
}
