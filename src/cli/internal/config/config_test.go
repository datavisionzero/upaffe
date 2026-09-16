package config

import "testing"

func TestResolveURL(t *testing.T) {
	t.Run("flag wins", func(t *testing.T) {
		got, err := ResolveURL("https://monitor.example.test/", func(string) string {
			return "https://ignored.example.test"
		})
		if err != nil || got != "https://monitor.example.test" {
			t.Fatalf("got %q, %v", got, err)
		}
	})

	t.Run("environment is the fallback", func(t *testing.T) {
		got, err := ResolveURL("", func(key string) string {
			if key != URLVariable {
				t.Fatalf("asked for %q", key)
			}
			return "http://127.0.0.1:8080"
		})
		if err != nil || got != "http://127.0.0.1:8080" {
			t.Fatalf("got %q, %v", got, err)
		}
	})

	for _, value := range []string{"", "localhost:8080", "ftp://example.test", "https://user:secret@example.test"} {
		t.Run("refuses "+value, func(t *testing.T) {
			if _, err := ResolveURL(value, func(string) string { return "" }); err == nil {
				t.Fatal("expected an error")
			}
		})
	}
}
