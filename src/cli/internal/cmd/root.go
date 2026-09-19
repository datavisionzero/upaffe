package cmd

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"time"

	"github.com/datavisionzero/upaffe/src/cli/internal/api"
	"github.com/datavisionzero/upaffe/src/cli/internal/config"
	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
	"github.com/datavisionzero/upaffe/src/cli/internal/version"
	"github.com/spf13/cobra"
)

type environment func(string) string

func Run(
	arguments []string,
	input io.Reader,
	output io.Writer,
	diagnostics io.Writer,
	getenv environment,
) int {
	root := newRoot(output, getenv)
	root.SetArgs(arguments)
	root.SetIn(input)
	root.SetOut(output)
	root.SetErr(diagnostics)
	root.SilenceErrors = true
	root.SilenceUsage = true

	if err := root.Execute(); err != nil {
		fmt.Fprintf(diagnostics, "ua: %v\n", err)
		return process.CodeOf(err)
	}
	return process.Success
}

func newRoot(output io.Writer, getenv environment) *cobra.Command {
	root := &cobra.Command{
		Use:               "ua",
		Short:             "Administer an upaffe instance",
		CompletionOptions: cobra.CompletionOptions{DisableDefaultCmd: true},
	}
	root.AddCommand(newVersion(output))
	root.AddCommand(newStatus(output, getenv))
	root.AddCommand(newCredential(output, getenv))
	root.AddCommand(newProject(output, getenv))
	root.AddCommand(newMonitor(output, getenv))
	root.AddCommand(newPush(output, getenv))
	root.AddCommand(newEmail(output, getenv))
	root.AddCommand(newMaintenance(output, getenv))
	return root
}

func newVersion(output io.Writer) *cobra.Command {
	var asJSON bool
	command := &cobra.Command{
		Use:   "version",
		Short: "Print this CLI version without contacting an instance",
		Args:  cobra.NoArgs,
		RunE: func(*cobra.Command, []string) error {
			if asJSON {
				return writeJSON(output, map[string]string{"version": version.Value})
			}
			_, err := fmt.Fprintln(output, version.Value)
			return err
		},
	}
	command.Flags().BoolVar(&asJSON, "json", false, "write machine-readable JSON")
	return command
}

func newStatus(output io.Writer, getenv environment) *cobra.Command {
	var address string
	var asJSON bool
	command := &cobra.Command{
		Use:   "status",
		Short: "Ask an instance for its technical version",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			resolved, err := config.ResolveURL(address, config.Environment(getenv))
			if err != nil {
				return process.New(process.Usage, "%v", err)
			}

			client, err := api.NewClientWithResponses(resolved)
			if err != nil {
				return process.New(process.Usage, "invalid instance address: %v", err)
			}
			ctx, cancel := context.WithTimeout(command.Context(), 10*time.Second)
			defer cancel()
			response, err := client.ReadVersionWithResponse(ctx)
			if err != nil {
				return process.New(process.Unreachable, "instance is unreachable: %v", err)
			}

			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return responseError(response.StatusCode())
			}

			result := struct {
				URL     string `json:"url"`
				Version string `json:"version"`
			}{URL: resolved, Version: response.JSON200.Version}
			if asJSON {
				return writeJSON(output, result)
			}
			_, err = fmt.Fprintf(output, "live\t%s\t%s\n", result.Version, result.URL)
			return err
		},
	}
	command.Flags().StringVar(&address, "url", "", "instance address (otherwise UPAFFE_URL)")
	command.Flags().BoolVar(&asJSON, "json", false, "write machine-readable JSON")
	return command
}

func responseError(status int) error {
	switch status {
	case http.StatusUnauthorized, http.StatusForbidden:
		return process.New(process.Unauthorized, "instance did not authorize the request (HTTP %d)", status)
	case http.StatusNotFound:
		return process.New(process.NotFound, "instance has no such endpoint (HTTP %d)", status)
	case http.StatusBadRequest, http.StatusConflict, http.StatusUnprocessableEntity:
		return process.New(process.Refused, "instance refused the request (HTTP %d)", status)
	default:
		return process.New(process.Unexpected, "instance returned HTTP %d", status)
	}
}

func responseProblem(status int, body []byte) error {
	var problem api.ProblemResponse
	if json.Unmarshal(body, &problem) == nil && problem.Code != "" {
		classified := responseError(status)
		var failure *process.Error
		if errors.As(classified, &failure) {
			return process.New(failure.Code, "%s (HTTP %d)", problem.Code, status)
		}
	}
	return responseError(status)
}

func responseOrTransportError(err error) error {
	var address *url.Error
	if errors.As(err, &address) || errors.Is(err, context.DeadlineExceeded) {
		return process.New(process.Unreachable, "instance_unreachable")
	}
	return process.New(process.Unexpected, "unexpected_response")
}

func writeJSON(output io.Writer, value any) error {
	encoder := json.NewEncoder(output)
	encoder.SetEscapeHTML(false)
	return encoder.Encode(value)
}
