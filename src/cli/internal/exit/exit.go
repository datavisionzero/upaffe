package exit

import (
	"errors"
	"fmt"
)

const (
	Success      = 0
	Unexpected   = 1
	Usage        = 2
	NotFound     = 3
	Refused      = 4
	Unauthorized = 7
	Unreachable  = 10
)

type Error struct {
	Code int
	Err  error
}

func (failure *Error) Error() string { return failure.Err.Error() }
func (failure *Error) Unwrap() error { return failure.Err }

func New(code int, format string, arguments ...any) error {
	return &Error{Code: code, Err: fmt.Errorf(format, arguments...)}
}

func CodeOf(failure error) int {
	if failure == nil {
		return Success
	}
	var classified *Error
	if errors.As(failure, &classified) {
		return classified.Code
	}
	return Usage
}
