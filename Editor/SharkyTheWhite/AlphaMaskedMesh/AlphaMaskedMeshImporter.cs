using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine;

namespace SharkyTheWhite.AlphaMaskedMesh
{
    [ScriptedImporter(2, MaskedImporterExtension)]
    [HelpURL("https://github.com/SharkyTheWhite/Unity_AlphaMaskedMeshImporter")]
    public class AlphaMaskedMeshImporter : ScriptedImporter
    {
        public const string MaskedImporterExtension = "stw_alphamaskedmesh";
        public static readonly Dictionary<string, List<string>> LastImportResults = new Dictionary<string, List<string>>();

        public enum MaskChannel : int
        {
            Red = 0,
            Green = 1,
            Blue = 2,
            Alpha = 3,
        }

        [Serializable]
        public class MaskSetting
        {
            [Tooltip("The mask image to use (must be imported as a texture). " +
                     "An empty mask is interpreted as \"remove nothing\" " +
                     "which can still be inverted to act as \"remove all\"")]
            public Texture2D mask;
            [Tooltip("Choose one of the mask image color channels or alpha (default) to use for masking. ")]
            public MaskChannel colorChannel = MaskChannel.Alpha;
            [Range(0, 1)]
            [Tooltip("Mesh faces with a color/alpha value below this will be removed. Default is 0.5")]
            public float threshold = 0.5f;
            [Tooltip("Remove the exact opposite parts of the mesh.")]
            public bool invert = false;
            [Range(0, 7)]
            [Tooltip("If the mesh has multiple UV maps, select the correct one here. Usually this is 0.")]
            public int uvChannel = 0;
            
        }
        
        // TODO: Add settings per Submesh AKA material slot!

        [Tooltip("The mesh which should be reduced using the mask.")]
        public Mesh originalMesh = null;

        [Tooltip("Define the masking settings for each material slot.")]
        public List<MaskSetting> maskSettings = new List<MaskSetting>();
        
        [Tooltip("Usually, when changing the face list of a mesh, the bounds are recomputed. " +
                 "This e.g. helps improve the rendering performance of your object.")]
        public bool keepOriginalBounds = false;
        [Tooltip("The optimization step (provided by Unity) is highly recommended, as it removes " +
                 "the orphaned vertices after masking. This reduces the memory your mesh takes up." +
                 "In some edge cases, this could lead to unexpected behaviour when replacing meshes in " +
                 "already configured game objects. Use this only when you experience issues!")]
        public bool skipMeshOptimization = false;

