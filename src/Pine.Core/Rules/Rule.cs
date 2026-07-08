using System;
using Pine.Core.Common;

namespace Pine.Core.Rules;

public sealed class Rule : AggregateRoot
{
    public string MatchCondition { get; private set; }
    public Guid? TargetCategoryId { get; private set; }
    public Guid? TargetTagId { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Rule() { }
#pragma warning restore CS8618

    private Rule(Guid id, string matchCondition, Guid? targetCategoryId, Guid? targetTagId) : base(id)
    {
        MatchCondition = matchCondition;
        TargetCategoryId = targetCategoryId;
        TargetTagId = targetTagId;
    }

    public static Rule Create(string matchCondition, Guid? targetCategoryId, Guid? targetTagId)
    {
        if (string.IsNullOrWhiteSpace(matchCondition))
            throw new ArgumentException("Match condition cannot be empty.", nameof(matchCondition));
            
        if (targetCategoryId == null && targetTagId == null)
            throw new ArgumentException("A rule must specify at least a target category or a target tag.");

        return new Rule(Guid.NewGuid(), matchCondition, targetCategoryId, targetTagId);
    }
}
