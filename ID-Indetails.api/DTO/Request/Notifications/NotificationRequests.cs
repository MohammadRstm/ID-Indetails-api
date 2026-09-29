namespace ID_Indetails.api.DTO.Request.Notifications;

public sealed record NotificationListQuery
{
    public bool UnreadOnly { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