        [MenuItem("Assets/Create/Alpha Masked Mesh", false, 205)]
        public static void CreateAsset()
        {
            ProjectWindowUtil.CreateAssetWithContent("MyMaskedMesh."+MaskedImporterExtension, "# No Data here - all stored in asset metadata!");
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            List<string> importResults = new List<string>();
            LastImportResults[AssetDatabase.AssetPathToGUID(ctx.assetPath)] = importResults;
            
            // Add dependencies early so fixing them from an invalid state also triggers a reimport here
            if (originalMesh)
                ctx.DependsOnSourceAsset(AssetDatabase.GetAssetPath(originalMesh));
            // Depend on all sub settings in case the model changes
            foreach (MaskSetting maskSetting in maskSettings)
            {
                if (maskSetting.mask) 
                    ctx.DependsOnSourceAsset(AssetDatabase.GetAssetPath(maskSetting.mask));                
            }
            
            bool cannotContinue = false;
            if (!originalMesh || !originalMesh.isReadable)
            {
                importResults.Add($"Original mesh must be selected and readable!");
                cannotContinue = true;
            }
            else
            {
                // Check only settings which will be applied
                foreach (MaskSetting maskSetting in maskSettings.GetRange(0,
                             Math.Min(originalMesh.subMeshCount, maskSettings.Count)))
                {
                    if (maskSetting.mask)
                    {
                        string maskAssetPath = AssetDatabase.GetAssetPath(maskSetting.mask);
                        ctx.DependsOnSourceAsset(maskAssetPath);
                        if (!maskSetting.mask.isReadable)
                        {
                            importResults.Add($"Mask texture must be readable ({maskAssetPath})!");
                            cannotContinue = true;
                        }
                    }
                }
            }

            if (cannotContinue)
            {
                ctx.LogImportWarning($"{string.Join(" ", importResults)} ({ctx.assetPath})");
                return;
            }

            Mesh maskedMesh = Instantiate(originalMesh);
            
            importResults.Add($"Original total vertex count: {originalMesh.vertexCount}.");
            importResults.Add($"Number of sub meshes: {originalMesh.subMeshCount}.");

            for (int subMeshIndex = 0; subMeshIndex < originalMesh.subMeshCount; subMeshIndex++)
            {
                MeshTopology topology = maskedMesh.GetTopology(subMeshIndex);
                MaskSetting maskSetting = maskSettings.Count > subMeshIndex ? maskSettings[subMeshIndex] : null;
                Texture2D mask = maskSetting?.mask;

                int groupSize = 0;
                switch (topology)
                {
                    case MeshTopology.Points:
                    case MeshTopology.LineStrip:
                        groupSize = 1;
                        break;
                    case MeshTopology.Lines:
                        groupSize = 2;
                        break;
                    case MeshTopology.Triangles:
                        groupSize = 3;
                        break;
                    case MeshTopology.Quads:
                        groupSize = 4;
                        break;
                }

                if (groupSize > 0)
                {
                    List<int> originalFaces = new List<int>();
                    maskedMesh.GetIndices(originalFaces, subMeshIndex);

                    List<int> maskedFaces = new List<int>(originalFaces.Count);
                    int maskChannel = (int)(maskSetting?.colorChannel ?? MaskChannel.Alpha);
                    float maskThreshold = maskSetting?.threshold ?? 0.5f;
                    bool invertMask = maskSetting?.invert ?? false;
                    int uvChannel = maskSetting?.uvChannel ?? 0;

                    if (mask)
                    {
                        try
                        {
                            // Get UV coordinates
                            List<Vector2> uvs = new List<Vector2>(originalMesh.vertexCount);
                            originalMesh.GetUVs(uvChannel, uvs);

                            // Iterate over face groups
                            for (int faceOffset = 0;
                                 faceOffset < originalFaces.Count - (groupSize - 1);
                                 faceOffset += groupSize)
                            {
                                // Compute average UV coordinate (i.e. triangle center)
                                Vector2 uvSum = Vector2.zero;
                                for (int vertexIndex = 0; vertexIndex < groupSize; vertexIndex++)
                                {
                                    uvSum += uvs[originalFaces[faceOffset + vertexIndex]];
                                }
                                Vector2 centerUv = uvSum / groupSize;
                                
                                // Interpolate mask color and apply masking
                                Color c = mask.GetPixelBilinear(centerUv.x, centerUv.y);
                                if ((c[maskChannel] > maskThreshold) ^ invertMask)
                                    maskedFaces.AddRange(originalFaces.GetRange(faceOffset, groupSize));
                            }
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            string msg =
                                $"Index errors when masking sub mesh {subMeshIndex}! Is the right UV Channel selected?";
                            importResults.Add(msg);
                            ctx.LogImportError($"{msg} ({ctx.assetPath})");
                        }
                    }
                    else
                    {
                        importResults.Add($"No mask selected for sub mesh {subMeshIndex}.");
                        if (!invertMask)
                            maskedFaces = originalFaces;
                    }

                    if (topology == MeshTopology.LineStrip && maskedFaces.Count < 1)
                        maskedFaces.Clear();

                    maskedMesh.SetIndices(
                        maskedFaces, // We replace the index list with our filtered list
                        topology, // keep the same topology
                        subMeshIndex, // for this submesh
                        calculateBounds: !keepOriginalBounds, // optionally: do not change the bounds 
                        baseVertex: (int)originalMesh.GetBaseVertex(subMeshIndex) // keep base vertex
                    );

                    if (originalFaces.Count == maskedFaces.Count)
                        importResults.Add(
                            $"Submesh {subMeshIndex} unchanged: {originalFaces.Count / groupSize} {topology}.");
                    else
                        importResults.Add(
                            $"Submesh {subMeshIndex} reduced from {originalFaces.Count / groupSize} to {maskedFaces.Count / groupSize}  {topology}.");
                }
                else
                {
                    importResults.Add(
                        $"Leaving submesh {subMeshIndex} untouched since topology \"{topology}\" is not supported!");

                }
            }

            if (!skipMeshOptimization)
            {
                importResults.Add($"Total vertex count before optimization: {maskedMesh.vertexCount}.");
                importResults.Add($"Applying final optimization.");
                maskedMesh.Optimize();
            }
            importResults.Add($"Resulting mesh total vertex count: {maskedMesh.vertexCount}.");
            
            ctx.AddObjectToAsset("maskedMesh", maskedMesh);
            ctx.SetMainObject(maskedMesh);
        }
    }
}
