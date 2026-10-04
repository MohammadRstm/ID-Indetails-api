using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.LoadTests.Tool.Safety;
using ID_Indetails.LoadTests.Tool.Seeding;
using Shouldly;

namespace ID_Indetails.LoadTests.Tool.Tests;

public class SeedingTests
{
    [Fact]
    public void Two_hundred_users_are_the_agreed_mix()
    {
        var plan = new SeedPlan(200, 3, 42, new DateOnly(2026, 10, 4));

        (plan.SuperAdmins, plan.DesignAdmins, plan.ExecutionAdmins, plan.DesignEmployees, plan.ExecutionEmployees)
            .ShouldBe((4, 8, 8, 108, 72));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(57)]
    [InlineData(1000)]
    public void Every_size_has_someone_in_every_role_and_adds_up(int users)
    {
        var plan = new SeedPlan(users, 3, 42, new DateOnly(2026, 10, 4));

        new[] { plan.SuperAdmins, plan.DesignAdmins, plan.ExecutionAdmins, plan.DesignEmployees, plan.ExecutionEmployees }
            .ShouldAllBe(count => count >= 1);
        (plan.SuperAdmins + plan.DesignAdmins + plan.ExecutionAdmins + plan.DesignEmployees + plan.ExecutionEmployees).ShouldBe(users);
    }

    [Fact]
    public void The_history_covers_whole_months_and_ends_the_day_before_the_anchor()
    {
        var plan = new SeedPlan(200, 3, 42, new DateOnly(2026, 10, 4));

        plan.FirstDay.ShouldBe(new DateOnly(2026, 7, 1));
        plan.LastDay.ShouldBe(new DateOnly(2026, 10, 3));
    }

    [Theory]
    [InlineData(Persona.SuperAdmin, 1, "lt.super.001@loadtest.invalid", "Super Admin 001", "super_admin", null)]
    [InlineData(Persona.DesignAdmin, 8, "lt.design.admin.008@loadtest.invalid", "Design Admin 008", "admin", "design")]
    [InlineData(Persona.ExecutionEmployee, 72, "lt.execution.employee.072@loadtest.invalid", "Execution Employee 072", "employee", "execution")]
    public void Test_users_are_named_by_persona_and_number(Persona persona, int number, string email, string lastName, string role, string? domain)
    {
        var person = new Person(persona, number);

        person.Email.ShouldBe(email);
        person.FirstName.ShouldBe("LT");
        person.LastName.ShouldBe(lastName);
        person.Role.ShouldBe(role);
        person.Domain.ShouldBe(domain);
        EmailSafety.HasDeliverableAddress(person.Email).ShouldBeFalse();
    }

    [Fact]
    public async Task The_seeding_mailbox_keeps_mail_to_test_addresses()
    {
        var mailbox = new SeedMailbox();

        await mailbox.SendAsync(Message("lt.design.employee.001@loadtest.invalid", "Join here: https://loadtest.invalid/accept-invitation?token=abc%2B1"));

        mailbox.Received.ShouldBe(1);
        mailbox.InvitationToken("lt.design.employee.001@loadtest.invalid").ShouldBe("abc+1");
    }

    [Theory]
    [InlineData("someone@gmail.com")]
    [InlineData("x@loadtest.invalid.com")]
    [InlineData("client@company.com")]
    public async Task The_seeding_mailbox_refuses_any_other_address(string address)
    {
        var mailbox = new SeedMailbox();

        await Should.ThrowAsync<InvalidOperationException>(() => mailbox.SendAsync(Message(address, "text")));
        mailbox.Received.ShouldBe(0);
    }

    [Fact]
    public void The_simulated_clock_only_moves_forward()
    {
        var clock = new SimulatedClock(new DateOnly(2026, 7, 1));
        var start = clock.UtcNow;

        clock.MoveTo(start.AddHours(-1));
        clock.UtcNow.ShouldBe(start);

        clock.MoveTo(new DateOnly(2026, 7, 1), 9);
        clock.CompanyToday.ShouldBe(new DateOnly(2026, 7, 1));
        clock.ToCompanyTime(clock.UtcNow).Hour.ShouldBe(9);
    }

    private static EmailMessage Message(string to, string text) => new(to, null, "Subject", "<p>html</p>", text);
}
