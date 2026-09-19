package cmd

import (
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
)

func TestEmailAndMaintenanceCommandsUseGeneratedOperations(t *testing.T) {
	const secret = "private-smtp-password"
	const id = "dcf2cf4b-1070-45b7-bd39-dfd12f1fb901"
	settings := `{"version":2,"security":"starttls","default_recipients":["ops@example.test"],"has_password":true}`
	recipients := `{"project_key":"systems","version":3,"recipients":["ops@example.test"]}`
	summary := `{"project_key":"systems","pending_count":1,"retrying_count":0,"terminal_failure_count":1,"smtp_accepted_count":2}`
	delivery := `{"id":"` + id + `","incident_id":"` + id + `","kind":"alert","recipient":"ops@example.test","project_key":"systems","monitor_type":"http","monitor_key":"site","state":"terminal_failure","attempt_count":5,"last_error_code":"smtp_timeout","created_at":"2026-09-19T12:00:00Z"}`
	maintenance := `{"project_key":"systems","scope_type":"project","version":1,"active_scopes":["project"],"direct_active":true,"effective_active":true}`
	calls := map[string]int{}
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.Header.Get("Authorization") != "Bearer management-token" {
			t.Errorf("missing bearer credential")
		}
		writer.Header().Set("Content-Type", "application/json")
		key := request.Method + " " + request.URL.Path
		calls[key]++
		switch key {
		case "GET /api/email/settings", "PUT /api/email/settings", "PUT /api/email/password", "DELETE /api/email/password", "PUT /api/email/default-recipients":
			if key == "PUT /api/email/password" {
				content, _ := io.ReadAll(request.Body)
				if !strings.Contains(string(content), secret) {
					t.Errorf("password was not submitted")
				}
			}
			if key == "DELETE /api/email/password" && request.URL.Query().Get("version") != "2" {
				t.Errorf("missing password version")
			}
			_, _ = io.WriteString(writer, settings)
		case "GET /api/projects/systems/recipients", "PUT /api/projects/systems/recipients":
			_, _ = io.WriteString(writer, recipients)
		case "POST /api/email/test":
			_, _ = io.WriteString(writer, `{"status":"accepted_by_smtp","accepted_at":"2026-09-19T12:00:00Z"}`)
		case "GET /api/email/deliveries":
			if request.URL.Query().Get("project_key") != "systems" || request.URL.Query().Get("limit") != "1" {
				t.Errorf("delivery filters missing: %s", request.URL.RawQuery)
			}
			_, _ = io.WriteString(writer, `{"items":[`+delivery+`],"total":1,"limit":1,"offset":0,"has_more":false}`)
		case "GET /api/email/deliveries/summary", "GET /api/projects/systems/email-summary":
			_, _ = io.WriteString(writer, summary)
		case "GET /api/email/incidents/" + id:
			if request.URL.Query().Get("monitor_type") != "http" {
				t.Errorf("missing monitor type")
			}
			_, _ = io.WriteString(writer, `{"incident_id":"`+id+`","project_key":"systems","monitor_type":"http","monitor_key":"site","open":true,"announcement_state":"failed","deliveries":[`+delivery+`]}`)
		case "GET /api/projects/systems/maintenance", "POST /api/projects/systems/maintenance", "DELETE /api/projects/systems/maintenance":
			_, _ = io.WriteString(writer, maintenance)
		case "GET /api/projects/systems/http-monitors/site/maintenance", "POST /api/projects/systems/http-monitors/site/maintenance", "DELETE /api/projects/systems/http-monitors/site/maintenance":
			_, _ = io.WriteString(writer, strings.Replace(maintenance, `"scope_type":"project"`, `"scope_type":"http"`, 1))
		case "GET /api/projects/systems/push-monitors/worker/maintenance", "POST /api/projects/systems/push-monitors/worker/maintenance", "DELETE /api/projects/systems/push-monitors/worker/maintenance":
			_, _ = io.WriteString(writer, strings.Replace(maintenance, `"scope_type":"project"`, `"scope_type":"push"`, 1))
		default:
			t.Errorf("unexpected operation %s", key)
			writer.WriteHeader(http.StatusNotFound)
		}
	}))
	defer instance.Close()

	file := filepath.Join(t.TempDir(), "settings.json")
	if err := os.WriteFile(file, []byte(`{"version":2,"host":"mail.example.test","port":587,"security":"starttls","sender_address":"notify@example.test"}`), 0o600); err != nil {
		t.Fatal(err)
	}
	cases := []struct {
		input string
		args  []string
		want  string
	}{
		{"", []string{"email", "settings", "get"}, "password_set=true"},
		{"", []string{"email", "settings", "set", "--file", file}, "version=2"},
		{`{"version":2,"password":"` + secret + `"}`, []string{"email", "password", "set", "--file", "-", "--json"}, `"has_password":true`},
		{"", []string{"email", "password", "clear", "--version", "2"}, "password_set=true"},
		{"", []string{"email", "defaults", "get"}, "ops@example.test"},
		{`{"version":2,"recipients":["ops@example.test"]}`, []string{"email", "defaults", "set", "--file", "-"}, "ops@example.test"},
		{"", []string{"email", "recipients", "get", "systems"}, "systems"},
		{`{"version":3,"recipients":["ops@example.test"]}`, []string{"email", "recipients", "set", "systems", "--file", "-"}, "version=3"},
		{"", []string{"email", "test", "--recipient", "ops@example.test"}, "accepted_by_smtp"},
		{"", []string{"email", "deliveries", "--project", "systems", "--limit", "1"}, "smtp_timeout"},
		{"", []string{"email", "deliveries", "--project", "systems", "--limit", "1", "--json"}, `"terminal_failure"`},
		{"", []string{"email", "summary"}, "failed=1"},
		{"", []string{"email", "summary", "--project", "systems", "--json"}, `"pending_count":1`},
		{"", []string{"email", "incident", id, "--monitor-type", "http"}, "failed"},
		{"", []string{"maintenance", "get", "systems", "--scope", "project"}, "direct=true"},
		{"", []string{"maintenance", "start", "systems", "--scope", "project", "--version", "0", "--duration-seconds", "600"}, "version=1"},
		{"", []string{"maintenance", "end", "systems", "--scope", "project", "--version", "1"}, "effective=true"},
		{"", []string{"maintenance", "get", "systems", "site", "--scope", "http"}, "systems/http"},
		{"", []string{"maintenance", "start", "systems", "site", "--scope", "http", "--version", "0", "--duration-seconds", "600"}, "systems/http"},
		{"", []string{"maintenance", "end", "systems", "site", "--scope", "http", "--version", "1"}, "systems/http"},
		{"", []string{"maintenance", "get", "systems", "worker", "--scope", "push"}, "systems/push"},
		{"", []string{"maintenance", "start", "systems", "worker", "--scope", "push", "--version", "0", "--duration-seconds", "600"}, "systems/push"},
		{"", []string{"maintenance", "end", "systems", "worker", "--scope", "push", "--version", "1"}, "systems/push"},
	}
	for _, item := range cases {
		args := append(item.args, "--url", instance.URL, "--credential", "management-token")
		code, stdout, stderr := runWithInput(t, item.input, args...)
		if code != process.Success || stderr != "" || !strings.Contains(stdout, item.want) {
			t.Fatalf("args=%v code=%d stdout=%q stderr=%q", args, code, stdout, stderr)
		}
		if strings.Contains(stdout+stderr, secret) || strings.Contains(stdout+stderr, "management-token") {
			t.Fatalf("secret leaked: args=%v", args)
		}
	}
	for _, key := range []string{"PUT /api/email/password", "DELETE /api/email/password", "PUT /api/email/default-recipients", "PUT /api/projects/systems/recipients", "GET /api/email/deliveries", "GET /api/email/incidents/" + id, "POST /api/projects/systems/maintenance", "POST /api/projects/systems/http-monitors/site/maintenance", "POST /api/projects/systems/push-monitors/worker/maintenance"} {
		if calls[key] == 0 {
			t.Errorf("operation not called: %s", key)
		}
	}
}

