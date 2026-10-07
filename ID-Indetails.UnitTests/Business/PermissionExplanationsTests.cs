using System.Reflection;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PermissionExplanationsTests
{
    [Fact]
    public void Every_permission_says_what_it_lets_someone_do()
    {
        var codes = typeof(PermissionCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        codes.Count.ShouldBe(54);
        PermissionExplanations.All.Keys.ShouldBe(codes, ignoreOrder: true);
        PermissionExplanations.All.Values.ShouldAllBe(text => text.Length > 20 && text.EndsWith('.'));
        PermissionExplanations.Of("nothing:like_this").ShouldBeEmpty();
    }
}
