package cmd

import (
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"reflect"
	"strings"
	"testing"

	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
)

const projectBody = `{"id":"f0187842-6f73-4c54-8a8c-57ac7c117c39","key":"backup-jobs","name":"Backup jobs","version":3,"created_at":"2026-09-16T12:00:00Z","updated_at":"2026-09-16T13:00:00Z","deleted_at":null}`

func TestProjectCommandsCoverTheGeneratedContractByImmutableKey(t *testing.T) {
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.Header.Get("Authorization") != "Bearer project-credential" {
			t.Fatalf("authorization=%q", request.Header.Get("Authorization"))
		}
		writer.Header().Set("Content-Type", "application/json")
		switch {
		case request.Method == http.MethodPost && request.URL.Path == "/api/projects":
			assertJSONBody(t, request, map[string]any{"key": "backup-jobs", "name": "Backup jobs"})
			writer.WriteHeader(http.StatusCreated)
			_, _ = io.WriteString(writer, projectBody)
		case request.Method == http.MethodGet && request.URL.Path == "/api/projects":
			if request.URL.Query().Get("deleted") != "true" {
				t.Fatalf("query=%q", request.URL.RawQuery)
			}
			_, _ = io.WriteString(writer, "["+projectBody+"]")
		case request.Method == http.MethodGet && request.URL.Path == "/api/projects/backup-jobs":
			_, _ = io.WriteString(writer, projectBody)
		case request.Method == http.MethodPut && request.URL.Path == "/api/projects/backup-jobs":
			assertJSONBody(t, request, map[string]any{"name": "Backups", "version": float64(3)})
			_, _ = io.WriteString(writer, projectBody)
		case request.Method == http.MethodDelete && request.URL.Path == "/api/projects/backup-jobs":
			if request.URL.Query().Get("version") != "3" {
				t.Fatalf("query=%q", request.URL.RawQuery)
			}
			_, _ = io.WriteString(writer, projectBody)
		case request.Method == http.MethodPost && request.URL.Path == "/api/projects/backup-jobs/restore":
			assertJSONBody(t, request, map[string]any{"version": float64(3)})
			_, _ = io.WriteString(writer, projectBody)
		default:
			t.Fatalf("asked %s %s", request.Method, request.URL.RequestURI())
		}
	}))
	defer instance.Close()

	cases := [][]string{
		{"project", "create", "--key", "backup-jobs", "--name", "Backup jobs"},
		{"project", "list", "--deleted"},
		{"project", "get", "backup-jobs"},
		{"project", "rename", "backup-jobs", "--name", "Backups", "--version", "3"},
		{"project", "delete", "backup-jobs", "--version", "3"},
		{"project", "restore", "backup-jobs", "--version", "3"},
	}
	for _, arguments := range cases {
		arguments = append(arguments, "--url", instance.URL, "--credential", "project-credential")
		code, output, diagnostics := run(t, arguments...)
		if code != 0 || diagnostics != "" || !strings.Contains(output, "backup-jobs") {
			t.Fatalf("args=%v code=%d stdout=%q stderr=%q", arguments, code, output, diagnostics)
		}
	}
}

func TestProjectHumanAndJSONOutputCarryTheSameFacts(t *testing.T) {
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
		writer.Header().Set("Content-Type", "application/json")
		_, _ = io.WriteString(writer, projectBody)
	}))
	defer instance.Close()

	common := []string{"project", "get", "backup-jobs", "--url", instance.URL, "--credential", "valid"}
	code, human, diagnostics := run(t, common...)
	if code != 0 || diagnostics != "" {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, human, diagnostics)
	}
	code, machine, diagnostics := run(t, append(common, "--json")...)
	if code != 0 || diagnostics != "" {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, machine, diagnostics)
	}

	var facts map[string]any
	if err := json.Unmarshal([]byte(machine), &facts); err != nil {
		t.Fatal(err)
	}
	for _, expected := range []string{
		facts["id"].(string),
		facts["key"].(string),
		facts["name"].(string),
		"3",
		facts["created_at"].(string),
		facts["updated_at"].(string),
		"live",
	} {
		if !strings.Contains(human, expected) {
			t.Fatalf("human output %q does not contain %q from JSON %q", human, expected, machine)
		}
	}
}

func TestProjectProblemsUseDocumentedExitCodesWithoutCopyingBodies(t *testing.T) {
	cases := []struct {
		status  int
		problem string
		exit    int
	}{
		{http.StatusBadRequest, "validation", process.Refused},
		{http.StatusConflict, "conflict", process.Refused},
		{http.StatusNotFound, "not_found", process.NotFound},
		{http.StatusUnauthorized, "authentication_rejected", process.Unauthorized},
	}
	for _, item := range cases {
		t.Run(item.problem, func(t *testing.T) {
			instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
				writer.Header().Set("Content-Type", "application/problem+json")
				writer.WriteHeader(item.status)
				_, _ = fmt.Fprintf(
					writer,
					`{"code":%q,"title":"sensitive remote detail","status":%d}`,
					item.problem,
					item.status)
			}))
			defer instance.Close()

			code, output, diagnostics := run(
				t,
				"project", "get", "stable-key",
				"--url", instance.URL,
				"--credential", "never-print-this")
			if code != item.exit || output != "" || !strings.Contains(diagnostics, item.problem) ||
				strings.Contains(diagnostics, "sensitive") || strings.Contains(diagnostics, "never-print-this") {
				t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
			}
		})
	}
}

func TestProjectCommandsRejectMissingFactsBeforeTheNetwork(t *testing.T) {
	cases := [][]string{
		{"project", "create", "--key", "only-a-key"},
		{"project", "rename", "stable-key", "--name", "A name"},
		{"project", "delete", "stable-key", "--version", "0"},
		{"project", "restore", "stable-key", "--version", "-1"},
	}
	for _, arguments := range cases {
		code, output, diagnostics := run(t, arguments...)
		if code != process.Usage || output != "" || diagnostics == "" {
			t.Fatalf("args=%v code=%d stdout=%q stderr=%q", arguments, code, output, diagnostics)
		}
	}
}

func assertJSONBody(t *testing.T, request *http.Request, expected map[string]any) {
	t.Helper()
	var actual map[string]any
	if err := json.NewDecoder(request.Body).Decode(&actual); err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("body=%v expected=%v", actual, expected)
	}
}
