namespace ProspectorsDispatch;

/// <summary>How large a resource's deposits are, classified from the deposit's worldgen radius.</summary>
public enum VeinSize
{
    /// <summary>Tiny isolated pockets, such as diamond (radius about 2).</summary>
    Scattered,

    Small,

    Sizable,

    /// <summary>Big masses, such as iron (radius about 26).</summary>
    Large
}
