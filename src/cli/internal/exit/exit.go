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
	CheckFailed  = 5
	Unauthorized = 7
	Unreachable  = 10
)

type Error struct {
	Code        int
	MachineCode string
	HTTPStatus  int
	Err         error
}

func (failure *Error) Error() string { return failure.Err.Error() }
func (failure *Error) Unwrap() error { return failure.Err }

func New(code int, format string, arguments ...any) error {
	return &Error{Code: code, MachineCode: DefaultMachineCode(code),
		Err: fmt.Errorf(format, arguments...)}
}

func Remote(code int, machineCode string, httpStatus int) error {
	return &Error{Code: code, MachineCode: machineCode, HTTPStatus: httpStatus,
		Err: fmt.Errorf("%s (HTTP %d)", machineCode, httpStatus)}
}

func DefaultMachineCode(code int) string {
	switch code {
	case Usage:
		return "usage_error"
	case NotFound:
		return "not_found"
	case Refused:
		return "request_refused"
	case CheckFailed:
		return "check_failed"
	case Unauthorized:
		return "authentication_required"
	case Unreachable:
		return "instance_unreachable"
	default:
		return "unexpected_response"
	}
}

func DetailsOf(failure error) (int, string, int) {
	var classified *Error
	if errors.As(failure, &classified) {
		code := classified.MachineCode
		if code == "" {
			code = DefaultMachineCode(classified.Code)
		}
		return classified.Code, code, classified.HTTPStatus
	}
	return Usage, DefaultMachineCode(Usage), 0
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
