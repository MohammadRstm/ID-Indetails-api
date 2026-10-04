using ID_Indetails.api.Authorization;
using Shouldly;

namespace ID_Indetails.UnitTests.Authorization;

public class SecurityLogTests
{
    [Fact]
    public void A_logged_path_cant_start_a_line_of_its_own()
    {
        SecurityLog.OneLine("/api/auth/login\r\n2026-10-04 warn: forged").ShouldBe("/api/auth/login2026-10-04 warn: forged");
        SecurityLog.OneLine(null).ShouldBe("");
    }
}
