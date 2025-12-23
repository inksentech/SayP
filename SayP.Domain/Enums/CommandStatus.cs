namespace SayP.Domain.Enums;

public enum CommandStatus
{
    Pending = 0,
    AwaitingConfirmation = 1,
    Executing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Retrying = 6
}
