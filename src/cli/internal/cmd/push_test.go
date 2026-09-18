package cmd

import (
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
)

const (
	pushMonitorID   = "8b48c497-0bf9-4482-84a0-edc9c50f05dc"
	pushReportID    = "f62b26cc-0bc6-4f6f-bb42-a6e0a9111b09"
	pushIncidentID  = "b56cd145-59c8-4277-858c-3e758521287b"
	pushMonitorBody = `{
  "created_at":"2026-09-18T12:00:00Z",
  "deleted_at":null,
  "has_reporting_credential":true,
  "id":"` + pushMonitorID + `",
  "instruction":"Inspect the job log",
  "interval_seconds":86400,
  "key":"nightly-backup",
  "last_received_at":"2026-09-18T12:01:00Z",
  "latest_report_id":"` + pushReportID + `",
  "latest_success_id":"` + pushReportID + `",
  "mode":"job_completion",
  "name":"Nightly backup",
  "next_deadline_at":"2026-09-19T13:01:00Z",
  "open_incident_id":"` + pushIncidentID + `",
  "paused_at":null,
  "project_key":"backups",
  "runbook_url":"https://docs.example.test/runbooks/nightly",
  "state":"failing",
  "tolerance_seconds":3600,
  "updated_at":"2026-09-18T12:01:00Z",
  "version":4
}`
	pushReportBody = `{
  "applicable":true,
  "evaluation_generation":1,
  "id":"` + pushReportID + `",
  "observed_at":"2026-09-18T12:00:00Z",
  "outcome":"failure",
  "reason":"reported_failure",
  "received_at":"2026-09-18T12:01:00Z",
  "report_id":"96f5b8c6-40f6-46a0-a1e9-8b7960474c55",
  "sequence":9
}`
	pushIncidentBody = `{
  "began_at":"2026-09-18T12:00:00Z",
  "id":"` + pushIncidentID + `",
  "last_observed_at":"2026-09-18T12:00:00Z",
  "latest_failure_report_id":"` + pushReportID + `",
  "latest_failure_sequence":9,
  "latest_reason":"reported_failure",
  "opened_at":"2026-09-18T12:01:00Z",
  "opening_report_id":"` + pushReportID + `",
  "opening_sequence":9,
  "original_reason":"reported_failure",
  "resolution_report_id":null,
  "resolution_sequence":null,
  "resolved_at":null
}`
	credentialBody        = `{"created_at":"2026-09-18T12:00:00Z","id":"6b63a1c5-4db7-48e9-a566-70ea48f1197c","revoked_at":null,"rotated_at":null}`
	issuedCredentialBody  = `{"created_at":"2026-09-18T12:00:00Z","id":"6b63a1c5-4db7-48e9-a566-70ea48f1197c","previous_valid_until":null,"report_url":"/api/report/<one-time-reporting-token>","rotated_at":null,"token":"<one-time-reporting-token>"}`
	rotatedCredentialBody = `{"created_at":"2026-09-18T12:00:00Z","id":"6b63a1c5-4db7-48e9-a566-70ea48f1197c","previous_valid_until":"2026-09-18T12:10:00Z","report_url":"/api/report/<replacement-reporting-token>","rotated_at":"2026-09-18T12:05:00Z","token":"<replacement-reporting-token>"}`
)

