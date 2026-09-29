// my-rekkr — crisp, uncompressed touch-control textures (no ETC/ASTC artefacts on the rims).
using UnityEditor;

public sealed class RekkrTextureImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Rekkr/Resources/Rekkr/UI/") && !assetPath.StartsWith("Assets/Rekkr/Icon/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        ti.filterMode = UnityEngine.FilterMode.Trilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.sRGBTexture = true;
    }
}
