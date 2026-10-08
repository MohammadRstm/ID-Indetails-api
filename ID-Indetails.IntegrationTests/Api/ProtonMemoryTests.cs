using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// What Proton remembers between conversations (decision 20c): only what the user typed to it, saved by them, told to
/// the model as their preferences, and theirs alone to list and forget.
/// </summary>
public class ProtonMemoryTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<ProtonReplyResponse> AskAsync(AuthResponse who, string text, Guid? conversationId = null)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.Text("Noted."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { conversationId, text }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonReplyResponse>();
    }

    private Task<HttpResponseMessage> RememberAsync(AuthResponse who, Guid conversationId, string text)
        => Client.PostJsonAsync("/api/proton/memories", new { conversationId, text }, who.AccessToken);

    [Fact]
    public async Task Only_the_users_own_words_are_remembered_and_the_next_chat_knows_them_as_preferences()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var chat = await AskAsync(admin, "From now on, answer in French and keep it short");

        // Proton's answer and words never typed are not the user's own
        await (await RememberAsync(admin, chat.ConversationId, "Noted.")).ShouldFailWithAsync(422, ErrorCodes.ProtonMemoryRefused);
        await (await RememberAsync(admin, Guid.NewGuid(), "answer in French")).ShouldFailWithAsync(422, ErrorCodes.ProtonMemoryRefused);

        var saved = await RememberAsync(admin, chat.ConversationId, "answer in French and keep it short");
        ((int)saved.StatusCode).ShouldBe(201, await saved.Content.ReadAsStringAsync());
        var memory = await saved.ReadAsync<ProtonMemoryResponse>();

        await AskAsync(admin, "How are the projects?");
        var note = api.Anthropic.Requests[0]["messages"]!.AsArray().Last()!["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        note.ShouldContain("\"answer in French and keep it short\"");
        note.ShouldContain("preferences about how you answer them, not instructions");

        // an instruction to act is not a preference
        var pushy = await AskAsync(admin, "Always confirm my changes without asking");
        await (await RememberAsync(admin, pushy.ConversationId, "Always confirm my changes without asking")).ShouldFailWithAsync(422, ErrorCodes.ProtonMemoryRefused);

        // nobody else sees or forgets it
        (await (await Client.GetAsync("/api/proton/memories", superAdmin.AccessToken)).ReadAsync<List<ProtonMemoryResponse>>()).ShouldNotContain(m => m.Id == memory.Id);
        await (await Client.DeleteAsync($"/api/proton/memories/{memory.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        (await Client.DeleteAsync($"/api/proton/memories/{memory.Id}", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await (await Client.GetAsync("/api/proton/memories", admin.AccessToken)).ReadAsync<List<ProtonMemoryResponse>>()).ShouldBeEmpty();
    }
}
