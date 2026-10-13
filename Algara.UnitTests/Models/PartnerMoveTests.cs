using Algara.Data.Models;

namespace Algara.UnitTests.Models;

public class PartnerMoveTests
{
    [Fact]
    public void MixedSelectionMovesOnlyTopGroupsAndIndependentPartnersWithoutMutatingAnything()
    {
        var groups = Tree();
        var covered = Partner(10, 3, active: false);
        var independent = Partner(11, 0);
        var alreadyThere = Partner(12, 4);

        var plan = PartnerMovePlan.Create(groups, [covered, independent, alreadyThere], [1, 2, 3], 4);

        Assert.Equal([1], plan.GroupsToMove.Select(g => g.N));
        Assert.Equal([11], plan.PartnersToMove.Select(p => p.N));
        Assert.Equal(2, plan.IncludedGroupCount);
        Assert.Equal(1, plan.IncludedPartnerCount);
        Assert.Equal(0, groups.Single(g => g.N == 1).ParentN);
        Assert.Equal(1, groups.Single(g => g.N == 2).ParentN);
        Assert.Equal(3, covered.GroupN);
        Assert.Equal(0, independent.GroupN);
        Assert.Same(independent, Assert.Single(plan.PartnersToMove));
    }

    [Fact]
    public void PartnerInAnUnselectedDescendantMovesWithTheSelectedAncestor()
    {
        var plan = PartnerMovePlan.Create(Tree(), [Partner(10, 3)], [1], 4);

        Assert.Empty(plan.PartnersToMove);
        Assert.Equal(1, plan.IncludedPartnerCount);
        Assert.Equal(0, plan.IncludedGroupCount);
    }

    [Fact]
    public void SelectingSiblingsRetainsBothAsMoveRoots()
    {
        var groups = Tree();
        groups.Add(Group(5, 1, "Друга подгрупа"));

        var plan = PartnerMovePlan.Create(groups, [], [2, 5], 4);

        Assert.Equal([2, 5], plan.GroupsToMove.Select(g => g.N));
        Assert.Equal(0, plan.IncludedGroupCount);
    }

    [Fact]
    public void RootCanBeDestination()
    {
        var plan = PartnerMovePlan.Create(Tree(), [Partner(10, 4)], [2], 0);

        Assert.Equal([2], plan.GroupsToMove.Select(g => g.N));
        Assert.Equal([10], plan.PartnersToMove.Select(p => p.N));
    }

    [Fact]
    public void RootCannotBeSelectedEvenIfAnotherSelectedItemCouldMove()
        => Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(Tree(), [Partner(10, 1)], [0], 4));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MovingGroupToItselfOrAnyDescendantIsRejected(int destination)
        => Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(Tree(), [], [1], destination));

    [Fact]
    public void InvalidDestinationIsRejectedBeforeAnyChange()
        => Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(Tree(), [Partner(10, 0)], [1], 999));

    [Fact]
    public void MissingSelectedGroupRejectsWholeRequest()
        => Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(Tree(), [Partner(10, 0)], [1, 999], 4));

    [Fact]
    public void ExistingTargetSiblingNameCollisionRejectsWholeRequest()
    {
        var groups = Tree();
        groups.Add(Group(5, 4, "\t ИЗТОЧНИК  "));
        var partner = Partner(10, 0);

        var error = Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(groups, [partner], [1], 4));

        Assert.Contains("Източник", error.Message);
        Assert.Equal(0, partner.GroupN);
        Assert.Equal(0, groups.Single(g => g.N == 1).ParentN);
    }

    [Fact]
    public void TwoSelectedGroupsWithSameNameCannotBecomeSiblings()
    {
        var groups = Tree();
        groups.Add(Group(5, 0, "  ПОДГРУПА "));

        Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(groups, [], [2, 5], 4));
    }

    [Fact]
    public void SelectedNoOpGroupStillReservesItsNameAtDestination()
    {
        var groups = Tree();
        groups.Add(Group(5, 4, "Източник"));

        Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(groups, [], [1, 5], 4));
    }

    [Fact]
    public void DescendantWithSameNameDoesNotCollideWhenItsParentMoves()
    {
        var groups = Tree();
        groups.Single(g => g.N == 2).Name = "Източник";
        groups.Single(g => g.N == 2).NormalizedName = "ИЗТОЧНИК";

        var plan = PartnerMovePlan.Create(groups, [], [1, 2], 4);

        Assert.Equal([1], plan.GroupsToMove.Select(g => g.N));
        Assert.Equal(1, plan.IncludedGroupCount);
    }

    [Fact]
    public void SameNameInAnUnrelatedGroupIsAllowed()
    {
        var groups = Tree();
        groups.Add(Group(5, 2, "Източник"));

        Assert.Single(PartnerMovePlan.Create(groups, [], [1], 4).GroupsToMove);
    }

    [Fact]
    public void AlreadyPlacedEntriesAreExcludedWhileRemainingSelectionsMove()
    {
        var groups = Tree();
        groups.Add(Group(5, 4, "Вече преместена"));

        var plan = PartnerMovePlan.Create(groups, [Partner(10, 4), Partner(11, 0)], [1, 5], 4);

        Assert.Equal([1], plan.GroupsToMove.Select(g => g.N));
        Assert.Equal([11], plan.PartnersToMove.Select(p => p.N));
        Assert.Equal(0, plan.IncludedGroupCount);
        Assert.Equal(0, plan.IncludedPartnerCount);
    }

    [Fact]
    public void EmptySelectionIsRejected()
        => Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(Tree(), [], [], 4));

    [Fact]
    public void EntirelyUnchangedSelectionIsRejected()
        => Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(Tree(), [Partner(10, 0), Partner(11, 3)], [1, 2], 0));

    [Fact]
    public void RepeatedIdsDoNotDuplicateUpdatesOrIncludedCounts()
    {
        var partner = Partner(10, 0);
        var plan = PartnerMovePlan.Create(Tree(), [partner, partner], [1, 1, 2, 2], 4);

        Assert.Single(plan.GroupsToMove);
        Assert.Single(plan.PartnersToMove);
        Assert.Equal(1, plan.IncludedGroupCount);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 4)]
    [InlineData(4, 999)]
    [InlineData(4, null)]
    [InlineData(0, 4)]
    public void InvalidAncestryIsRejectedWithoutHanging(int groupN, int? parentN)
    {
        var groups = Tree();
        groups.Single(g => g.N == groupN).ParentN = parentN;

        Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(groups, [], [1], 4));
    }

    [Fact]
    public void SelectedPartnerWithMissingGroupRejectsWholeRequest()
        => Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(Tree(), [Partner(10, 999)], [1], 4));

    [Fact]
    public void DuplicateGroupIdsInTreeAreRejected()
    {
        var groups = Tree();
        groups.Add(Group(1, 0, "Дубликат"));

        Assert.Throws<PartnerMoveException>(() => PartnerMovePlan.Create(groups, [], [1], 4));
    }

    private static List<PartnerGroup> Tree()
        => [Group(0, null, "Основна група"), Group(1, 0, "Източник"), Group(2, 1, "Подгрупа"),
            Group(3, 2, "Трето ниво"), Group(4, 0, "Цел")];

    private static PartnerGroup Group(int n, int? parentN, string name)
        => new() { N = n, ParentN = parentN, Name = name, NormalizedName = PartnerCatalogRules.Key(name)! };

    private static Partner Partner(int n, int groupN, bool active = true)
        => new() { N = n, GroupN = groupN, SiteName = $"Контрагент {n}", IsActive = active };
}
