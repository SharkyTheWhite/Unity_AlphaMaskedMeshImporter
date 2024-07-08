using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine;

namespace SharkyTheWhite.AlphaMaskedMesh
{
    [CustomEditor(typeof(AlphaMaskedMeshImporter))]
    public class AlphaMaskedMeshImporterEditor : ScriptedImporterEditor
    {
        private SerializedProperty _originalMesh;
        private SerializedProperty _maskSettings;
        private SerializedProperty _keepOriginalBounds;
        private SerializedProperty _skipMeshOptimization;

        protected override bool ShouldHideOpenButton() => true;

        public override void OnEnable()
        {
            base.OnEnable();
            _originalMesh = serializedObject.FindProperty("originalMesh");
            _maskSettings = serializedObject.FindProperty("maskSettings");
            _keepOriginalBounds = serializedObject.FindProperty("keepOriginalBounds");
            _skipMeshOptimization = serializedObject.FindProperty("skipMeshOptimization");
        }

        public override void OnInspectorGUI()
        {
            // serializedObject.UpdateIfRequiredOrScript();
            
            EditorGUILayout.HelpBox("Alpha Masked Mesh Importer\n" +
                                    "with ♥️ by SharkyTheWhite.eu", MessageType.None);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_originalMesh);
            bool inputMeshChanged = EditorGUI.EndChangeCheck();
            
            Mesh originalMesh = _originalMesh.objectReferenceValue as Mesh;
            if (!originalMesh)
            {
                EditorGUILayout.HelpBox("Required: Select the mesh which you want to mask.", MessageType.Warning);
                EditorGUILayout.HelpBox("Note: If you want to mask an FBX file, this is not the top-level entry! Expand it and find the mesh within.", MessageType.Info);
            }
            else if (!originalMesh.isReadable)
            {
                EditorGUILayout.HelpBox("Mesh must be readable!", MessageType.Error);
                ModelImporter importer =
                    AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(originalMesh)) as ModelImporter;
                if (importer && GUILayout.Button("Try to fix"))
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }

            if (inputMeshChanged)
            {
                // Convenience helper: Automatically add missing settings for all submeshes
                Mesh mesh = _originalMesh.objectReferenceValue as Mesh;
                if (mesh && mesh.isReadable && mesh.subMeshCount > _maskSettings.arraySize)
                {
                    int countBefore = _maskSettings.arraySize;
                    _maskSettings.arraySize = mesh.subMeshCount;
                    // Apply defaults since unity does not do it using the constructor...
                    for (int i = countBefore; i < mesh.subMeshCount; i++)
                    {
                        SerializedProperty sp = _maskSettings.GetArrayElementAtIndex(i); 
                        sp.FindPropertyRelative("threshold").floatValue = 0.5f;
                        sp.FindPropertyRelative("colorChannel").enumValueIndex = (int) AlphaMaskedMeshImporter.MaskChannel.Alpha;
                    }
                }
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_maskSettings);

            List<string> masksNotReadable = new List<string>();
            foreach (SerializedProperty child in _maskSettings)
            {
                Texture2D mask = child.FindPropertyRelative("mask").objectReferenceValue as Texture2D;
                if (mask && !mask.isReadable)
                {
                    masksNotReadable.Add(AssetDatabase.GetAssetPath(mask));
                }                
            }
            List<TextureImporter> importersToFix = null;
            
            if(masksNotReadable.Count > 0)
            {
                EditorGUILayout.HelpBox("All Maks Textures must be readable:\n -" + 
                                        string.Join("\n- ", masksNotReadable), MessageType.Warning);
                if (GUILayout.Button("Try to fix"))
                {
                    importersToFix = new List<TextureImporter>();                    
                    foreach (string maskAssetPath in masksNotReadable)
                    {
                        TextureImporter importer =
                            AssetImporter.GetAtPath(maskAssetPath) as TextureImporter;
                        if (importer)
                            importersToFix.Add(importer);
                    }
                }
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compatibility Options", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox("In some edge cases, optimization steps could lead to unexpected behaviour " +
                                    "when replacing meshes in already configured game objects. " +
                                    "Use these options only when you experience issues!", MessageType.Info);
            EditorGUILayout.PropertyField(_keepOriginalBounds);
            EditorGUILayout.PropertyField(_skipMeshOptimization);
            
            
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Import Result", EditorStyles.boldLabel);
            
            AlphaMaskedMeshImporter ourImporter = target as AlphaMaskedMeshImporter;
            List<string> lastImportResults = null;
            if (ourImporter?.assetPath?.Length > 0 && 
                AlphaMaskedMeshImporter.LastImportResults.TryGetValue(
                    AssetDatabase.AssetPathToGUID(ourImporter.assetPath), out lastImportResults)
                )
            {
                EditorGUILayout.HelpBox(string.Join("\n", lastImportResults), MessageType.None);
            } 
            else if (ourImporter?.assetPath?.Length > 0 && GUILayout.Button("Reimport to show messages"))
            {
                AssetImporter.GetAtPath(ourImporter.assetPath).SaveAndReimport();
            }

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
            
            // Run fixes at latest stage possible, since otherwise we will get disposed objects during validation!
            if (importersToFix != null)
            {
                foreach (TextureImporter importer in importersToFix)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }
        }
    }
}