func TestEmailProblemsAndSecretInputAreSafe(t *testing.T) {
	const secret = "malformed-smtp-secret"
	code, stdout, stderr := runWithInput(t, `{"version":2,"password":"`+secret+`"`, "email", "password", "set", "--file", "-")
	if code != process.Usage || stdout != "" || strings.Contains(stderr, secret) {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, stdout, stderr)
	}
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		writer.Header().Set("Content-Type", "application/problem+json")
		if request.URL.Path == "/api/email/settings" {
			writer.WriteHeader(http.StatusUnauthorized)
		} else {
			writer.WriteHeader(http.StatusConflict)
		}
		_, _ = io.WriteString(writer, `{"code":"conflict","title":"private relay detail","status":409}`)
	}))
	defer instance.Close()
	for _, item := range []struct {
		args []string
		code int
	}{
		{[]string{"email", "settings", "get"}, process.Unauthorized},
		{[]string{"maintenance", "end", "systems", "--scope", "project", "--version", "1"}, process.Refused},
	} {
		args := append(item.args, "--url", instance.URL, "--credential", "management-token")
		code, stdout, stderr := runWithInput(t, "", args...)
		if code != item.code || stdout != "" || strings.Contains(stderr, "private relay detail") || strings.Contains(stderr, "management-token") {
			t.Fatalf("args=%v code=%d stdout=%q stderr=%q", args, code, stdout, stderr)
		}
	}
	code, stdout, stderr = runWithInput(t, "", "email", "deliveries", "--limit", "101")
	if code != process.Usage || stdout != "" {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, stdout, stderr)
	}
	code, stdout, stderr = runWithInput(t, "", "maintenance", "start", "systems", "--scope", "project", "--version", "0", "--duration-seconds", "1")
	if code != process.Usage || stdout != "" {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, stdout, stderr)
	}
}
