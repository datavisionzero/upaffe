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
	if code != process.Usage || output != "" || !strings.Contains(diagnostics, "UPAFFE_URL") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}

	code, output, diagnostics = run(t, "status", "--url", "http://127.0.0.1:1")
	if code != process.Unreachable || output != "" || diagnostics == "" {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}
