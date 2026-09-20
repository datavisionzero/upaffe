package cmd

import (
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestInventoryUsesGeneratedQueryAndQuotesOperatorPurpose(t *testing.T) {
	const body = `{"generated_at":"2026-09-20T12:00:00Z","total":1,"limit":10,"offset":0,"has_more":false,"items":[{"project_key":"alpha","project_name":"Alpha","id":"4a572f67-2edb-46d3-86de-419608f16d83","type":"http","key":"site","name":"Site","purpose":"Check\nforged line","mode":null,"target_url":"https://status.example.test/health","interval_seconds":60,"tolerance_seconds":null,"state":"failing","overdue":true,"incident_open":false,"latest_observation_at":"2026-09-20T11:59:00Z","last_success_at":null,"next_due_at":"2026-09-20T11:58:00Z","maintenance_until":null}]}`
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.URL.Path != "/api/monitors" || request.URL.Query().Get("project") != "alpha" ||
			request.URL.Query().Get("type") != "http" || request.URL.Query().Get("state") != "failing" ||
			request.URL.Query().Get("q") != "site" || request.URL.Query().Get("limit") != "10" ||
			request.URL.Query().Get("offset") != "0" || request.Header.Get("Authorization") != "Bearer example-management-token" {
			t.Fatalf("unexpected request: %s", request.URL.RequestURI())
		}
		writer.Header().Set("Content-Type", "application/json")
		_, _ = io.WriteString(writer, body)
	}))
	defer instance.Close()
	args := []string{"inventory", "--project", "alpha", "--type", "http", "--state", "failing",
		"--search", "site", "--limit", "10", "--url", instance.URL,
		"--credential", "example-management-token"}
	code, output, diagnostics := run(t, args...)
	if code != 0 || diagnostics != "" || strings.Contains(output, "Check\nforged line") ||
		!strings.Contains(output, `purpose="Check\nforged line"`) ||
		!strings.Contains(output, "page\ttotal=1\tlimit=10\toffset=0\thas_more=false") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
	code, output, diagnostics = run(t, append(args, "--json")...)
	var page map[string]any
	if code != 0 || diagnostics != "" || json.Unmarshal([]byte(output), &page) != nil || page["total"] != float64(1) {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}
