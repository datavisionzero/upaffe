package cmd

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"time"

	"github.com/datavisionzero/upaffe/src/cli/internal/api"
	"github.com/datavisionzero/upaffe/src/cli/internal/config"
	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
	"github.com/google/uuid"
	"github.com/spf13/cobra"
)

type managementFlags struct {
	address    string
	credential string
	asJSON     bool
}

func newCredential(output io.Writer, getenv environment) *cobra.Command {
	flags := &managementFlags{}
	command := &cobra.Command{
		Use:   "credential",
		Short: "Manage noninteractive management credentials",
	}
	bindManagementFlags(command, flags)
	command.AddCommand(newCredentialCreate(output, getenv, flags))
	command.AddCommand(newCredentialList(output, getenv, flags))
	command.AddCommand(newCredentialRotate(output, getenv, flags))
	command.AddCommand(newCredentialRevoke(output, getenv, flags))
	return command
}

func bindManagementFlags(command *cobra.Command, flags *managementFlags) {
	command.PersistentFlags().StringVar(&flags.address, "url", "", "instance address (otherwise UPAFFE_URL)")
	command.PersistentFlags().StringVar(
		&flags.credential,
		"credential",
		"",
		"management credential (otherwise UPAFFE_CREDENTIAL)")
	command.PersistentFlags().BoolVar(&flags.asJSON, "json", false, "write machine-readable JSON")
}

func newCredentialCreate(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	var name string
	command := &cobra.Command{
		Use:   "create",
		Short: "Create a named credential and print its token once",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			if name == "" {
				return process.New(process.Usage, "--name is required")
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.CreateManagementCredentialWithResponse(
				ctx,
				api.CreateCredentialRequest{Name: &name})
			if err != nil {
				return process.New(process.Unreachable, "instance is unreachable: %v", err)
			}
			if response.StatusCode() != http.StatusCreated || response.JSON201 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			if flags.asJSON {
				return writeJSON(output, response.JSON201)
			}
			_, err = fmt.Fprintf(
				output,
				"%s\t%s\t%s\n",
				response.JSON201.Id,
				response.JSON201.Name,
				response.JSON201.Token)
			return err
		},
	}
	command.Flags().StringVar(&name, "name", "", "stable descriptive name")
	return command
}

func newCredentialList(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use:   "list",
		Short: "List credential metadata without tokens",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.ListManagementCredentialsWithResponse(ctx)
			if err != nil {
				return process.New(process.Unreachable, "instance is unreachable: %v", err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			if flags.asJSON {
				return writeJSON(output, response.JSON200)
			}
			for _, item := range *response.JSON200 {
				state := "active"
				if item.RevokedAt != nil {
					state = "revoked"
				}
				if _, err := fmt.Fprintf(output, "%s\t%s\t%s\n", item.Id, item.Name, state); err != nil {
					return err
				}
			}
			return nil
		},
	}
}

func newCredentialRotate(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use:   "rotate <id>",
		Short: "Rotate a credential and print its new token once",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			id, err := credentialID(arguments[0])
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.RotateManagementCredentialWithResponse(ctx, id)
			if err != nil {
				return process.New(process.Unreachable, "instance is unreachable: %v", err)
			}
			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseProblem(response.StatusCode(), response.Body)
			}
			if flags.asJSON {
				return writeJSON(output, response.JSON200)
			}
			_, err = fmt.Fprintf(
				output,
				"%s\t%s\t%s\n",
				response.JSON200.Id,
				response.JSON200.Name,
				response.JSON200.Token)
			return err
		},
	}
}

func newCredentialRevoke(output io.Writer, getenv environment, flags *managementFlags) *cobra.Command {
	return &cobra.Command{
		Use:   "revoke <id>",
		Short: "Revoke a credential immediately",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, arguments []string) error {
			id, err := credentialID(arguments[0])
			if err != nil {
				return err
			}
			client, ctx, cancel, err := managementClient(command, flags, getenv)
			if err != nil {
				return err
			}
			defer cancel()
			response, err := client.RevokeManagementCredentialWithResponse(ctx, id)
			if err != nil {
				return process.New(process.Unreachable, "instance is unreachable: %v", err)
			}
			if response.StatusCode() != http.StatusNoContent {
				return responseProblem(response.StatusCode(), response.Body)
			}
			result := struct {
				ID      uuid.UUID `json:"id"`
				Revoked bool      `json:"revoked"`
			}{ID: id, Revoked: true}
			if flags.asJSON {
				return writeJSON(output, result)
			}
			_, err = fmt.Fprintf(output, "revoked\t%s\n", id)
			return err
		},
	}
}

func managementClient(
	command *cobra.Command,
	flags *managementFlags,
	getenv environment,
) (*api.ClientWithResponses, context.Context, context.CancelFunc, error) {
	return managementClientWithTimeout(command, flags, getenv, 10*time.Second)
}

func managementClientWithTimeout(
	command *cobra.Command,
	flags *managementFlags,
	getenv environment,
	timeout time.Duration,
) (*api.ClientWithResponses, context.Context, context.CancelFunc, error) {
	address, err := config.ResolveURL(flags.address, config.Environment(getenv))
	if err != nil {
		return nil, nil, nil, process.New(process.Usage, "%v", err)
	}
	credential, err := config.ResolveCredential(flags.credential, config.Environment(getenv))
	if err != nil {
		return nil, nil, nil, process.New(process.Unauthorized, "authentication_required: %v", err)
	}
	client, err := api.NewClientWithResponses(
		address,
		api.WithRequestEditorFn(func(_ context.Context, request *http.Request) error {
			request.Header.Set("Authorization", "Bearer "+credential)
			return nil
		}))
	if err != nil {
		return nil, nil, nil, process.New(process.Usage, "invalid instance address: %v", err)
	}
	ctx, cancel := context.WithTimeout(command.Context(), timeout)
	return client, ctx, cancel, nil
}

func credentialID(value string) (uuid.UUID, error) {
	id, err := uuid.Parse(value)
	if err != nil || id == uuid.Nil {
		return uuid.Nil, process.New(process.Usage, "credential id must be a non-zero UUID")
	}
	return id, nil
}
