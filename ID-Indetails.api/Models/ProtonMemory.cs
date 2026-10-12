namespace ID_Indetails.api.Models;

/// <summary>
/// Something the user asked Proton to remember, in their own words: a preference about how it answers them. Saved only
/// by the user, from their own message; shown to the model as preference data, never as instructions.
/// </summary>
public class ProtonMemory
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
