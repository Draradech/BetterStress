using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PolyTechFramework;

namespace BetterStress
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(PolyTechMain.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(ConfigurationManager.ConfigurationManager.GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class BetterStressMain : PolyTechMod
    {
        new public const String PluginGuid = "draradech.pb2plugins.BetterStress";
        new public const String PluginName = "Better Stress";
        new public const String PluginVersion = "0.9.2";
        
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<Vector3> colCompressionMin;
        public static ConfigEntry<Vector3> colCompressionMax;
        public static ConfigEntry<Vector3> colTensionMin;
        public static ConfigEntry<Vector3> colTensionMax;
        public static ConfigEntry<float> stressSmoothing;
        public static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> maxStressHotkey;
        private static Dictionary<ConfigEntryBase, ColorCacheEntry> _colorCache = new Dictionary<ConfigEntryBase, ColorCacheEntry>();
        
        private static BepInEx.Logging.ManualLogSource staticLogger;
        private static Dictionary<Material, Color> originalColor = new Dictionary<Material, Color>();
        private static Dictionary<PolyPhysics.Edge, float> maxStress = new Dictionary<PolyPhysics.Edge, float>();
        private static Dictionary<PolyPhysics.Edge, float> curStress = new Dictionary<PolyPhysics.Edge, float>();
        private static bool maxStressSelected = false;
        
        void Awake()
        {
            modEnabled        = Config.Bind("", "Mod Enabled",                        true,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 7}));
            stressSmoothing   = Config.Bind("", "Current stress smoothing",           0.8f,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 6}));
            maxStressHotkey   = Config.Bind("", "Toggle max stress / current stress", new BepInEx.Configuration.KeyboardShortcut(KeyCode.X),  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 5}));
            colCompressionMax = Config.Bind("", "Compression Max",                    new Vector3(0.0f, 1.0f, 1.0f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 4, CustomDrawer = HsvDrawer}));
            colCompressionMin = Config.Bind("", "Compression Min",                    new Vector3(0.0f, 1.0f, 0.0f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 3, CustomDrawer = HsvDrawer}));
            colTensionMin     = Config.Bind("", "Tension Min",                        new Vector3(0.5f, 1.0f, 0.0f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 2, CustomDrawer = HsvDrawer}));
            colTensionMax     = Config.Bind("", "Tension Max",                        new Vector3(0.5f, 1.0f, 1.0f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 1, CustomDrawer = HsvDrawer}));
            
            modEnabled.SettingChanged += onEnableDisable;
            
            staticLogger = Logger;
            
            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            this.isCheat = false;
            this.isEnabled = true;
            PolyTechMain.registerMod(this);
        }
        
        private static void HsvDrawer(ConfigEntryBase entry)
        {
            var vec = (Vector3)entry.BoxedValue;
            
            if (!_colorCache.TryGetValue(entry, out var cacheEntry))
            {
                cacheEntry = new ColorCacheEntry { Tex = new Texture2D(40, 60, TextureFormat.ARGB32, false), Last = vec };
                cacheEntry.Tex.FillTexture(Color.HSVToRGB(vec.x, vec.y, vec.z));
                _colorCache[entry] = cacheEntry;
            }
            
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            GUILayout.Label("H", GUILayout.ExpandWidth(false));
            vec.x = Mathf.Round(100.0f * GUILayout.HorizontalSlider(vec.x, 0f, 1f, GUILayout.ExpandWidth(true))) / 100.0f;
            GUILayout.Label($"{vec.x:F2}", GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            GUILayout.Label("S", GUILayout.ExpandWidth(false));
            vec.y = Mathf.Round(100.0f * GUILayout.HorizontalSlider(vec.y, 0f, 1f, GUILayout.ExpandWidth(true))) / 100.0f;
            GUILayout.Label($"{vec.y:F2}", GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            GUILayout.Label("V", GUILayout.ExpandWidth(false));
            vec.z = Mathf.Round(100.0f * GUILayout.HorizontalSlider(vec.z, 0f, 1f, GUILayout.ExpandWidth(true))) / 100.0f;
            GUILayout.Label($"{vec.z:F2}", GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
            
            if (vec != cacheEntry.Last)
            {
                entry.BoxedValue = vec;
                cacheEntry.Tex.FillTexture(Color.HSVToRGB(vec.x, vec.y, vec.z));
                cacheEntry.Last = vec;
            }
            
            GUILayout.Label(cacheEntry.Tex, GUILayout.ExpandWidth(false));
        }
        
        private sealed class ColorCacheEntry
        {
          public Vector3 Last;
          public Texture2D Tex;
        }
        
        private void onEnableDisable(object sender, EventArgs e)
        {
            if (modEnabled.Value) enableMod();
            else disableMod();
        }
        
        public override void enableMod()
        {
            this.isEnabled = true;
            modEnabled.Value = true;
        }
        
        public override void disableMod()
        {
            SetOriginalColor();
            this.isEnabled = false;
            modEnabled.Value = false;
        }
        
        public override string getSettings()
        {
            return "";
        }
        
        public override void setSettings(string settings)
        {
        }
        
        [HarmonyPatch(typeof(BridgeEdges), "SetStressColor")]
        static class Patch_BridgeEdges_SetStressColor
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                if(!modEnabled.Value) return;
                
                if (maxStressHotkey.Value.IsDown())
                {
                    maxStressSelected = !maxStressSelected;
                }
                
                var bridgeLinkMeshRendererField = typeof(BridgeLink).GetField("m_MeshRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
                var hydVisMeshRenderersField = typeof(BridgeHydraulicEdgeVisualization).GetField("m_MeshRenderers", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (BridgeEdge edge in BridgeEdges.m_Edges)
                {
                    if(edge.m_PhysicsEdge != null)
                    {
                        float stress = maxStressSelected ? maxStress[edge.m_PhysicsEdge] : curStress[edge.m_PhysicsEdge];
                        float h = stress > 0.0 ? Mathf.Lerp(colCompressionMin.Value.x, colCompressionMax.Value.x, stress) : Mathf.Lerp(colTensionMin.Value.x, colTensionMax.Value.x, -stress);
                        float s = stress > 0.0 ? Mathf.Lerp(colCompressionMin.Value.y, colCompressionMax.Value.y, stress) : Mathf.Lerp(colTensionMin.Value.y, colTensionMax.Value.y, -stress);
                        float v = stress > 0.0 ? Mathf.Lerp(colCompressionMin.Value.z, colCompressionMax.Value.z, stress) : Mathf.Lerp(colTensionMin.Value.z, colTensionMax.Value.z, -stress);
                        Color stressCol = Color.HSVToRGB(h, s, v);
                        if ((edge.m_Material.m_MaterialType == BridgeMaterialType.ROPE || edge.m_Material.m_MaterialType == BridgeMaterialType.CABLE) && BridgeRopes.m_BridgeRopes.Count > 0)
                        {
                            foreach (BridgeRope bridgeRope in BridgeRopes.m_BridgeRopes)
                            {
                                if ((UnityEngine.Object) bridgeRope.m_ParentEdge == (UnityEngine.Object) edge)
                                {
                                    bridgeRope.SetStressColor(0.0f);
                                    foreach (BridgeLink link in bridgeRope.m_Links)
                                    {
                                        Material m = ((UnityEngine.MeshRenderer)bridgeLinkMeshRendererField.GetValue(link)).material;
                                        if (!originalColor.ContainsKey(m))
                                        {
                                            originalColor[m] = m.color;
                                        }
                                        m.color = stressCol;
                                    }
                                }
                            }
                        }
                        else if (edge.m_Material.m_MaterialType == BridgeMaterialType.SPRING && BridgeSprings.m_BridgeSprings.Count > 0)
                        {
                            BridgeSprings.SetStressColorForEdge(edge, 0.0f);
                            Material m1 = edge.m_SpringCoilVisualization.m_FrontLink.m_MeshRenderer.material;
                            if (!originalColor.ContainsKey(m1))
                            {
                                originalColor[m1] = m1.color;
                            }
                            m1.color = stressCol;
                            Material m2 = edge.m_SpringCoilVisualization.m_FrontLink.m_MeshRenderer.material;
                            if (!originalColor.ContainsKey(m2))
                            {
                                originalColor[m2] = m2.color;
                            }
                            m2.color = stressCol;
                        }
                        else
                        {
                            edge.SetStressColor(0.0f);
                            Material m1 = edge.m_MeshRenderer.material;
                            if (!originalColor.ContainsKey(m1))
                            {
                                originalColor[m1] = m1.color;
                            }
                            m1.color = stressCol;
                            if (edge.m_Material.m_MaterialType == BridgeMaterialType.REINFORCED_ROAD)
                            {
                                Material m2 = edge.m_MeshRenderer.materials[1];
                                if (!originalColor.ContainsKey(m2))
                                {
                                    originalColor[m2] = m2.color;
                                }
                                m2.color = stressCol;
                            }
                        }
                        
                        if (edge.m_HydraulicEdgeVisualization != null)
                        {
                            edge.m_HydraulicEdgeVisualization.SetStressColorForEdge(edge, 0.0f);
                            foreach (MeshRenderer meshRenderer in ((MeshRenderer[])hydVisMeshRenderersField.GetValue(edge.m_HydraulicEdgeVisualization)))
                            {
                                Material m = meshRenderer.material;
                                if (!originalColor.ContainsKey(m))
                                {
                                    originalColor[m] = m.color;
                                }
                                m.color = stressCol;
                            }
                        }
                    }
                }
            }
        }
        
        [HarmonyPatch(typeof(BridgeEdges), "SetOriginalColor")]
        static class Patch_BridgeEdges_SetOriginalColor
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                if(!modEnabled.Value) return;
                
                SetOriginalColor();
            }
        }
        
        private static void SetOriginalColor()
        {
            var bridgeLinkMeshRendererField = typeof(BridgeLink).GetField("m_MeshRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
            var hydVisMeshRenderersField = typeof(BridgeHydraulicEdgeVisualization).GetField("m_MeshRenderers", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (BridgeEdge edge in BridgeEdges.m_Edges)
            {
                if ((edge.m_Material.m_MaterialType == BridgeMaterialType.ROPE || edge.m_Material.m_MaterialType == BridgeMaterialType.CABLE) && BridgeRopes.m_BridgeRopes.Count > 0)
                {
                    foreach (BridgeRope bridgeRope in BridgeRopes.m_BridgeRopes)
                    {
                        if ((UnityEngine.Object) bridgeRope.m_ParentEdge == (UnityEngine.Object) edge)
                        {
                            foreach (BridgeLink link in bridgeRope.m_Links)
                            {
                                Material m = ((UnityEngine.MeshRenderer)bridgeLinkMeshRendererField.GetValue(link)).material;
                                if (originalColor.ContainsKey(m))
                                {
                                    m.color = originalColor[m];
                                }
                            }
                        }
                    }
                }
                else if (edge.m_Material.m_MaterialType == BridgeMaterialType.SPRING && BridgeSprings.m_BridgeSprings.Count > 0)
                {
                    Material m1 = edge.m_SpringCoilVisualization.m_FrontLink.m_MeshRenderer.material;
                    if (originalColor.ContainsKey(m1))
                    {
                        m1.color = originalColor[m1];
                    }
                    Material m2 = edge.m_SpringCoilVisualization.m_FrontLink.m_MeshRenderer.material;
                    if (originalColor.ContainsKey(m2))
                    {
                        m2.color = originalColor[m2];
                    }
                }
                else
                {
                    Material m1 = edge.m_MeshRenderer.material;
                    if (originalColor.ContainsKey(m1))
                    {
                        m1.color = originalColor[m1];
                    }
                    if (edge.m_Material.m_MaterialType == BridgeMaterialType.REINFORCED_ROAD)
                    {
                        Material m2 = edge.m_MeshRenderer.materials[1];
                        if (originalColor.ContainsKey(m2))
                        {
                            m2.color = originalColor[m2];
                        }
                    }
                }
                
                if (edge.m_HydraulicEdgeVisualization != null)
                {
                    foreach (MeshRenderer meshRenderer in ((MeshRenderer[])hydVisMeshRenderersField.GetValue(edge.m_HydraulicEdgeVisualization)))
                    {
                        Material m = meshRenderer.material;
                        if (originalColor.ContainsKey(m))
                        {
                            m.color = originalColor[m];
                        }
                    }
                }
            }
        }
        
        [HarmonyPatch(typeof(GameStateSim), "Exit")]
        static class Patch_GameStateSim_Exit
        {
            [HarmonyPostfix]
            static void Postfix(GameState nextState)
            {
                originalColor.Clear();
                maxStress.Clear();
                curStress.Clear();
            }
        }
        
        [HarmonyPatch(typeof(PolyPhysics.World), "FixedUpdate_Manual")]
        static class Patch_PolyPhysicsWorld_FixedUpdate_Manual
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                if(!modEnabled.Value) return;
                
                foreach (BridgeEdge edge in BridgeEdges.m_Edges)
                {
                    if(edge.m_PhysicsEdge != null)
                    {
                        float signedStress = edge.m_PhysicsEdge.handle.stressNormalizedSigned;
                        if (!maxStress.ContainsKey(edge.m_PhysicsEdge))
                        {
                            maxStress[edge.m_PhysicsEdge] = signedStress;
                        }
                        if (Mathf.Abs(signedStress) > Mathf.Abs(maxStress[edge.m_PhysicsEdge]))
                        {
                            maxStress[edge.m_PhysicsEdge] = signedStress;
                        }
                        if (!curStress.ContainsKey(edge.m_PhysicsEdge))
                        {
                            curStress[edge.m_PhysicsEdge] = signedStress;
                        }
                        curStress[edge.m_PhysicsEdge] = stressSmoothing.Value * curStress[edge.m_PhysicsEdge] + (1.0f - stressSmoothing.Value) * signedStress;
                    }
                }
            }
        }
    }
    internal static class Utils
    {
        public static void FillTexture(this Texture2D tex, Color color)
        {
          for (int x = 0; x < tex.width; ++x)
          {
            for (int y = 0; y < tex.height; ++y)
              tex.SetPixel(x, y, color);
          }
          tex.Apply(false);
        }
    }
}
