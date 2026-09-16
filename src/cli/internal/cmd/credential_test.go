package cmd

import (
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	process "github.com/datavisionzero/upaffe/src/cli/internal/exit"
)

const testCredentialID = "8f6ba774-6501-4c2b-b021-28af92b252c8"

var testToken = "upaffe_" + strings.ReplaceAll(testCredentialID, "-", "") + "_" + strings.Repeat("A", 43)

func TestCredentialCreateUsesBearerAndPrintsTheExplicitToken(t *testing.T) {
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.Method != http.MethodPost || request.URL.Path != "/api/management-credentials" {
			t.Fatalf("asked %s %s", request.Method, request.URL.Path)
		}
		if request.Header.Get("Authorization") != "Bearer existing-credential" {
			t.Fatalf("authorization=%q", request.Header.Get("Authorization"))
		}
		var body map[string]*string
		if err := json.NewDecoder(request.Body).Decode(&body); err != nil || body["name"] == nil || *body["name"] != "deployment" {
			t.Fatalf("body=%v err=%v", body, err)
		}
		writer.Header().Set("Content-Type", "application/json")
		writer.WriteHeader(http.StatusCreated)
		_, _ = io.WriteString(writer, `{"id":"`+testCredentialID+`","name":"deployment","token":"`+testToken+`","created_at":"2026-09-16T12:00:00Z","rotated_at":null,"previous_valid_until":null}`)
	}))
	defer instance.Close()

	code, output, diagnostics := run(
		t,
		"credential", "create",
		"--url", instance.URL,
		"--credential", "existing-credential",
		"--name", "deployment",
		"--json")
	if code != 0 || diagnostics != "" || !strings.Contains(output, testToken) {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}

func TestCredentialListNeverPrintsTheCredentialUsedForAuthentication(t *testing.T) {
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.Method != http.MethodGet || request.URL.Path != "/api/management-credentials" {
			t.Fatalf("asked %s %s", request.Method, request.URL.Path)
		}
		if request.Header.Get("Authorization") != "Bearer secret-authentication-value" {
			t.Fatalf("authorization=%q", request.Header.Get("Authorization"))
		}
		writer.Header().Set("Content-Type", "application/json")
		_, _ = io.WriteString(writer, `[{"id":"`+testCredentialID+`","name":"deployment","created_at":"2026-09-16T12:00:00Z","rotated_at":null,"revoked_at":null}]`)
	}))
	defer instance.Close()

	code, output, diagnostics := run(
		t,
		"credential", "list",
		"--url", instance.URL,
		"--credential", "secret-authentication-value")
	if code != 0 || diagnostics != "" || !strings.Contains(output, "deployment\tactive") || strings.Contains(output, "secret-authentication-value") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}

func TestCredentialRotateAndRevokeUseStableIdentifiers(t *testing.T) {
	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		writer.Header().Set("Content-Type", "application/json")
		switch {
		case request.Method == http.MethodPost && request.URL.Path == "/api/management-credentials/"+testCredentialID+"/rotate":
			_, _ = io.WriteString(writer, `{"id":"`+testCredentialID+`","name":"deployment","token":"`+testToken+`","created_at":"2026-09-16T12:00:00Z","rotated_at":"2026-09-16T13:00:00Z","previous_valid_until":"2026-09-16T13:10:00Z"}`)
		case request.Method == http.MethodDelete && request.URL.Path == "/api/management-credentials/"+testCredentialID:
			writer.WriteHeader(http.StatusNoContent)
		default:
			t.Fatalf("asked %s %s", request.Method, request.URL.Path)
		}
	}))
	defer instance.Close()

	code, output, diagnostics := run(t, "credential", "rotate", testCredentialID, "--url", instance.URL, "--credential", "existing")
	if code != 0 || diagnostics != "" || !strings.Contains(output, testToken) {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}

	code, output, diagnostics = run(t, "credential", "revoke", testCredentialID, "--url", instance.URL, "--credential", "existing", "--json")
	if code != 0 || diagnostics != "" || !strings.Contains(output, `"revoked":true`) {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}

func TestCredentialAuthenticationFailuresKeepStableCodesAndSecretsOffOutput(t *testing.T) {
	code, output, diagnostics := run(t, "credential", "list", "--url", "http://example.test")
	if code != process.Unauthorized || output != "" || !strings.Contains(diagnostics, "authentication_required") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}

	instance := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
		writer.Header().Set("Content-Type", "application/problem+json")
		writer.WriteHeader(http.StatusUnauthorized)
		_, _ = io.WriteString(writer, `{"code":"authentication_rejected","title":"secret response detail","status":401}`)
	}))
	defer instance.Close()

	code, output, diagnostics = run(
		t,
		"credential", "list",
		"--url", instance.URL,
		"--credential", "never-print-this")
	if code != process.Unauthorized || output != "" || !strings.Contains(diagnostics, "authentication_rejected") || strings.Contains(diagnostics, "secret") || strings.Contains(diagnostics, "never-print-this") {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, output, diagnostics)
	}
}
