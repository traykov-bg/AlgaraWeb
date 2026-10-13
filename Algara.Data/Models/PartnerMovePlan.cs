namespace Algara.Data.Models;

/// <summary>Validates a complete selection before any membership is changed.</summary>
public sealed class PartnerMovePlan
{
    public IReadOnlyList<PartnerGroup> GroupsToMove { get; }
    public IReadOnlyList<Partner> PartnersToMove { get; }
    public int IncludedGroupCount { get; }
    public int IncludedPartnerCount { get; }

    private PartnerMovePlan(List<PartnerGroup> groups, List<Partner> partners, int includedGroups, int includedPartners)
    {
        GroupsToMove = groups.AsReadOnly();
        PartnersToMove = partners.AsReadOnly();
        IncludedGroupCount = includedGroups;
        IncludedPartnerCount = includedPartners;
    }

    public static PartnerMovePlan Create(IReadOnlyCollection<PartnerGroup> groups,
        IReadOnlyCollection<Partner> selectedPartners, IReadOnlyCollection<int> selectedGroupIds, int destinationGroupN)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(selectedPartners);
        ArgumentNullException.ThrowIfNull(selectedGroupIds);

        var byId = new Dictionary<int, PartnerGroup>();
        foreach (var group in groups)
            if (!byId.TryAdd(group.N, group)) throw InvalidTree();

        if (!byId.ContainsKey(destinationGroupN))
            throw new PartnerMoveException("Целевата група вече не съществува. Обновете страницата и изберете друга група.");

        var selected = selectedGroupIds.ToHashSet();
        var partners = selectedPartners.DistinctBy(p => p.N).ToList();
        if (selected.Count == 0 && partners.Count == 0)
            throw new PartnerMoveException("Маркирайте поне една група или контрагент за преместване.");
        if (selected.Contains(0))
            throw new PartnerMoveException("Основната група не може да се премества.");
        if (selected.Any(n => !byId.ContainsKey(n)))
            throw new PartnerMoveException("Някоя от маркираните групи вече не съществува. Обновете страницата.");

        // Only paths involved in this operation are inspected. Every path must reach the sole root.
        var destinationPath = AncestorsAndSelf(destinationGroupN, byId);
        if (destinationPath.Overlaps(selected))
            throw new PartnerMoveException("Група не може да се премести в себе си или в своя подгрупа.");

        var roots = new List<PartnerGroup>();
        foreach (var n in selected.Order())
        {
            var ancestors = AncestorsAndSelf(n, byId);
            ancestors.Remove(n);
            if (!ancestors.Overlaps(selected)) roots.Add(byId[n]);
        }

        var coveredGroups = new HashSet<int>();
        foreach (var root in roots)
            coveredGroups.UnionWith(PartnerCatalogRules.Descendants(groups, root.N));

        var groupsToMove = roots.Where(g => g.ParentN != destinationGroupN).ToList();
        var partnersToMove = new List<Partner>();
        var includedPartnerCount = 0;
        foreach (var partner in partners)
        {
            AncestorsAndSelf(partner.GroupN, byId);
            if (coveredGroups.Contains(partner.GroupN)) includedPartnerCount++;
            else if (partner.GroupN != destinationGroupN) partnersToMove.Add(partner);
        }

        // A selected group already in the destination remains a sibling, and still reserves its name.
        var names = groups.Where(g => g.ParentN == destinationGroupN)
            .Select(g => g.NormalizedName).ToHashSet(StringComparer.Ordinal);
        foreach (var group in groupsToMove)
            if (!names.Add(group.NormalizedName))
                throw new PartnerMoveException($"В целевата група ще има две подгрупи с име „{group.Name}“. Преименувайте едната преди преместването.");

        if (groupsToMove.Count == 0 && partnersToMove.Count == 0)
            throw new PartnerMoveException("Маркираните записи вече са в целевата група или се съдържат в избраните групи. Изберете друга цел.");

        return new PartnerMovePlan(groupsToMove, partnersToMove, selected.Count - roots.Count, includedPartnerCount);
    }

    private static HashSet<int> AncestorsAndSelf(int n, IReadOnlyDictionary<int, PartnerGroup> groups)
    {
        var path = new HashSet<int>();
        while (true)
        {
            if (!path.Add(n) || !groups.TryGetValue(n, out var group)) throw InvalidTree();
            if (n == 0)
            {
                if (group.ParentN.HasValue) throw InvalidTree();
                return path;
            }
            if (!group.ParentN.HasValue) throw InvalidTree();
            n = group.ParentN.Value;
        }
    }

    private static PartnerMoveException InvalidTree()
        => new("Групите съдържат невалидна връзка. Обновете страницата и проверете подредбата им преди преместване.");
}

public sealed class PartnerMoveException(string message) : Exception(message);
