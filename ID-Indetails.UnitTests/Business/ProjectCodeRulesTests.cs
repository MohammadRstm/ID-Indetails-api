using ID_Indetails.api.Business.Projects;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectCodeRulesTests
{
    [Fact]
    public void A_project_code_is_ID_the_year_the_quarter_and_three_digits()
    {
        ProjectCodeRules.IsProjectCode("ID26Q4001").ShouldBeTrue();
        ProjectCodeRules.IsProjectCode(" id26q4001 ").ShouldBeTrue();
        ProjectCodeRules.IsProjectCode("ID26Q5001").ShouldBeFalse();
        ProjectCodeRules.IsProjectCode("ID26Q401").ShouldBeFalse();
        ProjectCodeRules.IsProjectCode("ID26Q40011").ShouldBeFalse();
        ProjectCodeRules.IsProjectCode("ID2026Q4001").ShouldBeFalse();
        ProjectCodeRules.IsProjectCode("ID26Q4001-VK").ShouldBeFalse();
        ProjectCodeRules.IsProjectCode(null).ShouldBeFalse();
        ProjectCodeRules.Clean(" id26q4001 ").ShouldBe("ID26Q4001");
    }

    [Fact]
    public void A_building_code_is_one_to_ten_letters_or_digits()
    {
        ProjectCodeRules.IsBuildingCode("VK").ShouldBeTrue();
        ProjectCodeRules.IsBuildingCode("m2c").ShouldBeTrue();
        ProjectCodeRules.IsBuildingCode("HOF3NTBAEW").ShouldBeTrue();
        ProjectCodeRules.IsBuildingCode("HOF3NTBAEWL").ShouldBeFalse();
        ProjectCodeRules.IsBuildingCode("V-K").ShouldBeFalse();
        ProjectCodeRules.IsBuildingCode("É").ShouldBeFalse();
        ProjectCodeRules.IsBuildingCode(" ").ShouldBeFalse();
    }

    [Fact]
    public void The_next_code_is_one_past_the_highest_of_the_quarter_it_is_in()
    {
        var autumn = new DateOnly(2026, 11, 16);
        ProjectCodeRules.PeriodOf(autumn).ShouldBe("ID26Q4");
        ProjectCodeRules.PeriodOf(new DateOnly(2026, 1, 1)).ShouldBe("ID26Q1");
        ProjectCodeRules.PeriodOf(new DateOnly(2030, 6, 30)).ShouldBe("ID30Q2");

        ProjectCodeRules.Next(autumn, []).ShouldBe("ID26Q4001");
        // a gap left by an edited code stays, and codes of other quarters or odd ones don't count
        ProjectCodeRules.Next(autumn, ["ID26Q4001", "ID26Q4007", "ID26Q3050", "ID25Q4090", "ID26Q4X"]).ShouldBe("ID26Q4008");
    }

    [Fact]
    public void The_building_code_suggested_is_the_initials_of_the_name()
    {
        ProjectCodeRules.BuildingCodeOf("Villa Kadmous").ShouldBe("VK");
        ProjectCodeRules.BuildingCodeOf("Mila 2 Concrete").ShouldBe("M2C");
        ProjectCodeRules.BuildingCodeOf("Rawda Residences, Block B").ShouldBe("RRBB");
        ProjectCodeRules.BuildingCodeOf("Abi-Nader villa (phase 12)").ShouldBe("ANVP12");
        ProjectCodeRules.BuildingCodeOf("Test(1)").ShouldBe("T1");
        // small words are skipped, but "A" is a block's name as often as a word
        ProjectCodeRules.BuildingCodeOf("The house of the Khoury family").ShouldBe("HKF");
        ProjectCodeRules.BuildingCodeOf("Beit el Hana, Block A").ShouldBe("BHBA");
        ProjectCodeRules.BuildingCodeOf("Élysée tower").ShouldBe("ET");
        ProjectCodeRules.BuildingCodeOf("Harbour Offices, floor 3, new tower, block A, east wing").ShouldBe("HOF3NTBAEW");
        ProjectCodeRules.BuildingCodeOf("مشروع").ShouldBe("X");
        ProjectCodeRules.BuildingCodeOf("  ").ShouldBe("X");
    }

    [Fact]
    public void The_full_code_joins_them_with_a_dash()
        => ProjectCodeRules.Full("ID26Q4001", "VK").ShouldBe("ID26Q4001-VK");
}
