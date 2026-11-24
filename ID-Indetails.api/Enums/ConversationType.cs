namespace ID_Indetails.api.Enums;

/// <summary>
/// Direct message between two people, a team's chat (<see cref="Group"/>), the one Announcements chat everyone reads,
/// or a chat group a super admin made with whoever they picked (<see cref="CustomGroup"/>).
/// </summary>
public enum ConversationType
{
    Dm,
    Group,
    Announcements,
    CustomGroup,
}
