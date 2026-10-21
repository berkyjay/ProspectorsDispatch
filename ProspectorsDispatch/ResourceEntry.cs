namespace ProspectorsDispatch;

/// <summary>A curated resource: its deposit code, the dispatch category that covers it, and its rarity.</summary>
public sealed record ResourceEntry(string Code, ResourceCategory Category, Rarity Rarity);
