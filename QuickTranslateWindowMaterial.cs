namespace TaskbarInfo;

public enum QuickTranslateWindowMaterial
{
    Mica,
    Acrylic,
    Solid
}

public static class QuickTranslateWindowMaterialParser
{
    public static QuickTranslateWindowMaterial Parse(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "ACRYLIC" => QuickTranslateWindowMaterial.Acrylic,
            "SOLID" => QuickTranslateWindowMaterial.Solid,
            _ => QuickTranslateWindowMaterial.Mica
        };
}
