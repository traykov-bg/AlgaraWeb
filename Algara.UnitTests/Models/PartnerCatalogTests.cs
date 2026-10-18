using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Web.Controllers;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Algara.UnitTests.Models;

public class PartnerCatalogTests
{
    [Theory]
    [InlineData("0888 123 456", "+359888123456")]
    [InlineData("+359 (888) 123-456", "+359888123456")]
    [InlineData("00359.888.123.456", "+359888123456")]
    [InlineData("02 123 4567", "+35921234567")]
    [InlineData("+44 20 1234 5678", "+442012345678")]
    [InlineData("0044 20 1234 5678", "+442012345678")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void EquivalentPhoneFormatsHaveOneCanonicalValue(string? input, string? expected)
    {
        Assert.True(PartnerCatalogRules.TryPhone(input, out var phone));
        Assert.Equal(expected, phone);
        Assert.True(PartnerCatalogRules.TryPartnerPhone(input, out var partnerPhone));
        Assert.Equal(expected, partnerPhone);
    }

    [Theory]
    [InlineData("0888123456 / 0888654321")]
    [InlineData("0888123456 ext 5")]
    [InlineData("888123456")]
    [InlineData("+359+888123456")]
    [InlineData("+000000000")]
    [InlineData("+359")]
    [InlineData("+1234567890123456")]
    [InlineData("+３５９８８８１２３４５６")]
    public void InvalidOrMultiplePhonesAreRejected(string input)
    {
        Assert.False(PartnerCatalogRules.TryPhone(input, out _));
        Assert.False(PartnerCatalogRules.TryPartnerPhone(input, out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData(" 0 ")]
    public void ZeroPhoneIsAvailableForPartnersWithoutWeakeningCompanyPhoneValidation(string input)
    {
        Assert.True(PartnerCatalogRules.TryPartnerPhone(input, out var phone));
        Assert.Equal("0", phone);
        Assert.False(PartnerCatalogRules.TryPhone(input, out _));
        Assert.Empty(Validate(new PartnerForm { SiteName = "Клиент на дребно", Code = "999999", Phone = input }));
        Assert.Contains(Validate(new CompanyForm { Name = "Фирма", Phone = input }),
            e => e.MemberNames.Contains(nameof(CompanyForm.Phone)));
    }

    [Fact]
    public void NameKeyIgnoresCaseRepeatedSpacesAndCanonicalUnicodeComposition()
    {
        Assert.Equal("ИВАН ПЕТРОВ", PartnerCatalogRules.Key("  Иван\t Петров  "));
        Assert.Equal(PartnerCatalogRules.Key("Йоан"), PartnerCatalogRules.Key("И\u0306оан"));
        Assert.Null(PartnerCatalogRules.Key(" \t "));
    }

    [Theory]
    [InlineData("Иван")]
    [InlineData("Иван Петров")]
    [InlineData("Иван Георгиев Петров")]
    [InlineData("Фирма – обект Варна")]
    [InlineData("Клиент на дребно")]
    public void OneNameIsSufficientWithoutPhoneCompanyOrAddress(string name)
        => Assert.Empty(Validate(new PartnerForm { SiteName = name }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t ")]
    public void EveryPartnerRequiresOneNonblankName(string? name)
    {
        var form = new PartnerForm { SiteName = name! };
        Assert.Contains(Validate(form), e => e.MemberNames.Contains(nameof(PartnerForm.SiteName)));
    }

    [Fact]
    public void NameLimitPreservesAllThreeLegacyNameComponentsWithoutTruncation()
    {
        var fullLegacyName = string.Join(" ", Enumerable.Repeat(new string('А', 100), 3));
        Assert.Empty(Validate(new PartnerForm { SiteName = fullLegacyName }));
        Assert.Empty(Validate(new PartnerForm { SiteName = new string('Б', 400) }));
        Assert.Contains(Validate(new PartnerForm { SiteName = new string('Б', 401) }),
            e => e.MemberNames.Contains(nameof(PartnerForm.SiteName)));
    }

    [Fact]
    public void CompanyCanBeAddedChangedAndRemovedFromTheSamePartnerForm()
    {
        var form = new PartnerForm { N = 12, SiteName = "Обект" };
        Assert.Empty(Validate(form));
        form.CompanyN = 1;
        Assert.Empty(Validate(form));
        form.CompanyN = 2;
        Assert.Empty(Validate(form));
        form.CompanyN = null;
        Assert.Empty(Validate(form));
    }

    [Fact]
    public void UnifiedDisplayNameDoesNotResurrectLegacyPersonFields()
    {
        var partner = new Partner
        {
            Kind = PartnerKind.Person, SiteName = "Иван Петров – Варна",
            FirstName = "Иван", MiddleName = "Георгиев", LastName = "Петров"
        };
        Assert.Equal(partner.SiteName, partner.DisplayName);
        partner.CompanyN = 5;
        Assert.Equal(partner.SiteName, partner.DisplayName);
    }

    [Theory]
    [InlineData("000001", true)]
    [InlineData("123", true)]
    [InlineData("", false)]
    [InlineData("12A", false)]
    [InlineData("１２３", false)]
    [InlineData("1 23", false)]
    public void CodesPreserveLeadingZeroesAndAcceptOnlyCurrentNumericFormat(string code, bool valid)
        => Assert.Equal(valid, PartnerCatalogRules.IsCode(code));

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 3, true)]
    [InlineData(2, 3, true)]
    [InlineData(3, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(2, 4, false)]
    [InlineData(0, 4, true)]
    [InlineData(-1, 0, false)]
    public void MovingGroupsCannotIntroduceSelfOrIndirectCycles(int group, int? parent, bool cycle)
        => Assert.Equal(cycle, PartnerCatalogRules.WouldCreateCycle(Groups(), group, parent));

    [Fact]
    public void DescendantScopeDoesNotIncludeSiblingsOrUnrelatedBranches()
        => Assert.Equal(new[] { 2, 3 }, PartnerCatalogRules.Descendants(Groups(), 2).Order());

    [Fact]
    public void RootDescendantsIncludeEveryBranchAndTheRootItself()
        => Assert.Equal(new[] { 0, 1, 2, 3, 4 }, PartnerCatalogRules.Descendants(Groups(), 0).Order());

    [Fact]
    public void ExistingCorruptCycleCannotHangTreeChecks()
    {
        var groups = Groups();
        groups.Single(g => g.N == 1).ParentN = 3;
        Assert.True(PartnerCatalogRules.WouldCreateCycle(groups, 4, 2));
        Assert.Equal(3, PartnerCatalogRules.Descendants(groups, 1).Count);
    }

    [Fact]
    public void DefaultCatalogScopeIsOnlyTheRootGroup()
    {
        var form = new PartnerForm();
        var model = new PartnerListModel();
        Assert.Equal(0, form.GroupN);
        Assert.Equal(0, new Partner().GroupN);
        Assert.Equal(0, model.GroupN);
        Assert.False(model.All);
        Assert.False(model.IncludeChildren);
        var action = typeof(AdminController).GetMethod(nameof(AdminController.Partners))!;
        Assert.Equal(0, action.GetParameters().Single(p => p.Name == "groupN").DefaultValue);
        Assert.Equal(false, action.GetParameters().Single(p => p.Name == "includeChildren").DefaultValue);
        Assert.Equal(false, action.GetParameters().Single(p => p.Name == "all").DefaultValue);
    }

    [Fact]
    public void SchemaGuaranteesGlobalNameAndCodeUniquenessIncludingInactiveRows()
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var partner = model.FindEntityType(typeof(Partner))!;
        var identity = partner.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(Partner.NormalizedSiteName));
        Assert.True(identity.IsUnique);
        Assert.Null(identity.GetFilter());
        Assert.DoesNotContain(partner.GetIndexes(), i => i.GetDatabaseName() is "IX_Partners_PersonIdentity" or "IX_Partners_CompanySite");
        Assert.DoesNotContain(partner.GetForeignKeys(), fk => fk.PrincipalEntityType.GetTableName() == "Users");
        Assert.True(partner.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == "Code").IsUnique);
        Assert.False(partner.FindProperty(nameof(Partner.SiteName))!.IsNullable);
        Assert.False(partner.FindProperty(nameof(Partner.NormalizedSiteName))!.IsNullable);
        Assert.Equal(400, partner.FindProperty(nameof(Partner.SiteName))!.GetMaxLength());
        Assert.Equal(400, partner.FindProperty(nameof(Partner.NormalizedSiteName))!.GetMaxLength());
        Assert.True(partner.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.True(model.FindEntityType(typeof(Company))!.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.True(model.FindEntityType(typeof(PartnerGroup))!.FindProperty("RowVersion")!.IsConcurrencyToken);
    }

    [Fact]
    public void EveryPartnerBelongsToAGroupButCompanyAndPhoneRemainOptional()
    {
        using var db = Context();
        var partner = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Partner))!;
        Assert.False(partner.FindProperty(nameof(Partner.GroupN))!.IsNullable);
        Assert.True(partner.GetForeignKeys().Single(f => f.Properties.Any(p => p.Name == nameof(Partner.GroupN))).IsRequired);
        Assert.True(partner.FindProperty(nameof(Partner.CompanyN))!.IsNullable);
        Assert.False(partner.GetForeignKeys().Single(f => f.Properties.Any(p => p.Name == nameof(Partner.CompanyN))).IsRequired);
        Assert.True(partner.FindProperty(nameof(Partner.Phone))!.IsNullable);
    }

    [Fact]
    public void EveryNewWriteRequiresStaffPermissionAndAntiforgeryAndEditsTakeRouteIdentity()
    {
        Assert.Equal("Staff", typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        var names = new[] { "PartnerCreate", "PartnerEdit", "CompanyCreate", "CompanyEdit", "PartnerGroupCreate", "PartnerGroupEdit" };
        var methods = typeof(AdminController).GetMethods().Where(m => names.Contains(m.Name) && m.GetCustomAttribute<HttpPostAttribute>() != null).ToArray();
        Assert.Equal(6, methods.Length);
        foreach (var method in methods)
        {
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            var permissions = method.GetCustomAttributes<Algara.Web.Authorization.RequirePermissionAttribute>().SelectMany(a => a.Permissions).ToArray();
            var module = method.Name.StartsWith("PartnerGroup", StringComparison.Ordinal) ? "PartnerGroups"
                : method.Name.StartsWith("Company", StringComparison.Ordinal) ? "Companies" : "Partners";
            Assert.Contains(module + ".View", permissions);
            Assert.Contains(module + (method.Name.EndsWith("Edit", StringComparison.Ordinal) ? ".Edit" : ".Create"), permissions);
            if (method.Name.EndsWith("Edit", StringComparison.Ordinal))
                Assert.NotNull(method.GetParameters().Single(p => p.Name == "n").GetCustomAttribute<FromRouteAttribute>());
        }
    }

    private static List<ValidationResult> Validate(object form)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), errors, true);
        return errors;
    }

    private static List<PartnerGroup> Groups() =>
    [
        new() { N = 0 }, new() { N = 1, ParentN = 0 }, new() { N = 2, ParentN = 1 },
        new() { N = 3, ParentN = 2 }, new() { N = 4, ParentN = 0 }
    ];

    private static ShopDbContext Context() => new(new DbContextOptionsBuilder<ShopDbContext>()
        .UseSqlServer("Server=(local);Database=NeverOpenedPartnerUnitTests;Integrated Security=True;TrustServerCertificate=True").Options);
}