func TestPushCommandsCoverGeneratedContractAndKeepOrdinaryOutputSecretFree(t *testing.T) {
	calls := map[string]int{}
	modes := map[string]int{}
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.Header.Get("Authorization") != "Bearer management-token" {
			t.Fatalf("authorization=%q", request.Header.Get("Authorization"))
		}
		writer.Header().Set("Content-Type", "application/json")
		path := request.URL.Path
		calls[request.Method+" "+path]++
		switch {
		case request.Method == http.MethodPost && path == "/api/projects/backups/push-monitors":
			var body map[string]any
			if err := json.NewDecoder(request.Body).Decode(&body); err != nil {
				t.Fatal(err)
			}
			modes[body["mode"].(string)]++
			writer.WriteHeader(http.StatusCreated)
			_, _ = io.WriteString(writer, pushMonitorBody)
		case request.Method == http.MethodGet && path == "/api/projects/backups/push-monitors":
			_, _ = io.WriteString(writer, "["+pushMonitorBody+"]")
		case request.Method == http.MethodGet && path == "/api/projects/backups/push-monitors/nightly-backup":
			_, _ = io.WriteString(writer, pushMonitorBody)
		case request.Method == http.MethodPut && path == "/api/projects/backups/push-monitors/nightly-backup":
			assertJSONBody(t, request, map[string]any{"instruction": nil, "interval_seconds": float64(43200), "name": "Updated backup", "runbook_url": nil, "tolerance_seconds": float64(1800), "version": float64(4)})
			_, _ = io.WriteString(writer, pushMonitorBody)
		case request.Method == http.MethodDelete && path == "/api/projects/backups/push-monitors/nightly-backup":
			assertQuery(t, request, "version", "4")
			_, _ = io.WriteString(writer, pushMonitorBody)
		case request.Method == http.MethodPost && path == "/api/projects/backups/push-monitors/nightly-backup/pause":
			assertJSONBody(t, request, map[string]any{"version": float64(4)})
			_, _ = io.WriteString(writer, pushMonitorBody)
		case request.Method == http.MethodPost && path == "/api/projects/backups/push-monitors/nightly-backup/resume":
			assertJSONBody(t, request, map[string]any{"version": float64(4)})
			_, _ = io.WriteString(writer, pushMonitorBody)
		case request.Method == http.MethodGet && path == "/api/projects/backups/push-monitors/nightly-backup/reports":
			assertQuery(t, request, "before_sequence", "10")
			assertQuery(t, request, "limit", "2")
			_, _ = io.WriteString(writer, `{"items":[`+pushReportBody+`],"next_before_sequence":9}`)
		case request.Method == http.MethodGet && path == "/api/projects/backups/push-monitors/nightly-backup/incidents":
			assertQuery(t, request, "before_opening_sequence", "10")
			assertQuery(t, request, "limit", "2")
			_, _ = io.WriteString(writer, `{"items":[`+pushIncidentBody+`],"next_before_opening_sequence":9}`)
		case request.Method == http.MethodPost && path == "/api/projects/backups/push-monitors/nightly-backup/reporting-credential":
			writer.WriteHeader(http.StatusCreated)
			_, _ = io.WriteString(writer, issuedCredentialBody)
		case request.Method == http.MethodGet && path == "/api/projects/backups/push-monitors/nightly-backup/reporting-credential":
			_, _ = io.WriteString(writer, credentialBody)
		case request.Method == http.MethodPost && path == "/api/projects/backups/push-monitors/nightly-backup/reporting-credential/rotate":
			_, _ = io.WriteString(writer, rotatedCredentialBody)
		case request.Method == http.MethodDelete && path == "/api/projects/backups/push-monitors/nightly-backup/reporting-credential":
			writer.WriteHeader(http.StatusNoContent)
		default:
			t.Fatalf("asked %s %s", request.Method, request.URL.RequestURI())
		}
	}))
	defer instance.Close()

	statePath := filepath.Join(t.TempDir(), "state.json")
	stateCreate := `{"instruction":null,"interval_seconds":60,"key":"local-state","mode":"state_report","name":"Local state","runbook_url":null,"tolerance_seconds":30}`
	if err := os.WriteFile(statePath, []byte(stateCreate), 0o600); err != nil {
		t.Fatal(err)
	}
	updatePath := filepath.Join(t.TempDir(), "update.json")
	update := `{"instruction":null,"interval_seconds":43200,"name":"Updated backup","runbook_url":null,"tolerance_seconds":1800,"version":4}`
	if err := os.WriteFile(updatePath, []byte(update), 0o600); err != nil {
		t.Fatal(err)
	}
	jobCreate := `{"instruction":null,"interval_seconds":86400,"key":"nightly-backup","mode":"job_completion","name":"Nightly backup","runbook_url":null,"tolerance_seconds":3600}`

	type commandCase struct {
		input        string
		args         []string
		want         string
		secretOutput bool
	}
	cases := []commandCase{
		{jobCreate, []string{"push", "create", "backups", "--file", "-"}, "job_completion", false},
		{"", []string{"push", "create", "backups", "--file", statePath}, "job_completion", false},
		{"", []string{"push", "list", "backups"}, "last_received=2026-09-18T12:01:00Z", false},
		{"", []string{"push", "get", "backups", "nightly-backup", "--json"}, `"has_reporting_credential":true`, false},
		{"", []string{"push", "update", "backups", "nightly-backup", "--file", updatePath}, "backups/nightly-backup", false},
		{"", []string{"push", "delete", "backups", "nightly-backup", "--version", "4"}, "backups/nightly-backup", false},
		{"", []string{"push", "pause", "backups", "nightly-backup", "--version", "4"}, "failing", false},
		{"", []string{"push", "resume", "backups", "nightly-backup", "--version", "4"}, "failing", false},
		{"", []string{"push", "reports", "backups", "nightly-backup", "--before-sequence", "10", "--limit", "2"}, "reason=reported_failure", false},
		{"", []string{"push", "incidents", "backups", "nightly-backup", "--before-opening-sequence", "10", "--limit", "2", "--json"}, `"next_before_opening_sequence":9`, false},
		{"", []string{"push", "credential", "issue", "backups", "nightly-backup"}, "<one-time-reporting-token>", true},
		{"", []string{"push", "credential", "get", "backups", "nightly-backup", "--json"}, `"revoked_at":null`, false},
		{"", []string{"push", "credential", "rotate", "backups", "nightly-backup", "--json"}, "<replacement-reporting-token>", true},
		{"", []string{"push", "credential", "revoke", "backups", "nightly-backup"}, "revoked", false},
	}
	for _, item := range cases {
		arguments := append(item.args, "--url", instance.URL, "--credential", "management-token")
		code, output, diagnostics := runWithInput(t, item.input, arguments...)
		if code != process.Success || diagnostics != "" || !strings.Contains(output, item.want) {
			t.Fatalf("args=%v code=%d stdout=%q stderr=%q", arguments, code, output, diagnostics)
		}
		if strings.Contains(output+diagnostics, "management-token") {
			t.Fatalf("management credential exposed: %v", arguments)
		}
		if !item.secretOutput && (strings.Contains(output, "<one-time-reporting-token>") || strings.Contains(output, "<replacement-reporting-token>")) {
			t.Fatalf("ordinary output exposed reporting token: args=%v stdout=%q", arguments, output)
		}
	}

	if modes["job_completion"] != 1 || modes["state_report"] != 1 {
		t.Fatalf("modes=%v", modes)
	}
	for _, operation := range []string{
		"POST /api/projects/backups/push-monitors",
		"GET /api/projects/backups/push-monitors",
		"GET /api/projects/backups/push-monitors/nightly-backup",
		"PUT /api/projects/backups/push-monitors/nightly-backup",
		"DELETE /api/projects/backups/push-monitors/nightly-backup",
		"POST /api/projects/backups/push-monitors/nightly-backup/pause",
		"POST /api/projects/backups/push-monitors/nightly-backup/resume",
		"GET /api/projects/backups/push-monitors/nightly-backup/reports",
		"GET /api/projects/backups/push-monitors/nightly-backup/incidents",
		"POST /api/projects/backups/push-monitors/nightly-backup/reporting-credential",
		"GET /api/projects/backups/push-monitors/nightly-backup/reporting-credential",
		"POST /api/projects/backups/push-monitors/nightly-backup/reporting-credential/rotate",
		"DELETE /api/projects/backups/push-monitors/nightly-backup/reporting-credential",
	} {
		if calls[operation] == 0 {
			t.Errorf("operation not called: %s", operation)
		}
	}
}

