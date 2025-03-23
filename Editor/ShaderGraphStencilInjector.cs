using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Serialization;

namespace ShaderGraphExtension
{
    public class ShaderGraphStencilInjector : Editor
    {
        [MenuItem("Assets/Shader Graph Extensions/Inject Stencil Support", false, 4201)]
        static public void Menu()
        {
            if (Selection.objects == null || Selection.objects.Length == 0)
                return;

            SortedDictionary<string, int> statisticsExtensions = new SortedDictionary<string, int>();
            List<string> statisticsSuccess = new List<string>();
            List<string> statisticsSkipped = new List<string>();
            List<string> statisticsFailure = new List<string>();

            foreach (var item in Selection.objects)
            {
                string assetPath = AssetDatabase.GetAssetPath(item);
                string extension = Path.GetExtension(assetPath).ToLowerInvariant();
                if (statisticsExtensions.ContainsKey(extension) == false) statisticsExtensions.Add(extension, 0);
                statisticsExtensions[extension] += 1;

                switch (InjectStencil(item))
                {
                    case ConversionState.Failed: statisticsFailure.Add(assetPath); break;
                    case ConversionState.Success: statisticsSuccess.Add(assetPath); break;
                    case ConversionState.Skip: statisticsSkipped.Add(assetPath); break;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            OutputStatistics(statisticsExtensions, statisticsSuccess, statisticsSkipped, statisticsFailure);
        }

        [MenuItem("Assets/Shader Graph Extensions/Inject Stencil Support", true, 4201)]
        static public bool Validate_Menu()
        {
            if (Selection.objects == null || Selection.objects.Length == 0)
                return false;

            foreach (var item in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(item);
                if (!string.IsNullOrEmpty(path) && Path.GetExtension(path).ToLowerInvariant() == ".shadergraph")
                    return true;
            }

            return false;
        }

        static bool IsAssetReady(UnityEngine.Object obj)
        {
            if (obj == null)
                return false;

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                return false;

            if (Path.GetExtension(path).ToLowerInvariant() != ".shadergraph")
                return false;

            Shader shader = (Shader)AssetDatabase.LoadAssetAtPath(path, typeof(Shader));
            return shader != null;
        }

        static ConversionState InjectStencil(UnityEngine.Object sourceShaderAsset)
        {
            if (!IsAssetReady(sourceShaderAsset))
                return ConversionState.Skip;

            string sourceShaderGraphAssetPath = AssetDatabase.GetAssetPath(sourceShaderAsset);
            string destinationShaderAssetPath = Path.Combine(
                Path.GetDirectoryName(sourceShaderGraphAssetPath), 
                Path.GetFileNameWithoutExtension(sourceShaderGraphAssetPath) + ".shader");

            string hlslCode = GetShaderGraphHLSLCode(sourceShaderGraphAssetPath);
            if (string.IsNullOrEmpty(hlslCode))
                return ConversionState.Failed;

            CreateShaderAssetFile(destinationShaderAssetPath, new List<string> { hlslCode });

            List<string> newShaderFile = File.ReadAllLines(destinationShaderAssetPath).ToList();
            newShaderFile.Insert(0, string.Empty);
            newShaderFile.Insert(0, "// Stencil Injection by ShaderGraphStencilInjector");

            if (!ChangeShaderName(sourceShaderGraphAssetPath, newShaderFile))
                return ConversionState.Failed;

            if (!AddStencilProperties(newShaderFile))
                return ConversionState.Failed;

            if (!AddStencilCode(newShaderFile))
                return ConversionState.Failed;

            CreateShaderAssetFile(destinationShaderAssetPath, newShaderFile);

            return ConversionState.Success;
        }

        static bool ChangeShaderName(string sourceShaderAssetPath, List<string> shaderFile)
        {
            string originalName = Path.GetFileNameWithoutExtension(sourceShaderAssetPath);

            for (int i = 0; i < shaderFile.Count; i++)
            {
                if (shaderFile[i].Contains("Shader \""))
                {
                    shaderFile[i] = "Shader \"Stencil Shader Graph/" + originalName + "\"";
                    return true;
                }
            }

            return false;
        }

        static bool AddStencilProperties(List<string> shaderFile)
        {
            int propertiesStart = -1;
            int propertiesEnd = -1;

            for (int i = 0; i < shaderFile.Count; i++)
            {
                if (shaderFile[i].Trim() == "Properties")
                {
                    propertiesStart = i + 1;
                    for (int j = propertiesStart; j < shaderFile.Count; j++)
                    {
                        if (shaderFile[j].Trim() == "}")
                        {
                            propertiesEnd = j;
                            break;
                        }
                    }
                    break;
                }
            }

            if (propertiesStart == -1 || propertiesEnd == -1)
                return false;

            // Remove Unity lightmaps
            for (int i = propertiesStart; i < propertiesEnd; i++)
            {
                string line = shaderFile[i];
                if (line.Contains("unity_Lightmaps") || 
                    line.Contains("unity_LightmapsInd") || 
                    line.Contains("unity_ShadowMasks"))
                {
                    shaderFile.RemoveAt(i);
                    i--;
                    propertiesEnd--;
                }
            }

            List<string> stencilProperties = new List<string>
            {
                "",
                "        // Stencil Properties",
                "        [IntRange] _StencilRef (\"Stencil Reference Value\", Range(0, 255)) = 0",
                "        [IntRange] _StencilReadMask (\"Stencil ReadMask Value\", Range(0, 255)) = 255",
                "        [IntRange] _StencilWriteMask (\"Stencil WriteMask Value\", Range(0, 255)) = 255",
                "        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp (\"Stencil Comparison\", Float) = 0",
                "        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass (\"Stencil Pass Op\", Float) = 0",
                "        [Enum(UnityEngine.Rendering.StencilOp)] _StencilFail (\"Stencil Fail Op\", Float) = 0",
                "        [Enum(UnityEngine.Rendering.StencilOp)] _StencilZFail (\"Stencil ZFail Op\", Float) = 0",
                "        [Enum(Off,0,On,1)] _StencilEnabled (\"Stencil Enabled\", Float) = 0"
            };

            shaderFile.InsertRange(propertiesEnd, stencilProperties);
            return true;
        }

        static bool AddStencilCode(List<string> shaderFile)
        {
            bool stencilAdded = false;

            for (int i = 0; i < shaderFile.Count; i++)
            {
                bool isMainPass = false;

                if (shaderFile[i].Trim() == "Pass")
                {
                    for (int j = i; j < Math.Min(i + 25, shaderFile.Count); j++)
                    {
                        string line = shaderFile[j].Trim();
                        if (line.Contains("\"Universal Forward\"") || 
                            line.Contains("Name \"Universal Forward\"") ||
                            line.Contains("SHADERPASS_FORWARD") ||
                            line.Contains("SHADERPASS SHADERPASS_UNLIT"))
                        {
                            isMainPass = true;
                            break;
                        }
                    }
                    
                    if (!stencilAdded && !isMainPass)
                        isMainPass = true;

                    if (isMainPass)
                    {
                        int insertPosition = -1;
                        for (int j = i + 1; j < Math.Min(i + 50, shaderFile.Count); j++)
                        {
                            string line = shaderFile[j].Trim();
                            if (line.Contains("// Debug") || line.Contains("// --------------------------------------------------"))
                            {
                                insertPosition = j;
                                break;
                            }
                        }

                        if (insertPosition != -1)
                        {
                            List<string> stencilCode = new List<string>
                            {
                                "        // Stencil Buffer Setup",
                                "        Stencil",
                                "        {",
                                "            Ref [_StencilRef]",
                                "            ReadMask [_StencilReadMask]",
                                "            WriteMask [_StencilWriteMask]",
                                "            Comp [_StencilComp]",
                                "            Pass [_StencilPass]", 
                                "            Fail [_StencilFail]", 
                                "            ZFail [_StencilZFail]",
                                "        }",
                                ""
                            };

                            shaderFile.InsertRange(insertPosition, stencilCode);
                            stencilAdded = true;
                            i = insertPosition + stencilCode.Count;
                        }
                    }
                }
            }

            return stencilAdded;
        }

        static void CreateShaderAssetFile(string path, List<string> content)
        {
            string normalizedContent = string.Join(System.Environment.NewLine, content);
            File.WriteAllText(path, normalizedContent);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        static void OutputStatistics(SortedDictionary<string, int> extensions, List<string> success, List<string> skipped, List<string> failure)
        {
            StackTraceLogType saveLog = Application.GetStackTraceLogType(LogType.Log);
            StackTraceLogType saveWarning = Application.GetStackTraceLogType(LogType.Warning);
            StackTraceLogType saveError = Application.GetStackTraceLogType(LogType.Error);

            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);

            if (success.Count > 0)
            {
                string str = $"Files updated: {success.Count}";
                foreach (var group in success.GroupBy(c => Path.GetExtension(c)).Select(c => c.ToList()).ToList())
                {
                    string currentStr = $"\n\n{Path.GetExtension(group[0])}: {group.Count}";
                    foreach (var item in group)
                    {
                        currentStr += $"\n{item}";
                    }
                    str += currentStr;
                }
                Debug.Log(str);
            }

            if (skipped.Count > 0)
            {
                string str = $"Files skipped: {skipped.Count}";
                foreach (var group in skipped.GroupBy(c => Path.GetExtension(c)).Select(c => c.ToList()).ToList())
                {
                    string currentStr = $"\n\n{Path.GetExtension(group[0])}: {group.Count}";
                    foreach (var item in group)
                    {
                        currentStr += $"\n{item}";
                    }
                    str += currentStr;
                }
                Debug.LogWarning(str);
            }

            if (failure.Count > 0)
            {
                string str = $"Files failed: {failure.Count}";
                foreach (var group in failure.GroupBy(c => Path.GetExtension(c)).Select(c => c.ToList()).ToList())
                {
                    string currentStr = $"\n\n{Path.GetExtension(group[0])}: {group.Count}";
                    foreach (var item in group)
                    {
                        currentStr += $"\n{item}";
                    }
                    str += currentStr;
                }
                Debug.LogError(str);
            }

            Application.SetStackTraceLogType(LogType.Log, saveLog);
            Application.SetStackTraceLogType(LogType.Warning, saveWarning);
            Application.SetStackTraceLogType(LogType.Error, saveError);
        }

        static string GetShaderGraphHLSLCode(string shaderGraphAssetPath)
        {
            AssetImporter importer = AssetImporter.GetAtPath(shaderGraphAssetPath);
            string assetName = Path.GetFileNameWithoutExtension(importer.assetPath);

            var graphData = GetGraphData(importer);
            var generator = new Generator(graphData, null, GenerationMode.ForReals, assetName, null);

            return generator.generatedShader;
        }

        static GraphData GetGraphData(AssetImporter importer)
        {
            var textGraph = File.ReadAllText(importer.assetPath, Encoding.UTF8);
            var graphObject = ScriptableObject.CreateInstance<GraphObject>();
            graphObject.hideFlags = HideFlags.HideAndDontSave;
            
            bool isSubGraph;
            var extension = Path.GetExtension(importer.assetPath).Replace(".", "");
            switch (extension)
            {
                case ShaderGraphImporter.Extension:
                case ShaderGraphImporter.LegacyExtension:
                    isSubGraph = false;
                    break;
                case ShaderSubGraphImporter.Extension:
                    isSubGraph = true;
                    break;
                default:
                    throw new Exception($"Invalid file extension {extension}");
            }
            
            var assetGuid = AssetDatabase.AssetPathToGUID(importer.assetPath);
            graphObject.graph = new GraphData
            {
                assetGuid = assetGuid,
                isSubGraph = isSubGraph,
                messageManager = null
            };
            
            MultiJson.Deserialize(graphObject.graph, textGraph);
            graphObject.graph.OnEnable();
            graphObject.graph.ValidateGraph();
            
            return graphObject.graph;
        }

        enum ConversionState { Failed, Success, Skip };
    }

    public class StencilShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            base.OnGUI(materialEditor, properties);

            Material material = materialEditor.target as Material;
            if (material == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stencil Settings", EditorStyles.boldLabel);

            MaterialProperty stencilEnabled = FindProperty("_StencilEnabled", properties, false);
            MaterialProperty stencilRef = FindProperty("_StencilRef", properties, false);
            MaterialProperty stencilReadMask = FindProperty("_StencilReadMask", properties, false);
            MaterialProperty stencilWriteMask = FindProperty("_StencilWriteMask", properties, false);
            MaterialProperty stencilComp = FindProperty("_StencilComp", properties, false);
            MaterialProperty stencilPass = FindProperty("_StencilPass", properties, false);
            MaterialProperty stencilFail = FindProperty("_StencilFail", properties, false);
            MaterialProperty stencilZFail = FindProperty("_StencilZFail", properties, false);

            if (stencilEnabled != null)
            {
                EditorGUI.BeginChangeCheck();
                materialEditor.ShaderProperty(stencilEnabled, "Enable Stencil");
                if (EditorGUI.EndChangeCheck())
                {
                    material.SetFloat("_StencilEnabled", stencilEnabled.floatValue);
                }
            }

            if (stencilEnabled != null && stencilEnabled.floatValue > 0)
            {
                EditorGUI.indentLevel++;
                
                if (stencilRef != null)
                    materialEditor.ShaderProperty(stencilRef, "Reference Value");
                
                if (stencilReadMask != null)
                    materialEditor.ShaderProperty(stencilReadMask, "Read Mask");
                
                if (stencilWriteMask != null)
                    materialEditor.ShaderProperty(stencilWriteMask, "Write Mask");
                
                if (stencilComp != null)
                    materialEditor.ShaderProperty(stencilComp, "Comparison");
                
                if (stencilPass != null)
                    materialEditor.ShaderProperty(stencilPass, "Pass Operation");
                
                if (stencilFail != null)
                    materialEditor.ShaderProperty(stencilFail, "Fail Operation");
                
                if (stencilZFail != null)
                    materialEditor.ShaderProperty(stencilZFail, "ZFail Operation");
                
                EditorGUI.indentLevel--;
            }
        }
    }
}