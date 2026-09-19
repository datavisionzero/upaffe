package cmd

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
)

func run(t *testing.T, arguments ...string) (int, string, string) {
	t.Helper()
	var output bytes.Buffer
	var diagnostics bytes.Buffer
	code := Run(arguments, strings.NewReader(""), &output, &diagnostics, func(string) string { return "" })
	return code, output.String(), diagnostics.String()
}

func server(t *testing.T, status int, body string) *httptest.Server {
	t.Helper()
	return httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.URL.Path != "/api/version" {
			t.Fatalf("asked %s", request.URL.Path)
		}
		writer.Header().Set("Content-Type", "application/json")
		writer.WriteHeader(status)
		_, _ = io.WriteString(writer, body)
	}))
}

func TestVersionAndHelpDoNotNeedAnInstance(t *testing.T) {
	code, output, diagnostics := run(t, "version", "--json")
	if code != 0 || diagnostics != "" || !strings.Contains(output, `"version":"0.0.0-dev"`) {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}

	code, output, diagnostics = run(t, "--help")
	if code != 0 || diagnostics != "" || !strings.Contains(output, "status") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}

func TestStatusUsesTheGeneratedClientAndJSON(t *testing.T) {
	instance := server(t, http.StatusOK, `{"version":"1.2.3"}`)
	defer instance.Close()

	code, output, diagnostics := run(t, "status", "--url", instance.URL, "--json")
	if code != 0 || diagnostics != "" {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
	var answer map[string]string
	if err := json.Unmarshal([]byte(output), &answer); err != nil {
		t.Fatal(err)
	}
	if answer["version"] != "1.2.3" || answer["url"] != instance.URL {
		t.Fatalf("answer=%v", answer)
	}
}

func TestStatusClassifiesFailuresAndKeepsDataOffStdout(t *testing.T) {
	cases := []struct {
		status int
		code   int
	}{
		{http.StatusUnauthorized, process.Unauthorized},
		{http.StatusForbidden, process.Unauthorized},
		{http.StatusNotFound, process.NotFound},
		{http.StatusUnprocessableEntity, process.Refused},
		{http.StatusInternalServerError, process.Unexpected},
	}
	for _, item := range cases {
		t.Run(fmt.Sprint(item.status), func(t *testing.T) {
			instance := server(t, item.status, `{"secret":"not printed"}`)
			defer instance.Close()
			code, output, diagnostics := run(t, "status", "--url", instance.URL)
			if code != item.code || output != "" || strings.Contains(diagnostics, "not printed") {
				t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
			}
		})
	}
}

func TestUsageAndNetworkErrorsHaveStableCodes(t *testing.T) {
	code, output, diagnostics := run(t, "status")
	if code != process.Usage || output != "" || !strings.Contains(diagnostics, "usage_error") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}

	code, output, diagnostics = run(t, "status", "--url", "http://127.0.0.1:1")
	if code != process.Unreachable || output != "" || diagnostics == "" {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}

func TestJSONProblemContractIsUniformAcrossManagementCommands(t *testing.T) {
	commands := []struct {
		name string
		args []string
	}{
		{"credential", []string{"credential", "list"}},
		{"project", []string{"project", "get", "backup-jobs"}},
		{"monitor", []string{"monitor", "get", "backup-jobs", "homepage"}},
		{"push", []string{"push", "get", "backup-jobs", "backup"}},
		{"email", []string{"email", "settings", "get"}},
		{"maintenance", []string{"maintenance", "get", "backup-jobs", "--scope", "project"}},
		{"report", []string{"project", "report", "backup-jobs"}},
	}
	problems := []struct {
		status int
		name   string
		exit   int
	}{
		{400, "validation", process.Refused},
		{401, "authentication_rejected", process.Unauthorized},
		{403, "forbidden", process.Unauthorized},
		{404, "not_found", process.NotFound},
		{409, "conflict", process.Refused},
		{422, "unprocessable", process.Refused},
		{502, "smtp_rejected", process.Refused},
	}
	for _, command := range commands {
		for _, problem := range problems {
			t.Run(command.name+"/"+problem.name, func(t *testing.T) {
				instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
					writer.Header().Set("Content-Type", "application/problem+json")
					writer.WriteHeader(problem.status)
					_, _ = fmt.Fprintf(writer,
						`{"code":%q,"title":"remote secret title","status":%d}`,
						problem.name, problem.status)
				}))
				defer instance.Close()
				args := append(append([]string(nil), command.args...),
					"--url", instance.URL, "--credential", "private-credential", "--json")
				code, output, diagnostics := run(t, args...)
				var parsed diagnostic
				if err := json.Unmarshal([]byte(diagnostics), &parsed); err != nil ||
					code != problem.exit || output != "" || parsed.ExitCode != problem.exit ||
					parsed.Code != problem.name || parsed.HTTPStatus == nil ||
					*parsed.HTTPStatus != problem.status ||
					strings.Contains(diagnostics, "remote secret") ||
					strings.Contains(diagnostics, "private-credential") {
					t.Fatalf("code=%d stdout=%q stderr=%q parse=%v", code, output, diagnostics, err)
				}
			})
		}
	}
}

func TestLocalJSONErrorsAreStableAndRedacted(t *testing.T) {
	for _, item := range []struct {
		name    string
		args    []string
		code    int
		machine string
	}{
		{"usage", []string{"project", "rename", "backup-jobs", "--json"}, process.Usage, "usage_error"},
		{"unsafe URL", []string{"project", "get", "backup-jobs", "--url", "https://example.test/?token=secret-query", "--credential", "private-credential", "--json"}, process.Usage, "usage_error"},
		{"unreachable", []string{"status", "--url", "http://127.0.0.1:1", "--json"}, process.Unreachable, "instance_unreachable"},
	} {
		t.Run(item.name, func(t *testing.T) {
			code, output, diagnostics := run(t, item.args...)
			var parsed diagnostic
			if err := json.Unmarshal([]byte(diagnostics), &parsed); err != nil ||
				code != item.code || output != "" || parsed.ExitCode != item.code ||
				parsed.Code != item.machine || parsed.HTTPStatus != nil ||
				strings.Contains(diagnostics, "secret-query") ||
				strings.Contains(diagnostics, "private-credential") {
				t.Fatalf("code=%d stdout=%q stderr=%q parse=%v", code, output, diagnostics, err)
			}
		})
	}
}
