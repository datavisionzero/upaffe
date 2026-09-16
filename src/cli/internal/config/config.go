package config

import (
	"fmt"
	"net/url"
	"strings"
)

const URLVariable = "UPAFFE_URL"

type Environment func(string) string

func ResolveURL(flagValue string, environment Environment) (string, error) {
	value := strings.TrimSpace(flagValue)
	if value == "" {
		value = strings.TrimSpace(environment(URLVariable))
	}
	if value == "" {
		return "", fmt.Errorf("set --url or %s to the instance address", URLVariable)
	}

	parsed, err := url.Parse(value)
	if err != nil || parsed.Host == "" || (parsed.Scheme != "http" && parsed.Scheme != "https") {
		return "", fmt.Errorf("instance address must be an absolute http or https URL")
	}
	if parsed.User != nil {
		return "", fmt.Errorf("instance address must not contain credentials")
	}
	parsed.Path = strings.TrimRight(parsed.Path, "/")
	return strings.TrimRight(parsed.String(), "/"), nil
}
