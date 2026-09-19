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

const projectReportBody = `{"generated_at":"2026-09-19T12:00:00Z","project":{"id":"f0187842-6f73-4c54-8a8c-57ac7c117c39","key":"backup-jobs","name":"Backup jobs","version":3,"created_at":"2026-09-16T12:00:00Z","updated_at":"2026-09-16T13:00:00Z"},"counts":{"total":4,"http":1,"push":3,"healthy":1,"failing":2,"untested":1,"paused":0},"attention":[{"type":"push","id":"f0187842-6f73-4c54-8a8c-57ac7c117c40","key":"missing","name":"Missing backup","state":"failing","mode":"job_completion","version":2,"interval_seconds":30,"tolerance_seconds":0,"overdue":true,"latest_result":{"id":"f0187842-6f73-4c54-8a8c-57ac7c117c41","outcome":"failure","reason":"report_missing","observed_at":"2026-09-19T11:00:00Z"},"last_success":{"id":"f0187842-6f73-4c54-8a8c-57ac7c117c42","outcome":"success","observed_at":"2026-09-18T11:00:00Z"},"incident":{"id":"f0187842-6f73-4c54-8a8c-57ac7c117c43","began_at":"2026-09-19T11:00:00Z","opened_at":"2026-09-19T11:00:01Z","age_seconds":3600,"original_reason":"report_missing","latest_reason":"report_missing"},"instruction":"Inspect backup log\nincident\tforged","runbook_url":"https://docs.example.test/backup"},{"type":"http","id":"f0187842-6f73-4c54-8a8c-57ac7c117c44","key":"site","name":"Site","state":"failing","version":1,"target_url":"https://example.test/health","expected_status_code":200,"text_condition":"none","interval_seconds":60,"timeout_seconds":10,"failure_threshold":3,"failure_count":1,"overdue":false,"latest_result":{"id":"f0187842-6f73-4c54-8a8c-57ac7c117c45","outcome":"failure","reason":"status_mismatch","observed_at":"2026-09-19T11:59:00Z"}},{"type":"push","id":"f0187842-6f73-4c54-8a8c-57ac7c117c46","key":"waiting","name":"Waiting","state":"untested","mode":"state_report","version":1,"interval_seconds":60,"tolerance_seconds":5,"overdue":false}],"healthy":[{"type":"push","id":"f0187842-6f73-4c54-8a8c-57ac7c117c47","key":"okay","name":"Okay","mode":"state_report","last_success_at":"2026-09-19T11:59:00Z","next_due_at":"2026-09-19T12:01:00Z"}],"email":{"configured":true,"recipients":["ops@example.test"],"delivery":{"project_key":"backup-jobs","pending_count":0,"retrying_count":0,"terminal_failure_count":1,"smtp_accepted_count":2}}}`

func TestProjectReportUsesOneGeneratedCallAndEscapesText(t *testing.T) {
	calls := 0
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		calls++
		if request.Method != http.MethodGet || request.URL.Path != "/api/projects/backup-jobs/report" ||
			request.Header.Get("Authorization") != "Bearer report-credential" {
			t.Fatalf("unexpected request: %s %s", request.Method, request.URL.RequestURI())
		}
		writer.Header().Set("Content-Type", "application/json")
		_, _ = io.WriteString(writer, projectReportBody)
	}))
	defer instance.Close()
	args := []string{"project", "report", "backup-jobs", "--url", instance.URL,
		"--credential", "report-credential"}
	code, text, diagnostics := run(t, args...)
	if code != 0 || diagnostics != "" || calls != 1 {
		t.Fatalf("code=%d calls=%d stdout=%q stderr=%q", code, calls, text, diagnostics)
	}
	for _, expected := range []string{"report_missing", "last_success=", "age_seconds=3600",
		"terminal_failure=1", "operator_guidance", "runbook_url=", "status_mismatch"} {
		if !strings.Contains(text, expected) {
			t.Fatalf("missing %q from %q", expected, text)
		}
	}
	if strings.Index(text, "key=\"missing\"") > strings.Index(text, "key=\"site\"") ||
		strings.Index(text, "key=\"site\"") > strings.Index(text, "key=\"waiting\"") ||
		strings.Index(text, "key=\"waiting\"") > strings.Index(text, "key=\"okay\"") {
		t.Fatalf("wrong ordering: %q", text)
	}
	if strings.Contains(text, "Inspect backup log\nincident\tforged") ||
		!strings.Contains(text, `Inspect backup log\nincident\tforged`) {
		t.Fatalf("control characters were not escaped: %q", text)
	}
	code, machine, diagnostics := run(t, append(args, "--json")...)
	if code != 0 || diagnostics != "" || calls != 2 {
		t.Fatalf("code=%d calls=%d stdout=%q stderr=%q", code, calls, machine, diagnostics)
	}
	var value map[string]any
	if json.Unmarshal([]byte(machine), &value) != nil || value["project"].(map[string]any)["key"] != "backup-jobs" {
		t.Fatalf("invalid JSON report: %q", machine)
	}
}

func TestProjectReportFailuresLeaveStdoutEmpty(t *testing.T) {
	for _, item := range []struct {
		name   string
		status int
		body   string
		code   int
	}{
		{"authentication", 401, `{"code":"authentication_rejected","title":"remote secret"}`, process.Unauthorized},
		{"missing", 404, `{"code":"not_found","title":"remote secret"}`, process.NotFound},
		{"malformed", 200, `{bad`, process.Unexpected},
	} {
		t.Run(item.name, func(t *testing.T) {
			instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
				writer.Header().Set("Content-Type", "application/json")
				writer.WriteHeader(item.status)
				_, _ = io.WriteString(writer, item.body)
			}))
			defer instance.Close()
			code, output, diagnostics := run(t, "project", "report", "backup-jobs",
				"--url", instance.URL, "--credential", "hidden-value", "--json")
			if code != item.code || output != "" || diagnostics == "" ||
				strings.Contains(diagnostics, "remote secret") || strings.Contains(diagnostics, "hidden-value") {
				t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
			}
		})
	}
	instance := httptest.NewServer(http.HandlerFunc(func(http.ResponseWriter, *http.Request) {}))
	address := instance.URL
	instance.Close()
	code, output, diagnostics := run(t, "project", "report", "backup-jobs",
		"--url", address, "--credential", "hidden-value", "--json")
	if code != process.Unreachable || output != "" || diagnostics == "" ||
		strings.Contains(diagnostics, "hidden-value") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}

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
