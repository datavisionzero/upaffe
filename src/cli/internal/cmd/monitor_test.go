package cmd

import (
	"bytes"
	"encoding/json"
	"fmt"
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
	monitorID   = "4a572f67-2edb-46d3-86de-419608f16d83"
	checkID     = "f262d323-0562-4fd1-95e0-5cc80b17a4ec"
	incidentID  = "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901"
	monitorBody = `{
  "consecutive_failures":1,
  "created_at":"2026-09-16T12:00:00Z",
  "deleted_at":null,
  "expected_status_code":200,
  "failure_threshold":3,
  "has_target_query":true,
  "headers":[{"created_at":"2026-09-16T12:00:00Z","id":"ea634209-c0de-4487-b68a-e33fefc70ba7","name":"Authorization","updated_at":"2026-09-16T12:00:00Z"}],
  "id":"` + monitorID + `",
  "instruction":"Investigate the public endpoint",
  "interval_seconds":60,
  "key":"homepage",
  "latest_result_id":"` + checkID + `",
  "latest_success_id":null,
  "name":"Homepage",
  "next_check_at":"2026-09-16T12:02:00Z",
  "open_incident_id":null,
  "paused_at":null,
  "project_key":"public-site",
  "runbook_url":"https://docs.example.test/runbook",
  "state":"failing",
  "target_url":"https://status.example.test/health",
  "text_condition":"contains",
  "text_fragment":"ready",
  "timeout_seconds":10,
  "updated_at":"2026-09-16T12:01:00Z",
  "version":4
}`
	checkBody = `{
  "completed_at":"2026-09-16T12:01:01Z",
  "effective_url":"https://status.example.test/health",
  "failure_reason":"unexpected_status",
  "id":"` + checkID + `",
  "outcome":"failure",
  "response_time_milliseconds":321,
  "scheduled_for":"2026-09-16T12:01:00Z",
  "sequence":9,
  "started_at":"2026-09-16T12:01:00Z",
  "status_code":503,
  "trigger":"scheduled"
}`
	incidentBody = `{
  "began_at":"2026-09-16T12:01:00Z",
  "first_failure_check_id":"` + checkID + `",
  "first_failure_sequence":9,
  "id":"` + incidentID + `",
  "last_observed_at":"2026-09-16T12:03:00Z",
  "latest_failure_check_id":"` + checkID + `",
  "latest_failure_sequence":11,
  "latest_reason":"unexpected_status",
  "opened_at":"2026-09-16T12:03:00Z",
  "opening_check_id":"` + checkID + `",
  "opening_sequence":11,
  "original_reason":"unexpected_status",
  "resolution_check_id":null,
  "resolution_sequence":null,
  "resolved_at":null
}`
)

func TestMonitorPurposeIsTrimmedAndBoundedBeforeRequest(t *testing.T) {
	value := "  " + strings.Repeat("✓", 240) + "  "
	if err := normalizeMonitorPurpose(&value); err != nil {
		t.Fatal(err)
	}
	if value != strings.Repeat("✓", 240) {
		t.Fatalf("purpose was not trimmed: %q", value)
	}
	tooLong := value + "x"
	if err := normalizeMonitorPurpose(&tooLong); err == nil {
		t.Fatal("expected a length error")
	}
}

