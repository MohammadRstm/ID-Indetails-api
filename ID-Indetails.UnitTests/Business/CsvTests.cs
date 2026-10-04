using System.Text;
using ID_Indetails.api.Business.Common;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class CsvTests
{
    [Fact]
    public void Fields_are_quoted_only_when_needed_with_a_bom_and_crlf_lines()
    {
        var bytes = Csv.Write(["Task", "Hours"], [["Logo, v2", "1.50"], ["Say \"hi\"", "0.25"], ["Two\nlines", "2.00"]]);

        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(bytes[3..]).ShouldBe(
            "Task,Hours\r\n\"Logo, v2\",1.50\r\n\"Say \"\"hi\"\"\",0.25\r\n\"Two\nlines\",2.00\r\n");
    }

    [Fact]
    public void Text_a_spreadsheet_would_run_as_a_formula_is_defused_and_numbers_stay_numbers()
    {
        var bytes = Csv.Write(["Task", "Hours"], [["=HYPERLINK(\"https://x.test/\")", "-1.50"], ["@SUM(A1)", "+2"], ["\tcmd", "0"]]);

        Encoding.UTF8.GetString(bytes[3..]).ShouldBe(
            "Task,Hours\r\n\"'=HYPERLINK(\"\"https://x.test/\"\")\",-1.50\r\n'@SUM(A1),+2\r\n'\tcmd,0\r\n");
    }
}
