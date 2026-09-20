package cmd

import (
	"bytes"
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

type diagnostic struct {
	Code       string `json:"code"`
	ExitCode   int    `json:"exit_code"`
	HTTPStatus *int   `json:"http_status,omitempty"`
}

type environment func(string) string

func Run(
	arguments []string,
	input io.Reader,
	output io.Writer,
	diagnostics io.Writer,
	getenv environment,
) int {
	var buffered bytes.Buffer
	root := newRoot(&buffered, getenv)
	root.SetArgs(arguments)
	root.SetIn(input)
	root.SetOut(&buffered)
	root.SetErr(diagnostics)
	root.SilenceErrors = true
	root.SilenceUsage = true

	if err := root.Execute(); err != nil {
		code, machineCode, httpStatus := process.DetailsOf(err)
		if code == process.CheckFailed {
			if _, copyErr := io.Copy(output, &buffered); copyErr != nil {
				return process.Unexpected
			}
		}
		if jsonRequested(arguments) {
			result := diagnostic{Code: machineCode, ExitCode: code}
			if httpStatus != 0 {
				result.HTTPStatus = &httpStatus
			}
			_ = writeJSON(diagnostics, result)
		} else if httpStatus != 0 {
			fmt.Fprintf(diagnostics, "ua: %s (HTTP %d)\n", machineCode, httpStatus)
		} else {
			fmt.Fprintf(diagnostics, "ua: %s\n", machineCode)
		}
		return code
	}
	if _, err := io.Copy(output, &buffered); err != nil {
		return process.Unexpected
	}
	return process.Success
}

func jsonRequested(arguments []string) bool {
	for _, argument := range arguments {
		if argument == "--json" || argument == "--json=true" {
			return true
		}
	}
	return false
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
	root.AddCommand(newInventory(output, getenv))
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
				return responseOrTransportError(err)
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
		return process.Remote(process.Unauthorized, "unauthorized", status)
	case http.StatusNotFound:
		return process.Remote(process.NotFound, "not_found", status)
	case http.StatusBadRequest, http.StatusConflict, http.StatusUnprocessableEntity:
		return process.Remote(process.Refused, "request_refused", status)
	default:
		return process.Remote(process.Unexpected, "unexpected_response", status)
	}
}

func responseProblem(status int, body []byte) error {
	var problem api.ProblemResponse
	if json.Unmarshal(body, &problem) == nil && knownProblemCode(problem.Code, status) {
		category := process.CodeOf(responseError(status))
		if problem.Code == "smtp_rejected" && status == http.StatusBadGateway {
			category = process.Refused
		}
		return process.Remote(category, problem.Code, status)
	}
	return responseError(status)
}

func knownProblemCode(code string, status int) bool {
	switch code {
	case "validation":
		return status == http.StatusBadRequest
	case "sign_in_rejected", "authentication_required",
		"authentication_rejected", "reporting_rejected":
		return status == http.StatusUnauthorized
	case "forbidden":
		return status == http.StatusForbidden
	case "conflict", "report_id_conflict", "email_not_configured":
		return status == http.StatusConflict
	case "not_found":
		return status == http.StatusNotFound
	case "unprocessable":
		return status == http.StatusUnprocessableEntity
	case "smtp_rejected":
		return status == http.StatusBadGateway
	default:
		return false
	}
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