func TestMonitorCommandsCoverTheGeneratedContractAndKeepInputsSecret(t *testing.T) {
	const secret = "Bearer monitor-input-secret"
	calls := map[string]int{}
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.Header.Get("Authorization") != "Bearer management-token" {
			t.Fatalf("authorization=%q", request.Header.Get("Authorization"))
		}
		writer.Header().Set("Content-Type", "application/json")
		path := request.URL.Path
		calls[request.Method+" "+path]++
		switch {
		case request.Method == http.MethodPost && path == "/api/projects/public-site/http-monitors":
			assertBodyContainsSecret(t, request, secret, 0)
			writer.WriteHeader(http.StatusCreated)
			_, _ = io.WriteString(writer, monitorBody)
		case request.Method == http.MethodGet && path == "/api/projects/public-site/http-monitors":
			_, _ = io.WriteString(writer, "["+monitorBody+"]")
		case request.Method == http.MethodGet && path == "/api/projects/public-site/http-monitors/homepage":
			_, _ = io.WriteString(writer, monitorBody)
		case request.Method == http.MethodPut && path == "/api/projects/public-site/http-monitors/homepage":
			assertBodyContainsSecret(t, request, "target-input-secret", 4)
			_, _ = io.WriteString(writer, monitorBody)
		case request.Method == http.MethodDelete && path == "/api/projects/public-site/http-monitors/homepage":
			assertQuery(t, request, "version", "4")
			_, _ = io.WriteString(writer, monitorBody)
		case request.Method == http.MethodPost && path == "/api/projects/public-site/http-monitors/homepage/pause":
			assertJSONBody(t, request, map[string]any{"version": float64(4)})
			_, _ = io.WriteString(writer, monitorBody)
		case request.Method == http.MethodPost && path == "/api/projects/public-site/http-monitors/homepage/resume":
			assertJSONBody(t, request, map[string]any{"version": float64(4)})
			_, _ = io.WriteString(writer, monitorBody)
		case request.Method == http.MethodPost && path == "/api/projects/public-site/http-monitors/homepage/test":
			_, _ = io.WriteString(writer, successfulTestBody())
		case request.Method == http.MethodGet && path == "/api/projects/public-site/http-monitors/homepage/checks":
			if request.URL.Query().Get("limit") == "2" {
				assertQuery(t, request, "before_sequence", "10")
				_, _ = io.WriteString(writer, `{"items":[`+checkBody+`],"next_before_sequence":9}`)
			} else {
				assertQuery(t, request, "limit", "100")
				_, _ = io.WriteString(writer, `{"items":[`+checkBody+`],"next_before_sequence":null}`)
			}
		case request.Method == http.MethodGet && path == "/api/projects/public-site/http-monitors/homepage/incidents":
			assertQuery(t, request, "before_opening_sequence", "12")
			assertQuery(t, request, "limit", "2")
			_, _ = io.WriteString(writer, `{"items":[`+incidentBody+`],"next_before_opening_sequence":11}`)
		case request.Method == http.MethodPut && path == "/api/projects/public-site/http-monitors/homepage/headers/X-Api-Key":
			assertBodyContainsSecret(t, request, "header-input-secret", 4)
			_, _ = io.WriteString(writer, monitorBody)
		case request.Method == http.MethodDelete && path == "/api/projects/public-site/http-monitors/homepage/headers/X-Api-Key":
			assertQuery(t, request, "version", "4")
			_, _ = io.WriteString(writer, monitorBody)
		default:
			t.Fatalf("asked %s %s", request.Method, request.URL.RequestURI())
		}
	}))
	defer instance.Close()

	updatePath := filepath.Join(t.TempDir(), "monitor.json")
	update := `{"expected_status_code":200,"failure_threshold":3,"instruction":"Inspect","interval_seconds":60,"name":"Homepage","runbook_url":null,"target_url":"https://status.example.test/health?token=target-input-secret","text_condition":"contains","text_fragment":"ready","timeout_seconds":10,"version":4}`
	if err := os.WriteFile(updatePath, []byte(update), 0o600); err != nil {
		t.Fatal(err)
	}

	create := `{"expected_status_code":200,"failure_threshold":3,"headers":[{"name":"Authorization","value":"` + secret + `"}],"instruction":"Inspect","interval_seconds":60,"key":"homepage","name":"Homepage","runbook_url":null,"target_url":"https://status.example.test/health","text_condition":"contains","text_fragment":"ready","timeout_seconds":10}`
	header := `{"value":"header-input-secret","version":4}`
	cases := []struct {
		input string
		args  []string
		want  string
	}{
		{create, []string{"monitor", "create", "public-site", "--file", "-"}, "public-site/homepage"},
		{"", []string{"monitor", "list", "public-site"}, "last_failure=unexpected_status/status=503/duration_ms=321"},
		{"", []string{"monitor", "get", "public-site", "homepage", "--json"}, `"latest_check"`},
		{"", []string{"monitor", "update", "public-site", "homepage", "--file", updatePath}, "public-site/homepage"},
		{"", []string{"monitor", "delete", "public-site", "homepage", "--version", "4"}, "public-site/homepage"},
		{"", []string{"monitor", "pause", "public-site", "homepage", "--version", "4"}, "failing"},
		{"", []string{"monitor", "resume", "public-site", "homepage", "--version", "4"}, "failing"},
		{"", []string{"monitor", "test", "public-site", "homepage"}, "status=200"},
		{"", []string{"monitor", "checks", "public-site", "homepage", "--before-sequence", "10", "--limit", "2"}, "unexpected_status"},
		{"", []string{"monitor", "incidents", "public-site", "homepage", "--before-opening-sequence", "12", "--limit", "2"}, "original=unexpected_status"},
		{header, []string{"monitor", "header", "set", "public-site", "homepage", "X-Api-Key", "--file", "-", "--json"}, `"name":"Authorization"`},
		{"", []string{"monitor", "header", "remove", "public-site", "homepage", "X-Api-Key", "--version", "4"}, "public-site/homepage"},
	}
	for _, item := range cases {
		arguments := append(item.args, "--url", instance.URL, "--credential", "management-token")
		code, output, diagnostics := runWithInput(t, item.input, arguments...)
		if code != process.Success || diagnostics != "" || !strings.Contains(output, item.want) {
			t.Fatalf("args=%v code=%d stdout=%q stderr=%q", arguments, code, output, diagnostics)
		}
		for _, forbidden := range []string{secret, "target-input-secret", "header-input-secret", "management-token"} {
			if strings.Contains(output, forbidden) || strings.Contains(diagnostics, forbidden) {
				t.Fatalf("args=%v exposed %q in stdout=%q stderr=%q", arguments, forbidden, output, diagnostics)
			}
		}
	}

	for _, operation := range []string{
		"POST /api/projects/public-site/http-monitors",
		"GET /api/projects/public-site/http-monitors",
		"GET /api/projects/public-site/http-monitors/homepage",
		"PUT /api/projects/public-site/http-monitors/homepage",
		"DELETE /api/projects/public-site/http-monitors/homepage",
		"POST /api/projects/public-site/http-monitors/homepage/pause",
		"POST /api/projects/public-site/http-monitors/homepage/resume",
		"POST /api/projects/public-site/http-monitors/homepage/test",
		"GET /api/projects/public-site/http-monitors/homepage/checks",
		"GET /api/projects/public-site/http-monitors/homepage/incidents",
		"PUT /api/projects/public-site/http-monitors/homepage/headers/X-Api-Key",
		"DELETE /api/projects/public-site/http-monitors/homepage/headers/X-Api-Key",
	} {
		if calls[operation] == 0 {
			t.Errorf("operation not called: %s", operation)
		}
	}
}

