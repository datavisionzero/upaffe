namespace Upaffe.Domain.Monitoring;

public enum MonitorState
{
    Untested,
    Healthy,
    Failing,
    Paused,
}

public enum TextCondition
{
    None,
    Required,
    Forbidden,
}

public enum CheckTrigger
{
    Scheduled,
    Requested,
}

public enum CheckOutcome
{
    Success,
    Failure,
}
