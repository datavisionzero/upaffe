package main

import (
	"os"

	"github.com/datavisionzero/upaffe/src/cli/internal/cmd"
)

func main() {
	os.Exit(cmd.Run(os.Args[1:], os.Stdin, os.Stdout, os.Stderr, os.Getenv))
}