func TestMonitorFailedImmediateCheckHasStructuredOutputAndDistinctExitCode(t *testing.T) {
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
		writer.Header().Set("Content-Type", "application/json")
		_, _ = io.WriteString(writer, failedTestBody())
	}))
	defer instance.Close()

	code, output, diagnostics := runWithInput(
		t,
		"",
		"monitor", "test", "public-site", "homepage",
		"--url", instance.URL,
		"--credential", "not-returned",
		"--json")
	if code != process.CheckFailed || !strings.Contains(diagnostics, `"code":"check_failed"`) {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
	var result map[string]any
	if err := json.Unmarshal([]byte(output), &result); err != nil {
		t.Fatal(err)
	}
	if result["succeeded"] != false || result["status_code"] != float64(503) ||
		result["response_time_milliseconds"] != float64(321) || result["reason_code"] != "unexpected_status" {
		t.Fatalf("result=%v", result)
	}
	if strings.Contains(output+diagnostics, "not-returned") {
		t.Fatalf("secret exposed in stdout=%q stderr=%q", output, diagnostics)
	}
}

func TestMonitorInputAndProblemsAreRedactedAndClassified(t *testing.T) {
	const secret = "malformed-secret-value"
	code, output, diagnostics := runWithInput(t, `{"value":"`+secret+`"`, "monitor", "header", "set", "p", "m", "X-Key", "--file", "-")
	if code != process.Usage || output != "" || strings.Contains(diagnostics, secret) {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}

	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
		writer.Header().Set("Content-Type", "application/problem+json")
		writer.WriteHeader(http.StatusConflict)
		_, _ = io.WriteString(writer, `{"code":"conflict","title":"remote-secret-value","status":409}`)
	}))
	defer instance.Close()
	code, output, diagnostics = runWithInput(
		t,
		"",
		"monitor", "get", "p", "m",
		"--url", instance.URL,
		"--credential", "credential-secret")
	if code != process.Refused || output != "" || !strings.Contains(diagnostics, "conflict") ||
		strings.Contains(diagnostics, "remote-secret-value") || strings.Contains(diagnostics, "credential-secret") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}

