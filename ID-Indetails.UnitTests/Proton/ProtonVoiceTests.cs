using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.Proton.Prompts;
using ID_Indetails.api.Proton.Voice;
using ID_Indetails.api.Validators.Proton;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonVoiceTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

    private static ProtonVoiceSession Session(long userId, string id = "live_1")
    {
        var session = new ProtonVoiceSession(userId, Guid.NewGuid(), Now, Now.AddMinutes(10));
        session.Started(id);
        return session;
    }

    [Fact]
    public void A_person_holds_one_session_and_only_they_find_it()
    {
        var sessions = new ProtonVoiceSessions();
        var mine = Session(1);

        sessions.TryReserve(mine).ShouldBeTrue();
        sessions.TryReserve(Session(1, "live_2")).ShouldBeFalse();
        sessions.TryReserve(Session(2, "live_3")).ShouldBeTrue();

        sessions.Find(1, "live_1").ShouldBeSameAs(mine);
        sessions.Find(2, "live_1").ShouldBeNull();
        sessions.Find(1, "live_9").ShouldBeNull();

        sessions.Release(Session(1));
        sessions.Find(1, "live_1").ShouldBeSameAs(mine);
        sessions.Release(mine);
        sessions.Find(1, "live_1").ShouldBeNull();
        sessions.TryReserve(Session(1, "live_4")).ShouldBeTrue();
    }

    [Fact]
    public void A_reserved_place_without_a_provider_id_yet_is_not_found()
    {
        var sessions = new ProtonVoiceSessions();
        var reserved = new ProtonVoiceSession(1, Guid.NewGuid(), Now, Now.AddMinutes(10));
        sessions.TryReserve(reserved).ShouldBeTrue();

        sessions.Find(1, "").ShouldBeNull();
    }

    [Fact]
    public void A_spoken_question_is_taken_once_and_closing_is_signalled()
    {
        var session = Session(1);

        session.Claim("item_1").ShouldBeTrue();
        session.Claim("item_1").ShouldBeFalse();
        session.Claim("item_2").ShouldBeTrue();

        session.Closing.IsCancellationRequested.ShouldBeFalse();
        session.Close();
        session.Close();
        session.Closing.IsCancellationRequested.ShouldBeTrue();
    }

    [Theory]
    [InlineData("live_123", true)]
    [InlineData("item_9tA2bF3h7K9m2P5q8R1s4", true)]
    [InlineData("a-b_C", true)]
    [InlineData("", false)]
    [InlineData("live 1", false)]
    [InlineData("../attach", false)]
    [InlineData("live_1?x=1", false)]
    public void Provider_ids_are_short_and_plain(string id, bool valid)
        => ProtonVoiceIds.IsValid(id).ShouldBe(valid);

    [Fact]
    public void A_provider_id_has_a_length_limit()
        => ProtonVoiceIds.IsValid(new string('a', ProtonVoiceIds.MaxLength + 1)).ShouldBeFalse();

    [Fact]
    public void A_spoken_question_needs_its_conversation_text_and_no_button()
    {
        var validator = new AskProtonRequestValidator();
        var voice = new ProtonVoiceTurnRequest { SessionId = "live_1", DelegationId = "item_1" };

        validator.Validate(new AskProtonRequest { ConversationId = Guid.NewGuid(), Text = "How busy are we?", Voice = voice }).IsValid.ShouldBeTrue();
        validator.Validate(new AskProtonRequest { Text = "How busy are we?", Voice = voice }).IsValid.ShouldBeFalse();
        validator.Validate(new AskProtonRequest { ConversationId = Guid.NewGuid(), Text = "", Voice = voice }).IsValid.ShouldBeFalse();
        validator.Validate(new AskProtonRequest { ConversationId = Guid.NewGuid(), Text = "Yes", Suggestion = 0, Voice = voice }).IsValid.ShouldBeFalse();
        validator.Validate(new AskProtonRequest
        {
            ConversationId = Guid.NewGuid(), Text = "Hi", Voice = voice with { DelegationId = "x y" },
        }).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\n", true)]
    [InlineData("", false)]
    [InlineData("hello", false)]
    public void A_start_needs_a_webrtc_offer(string sdp, bool valid)
        => new StartProtonVoiceRequestValidator().Validate(new StartProtonVoiceRequest { Sdp = sdp }).IsValid.ShouldBe(valid);

    [Fact]
    public void The_voice_prompt_names_the_app_says_speech_confirms_nothing_and_is_versioned_apart_from_the_chat()
    {
        var prompt = ProtonPrompts.VoiceFor("Hourline");

        prompt.ShouldContain("Hourline");
        prompt.ShouldNotContain(ProtonPrompts.ProductToken);
        prompt.ShouldContain("does not confirm anything");
        prompt.ShouldContain("Never say a change was made");
        ProtonPrompts.VoiceVersion.Length.ShouldBe(12);
        ProtonPrompts.VoiceVersion.ShouldNotBe(ProtonPrompts.ChatVersion);
        ProtonPrompts.Chat.ShouldNotContain("You are the voice of Proton");
    }
}