func TestPushProblemsAndArgumentsAreRedactedAndClassified(t *testing.T) {
	cases := [][]string{
		{"push", "create", "backups"},
		{"push", "update", "backups", "nightly"},
		{"push", "delete", "backups", "nightly", "--version", "0"},
		{"push", "pause", "backups", "nightly", "--version", "-1"},
		{"push", "reports", "backups", "nightly", "--limit", "101"},
		{"push", "incidents", "backups", "nightly", "--before-opening-sequence", "-1"},
	}
	for _, arguments := range cases {
		code, output, diagnostics := runWithInput(t, "", arguments...)
		if code != process.Usage || output != "" || diagnostics == "" {
			t.Fatalf("args=%v code=%d stdout=%q stderr=%q", arguments, code, output, diagnostics)
		}
	}

	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
		writer.Header().Set("Content-Type", "application/problem+json")
		writer.WriteHeader(http.StatusConflict)
		_, _ = io.WriteString(writer, `{"code":"conflict","title":"<remote-response-secret>","status":409}`)
	}))
	defer instance.Close()
	code, output, diagnostics := runWithInput(t, "", "push", "get", "backups", "nightly", "--url", instance.URL, "--credential", "<management-secret>")
	if code != process.Refused || output != "" || !strings.Contains(diagnostics, "conflict") || strings.Contains(diagnostics, "remote-response-secret") || strings.Contains(diagnostics, "management-secret") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}