func TestMonitorArgumentsAreValidatedBeforeNetworkAccess(t *testing.T) {
	cases := [][]string{
		{"monitor", "create", "project"},
		{"monitor", "update", "project", "monitor"},
		{"monitor", "delete", "project", "monitor", "--version", "0"},
		{"monitor", "pause", "project", "monitor", "--version", "-1"},
		{"monitor", "header", "remove", "project", "monitor", "X-Key"},
		{"monitor", "checks", "project", "monitor", "--limit", "101"},
		{"monitor", "incidents", "project", "monitor", "--before-opening-sequence", "-1"},
	}
	for _, arguments := range cases {
		code, output, diagnostics := runWithInput(t, "", arguments...)
		if code != process.Usage || output != "" || diagnostics == "" {
			t.Fatalf("args=%v code=%d stdout=%q stderr=%q", arguments, code, output, diagnostics)
		}
	}
}

func runWithInput(t *testing.T, input string, arguments ...string) (int, string, string) {
	t.Helper()
	var output bytes.Buffer
	var diagnostics bytes.Buffer
	code := Run(arguments, strings.NewReader(input), &output, &diagnostics, func(string) string { return "" })
	return code, output.String(), diagnostics.String()
}

func assertBodyContainsSecret(t *testing.T, request *http.Request, secret string, version int) {
	t.Helper()
	var body map[string]any
	if err := json.NewDecoder(request.Body).Decode(&body); err != nil {
		t.Fatal(err)
	}
	encoded, err := json.Marshal(body)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(encoded), secret) {
		t.Fatalf("body does not contain submitted input")
	}
	if version > 0 && body["version"] != float64(version) {
		t.Fatalf("version=%v", body["version"])
	}
}

func assertQuery(t *testing.T, request *http.Request, name string, expected string) {
	t.Helper()
	if actual := request.URL.Query().Get(name); actual != expected {
		t.Fatalf("%s=%q query=%q", name, actual, request.URL.RawQuery)
	}
}

func successfulTestBody() string {
	return fmt.Sprintf(
		`{"applied_to_current_state":true,"check_id":%q,"effective_url":"https://status.example.test/health","message":"succeeded","monitor":%s,"reason_code":null,"response_time_milliseconds":45,"status_code":200,"succeeded":true}`,
		checkID,
		monitorBody)
}

func failedTestBody() string {
	return fmt.Sprintf(
		`{"applied_to_current_state":true,"check_id":%q,"effective_url":"https://status.example.test/health","message":"status did not match","monitor":%s,"reason_code":"unexpected_status","response_time_milliseconds":321,"status_code":503,"succeeded":false}`,
		checkID,
		monitorBody)
}
