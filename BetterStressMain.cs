using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using BepInEx;
using BepInEx.Configuration;
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
        new public const String PluginVersion = "0.9.4";
        
        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<Vector3> colCompressionMin;
        private static ConfigEntry<Vector3> colCompressionMax;
        private static ConfigEntry<Vector3> colTensionMin;
        private static ConfigEntry<Vector3> colTensionMax;
        private static ConfigEntry<float> stressSmoothing;
        private static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> maxStressHotkey;
        private static ConfigEntry<float>[] stressExponent;
        private static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> stressExponentKey;
        private static Dictionary<ConfigEntryBase, ColorCacheEntry> _colorCache = new Dictionary<ConfigEntryBase, ColorCacheEntry>();
        
        private static BepInEx.Logging.ManualLogSource staticLogger;
        private static Dictionary<Material, Color> originalColor = new Dictionary<Material, Color>();
        private static Dictionary<PolyPhysics.Edge, float> maxStress = new Dictionary<PolyPhysics.Edge, float>();
        private static Dictionary<PolyPhysics.Edge, float> curStress = new Dictionary<PolyPhysics.Edge, float>();
        private static bool maxStressSelected = false;
        private static int selectedExponent = 1;
        private static BridgeEdge newEdge;
        
        void Awake()
        {
            stressExponent = new ConfigEntry<float>[3];
            
            modEnabled        = Config.Bind("", "Mod Enabled",                        true,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 11}));
            stressSmoothing   = Config.Bind("", "Current stress smoothing",           0.8f,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 10}));
            maxStressHotkey   = Config.Bind("", "Toggle max stress / current stress", new BepInEx.Configuration.KeyboardShortcut(KeyCode.X),  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  9}));
            stressExponent[0] = Config.Bind("", "Stress exponent 1",                  0.33f,                                                  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  8}));
            stressExponent[1] = Config.Bind("", "Stress exponent 2",                  1.0f,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  7}));
            stressExponent[2] = Config.Bind("", "Stress exponent 3",                  3.0f,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  6}));
            stressExponentKey = Config.Bind("", "Stress exponent key",                new BepInEx.Configuration.KeyboardShortcut(KeyCode.J),  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  5}));
            colCompressionMax = Config.Bind("", "Compression Max",                    new Vector3(0.0f, 1.0f, 0.8f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  4, CustomDrawer = HsvDrawer}));
            colCompressionMin = Config.Bind("", "Compression Min",                    new Vector3(0.0f, 1.0f, 0.0f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  3, CustomDrawer = HsvDrawer}));
            colTensionMin     = Config.Bind("", "Tension Min",                        new Vector3(0.5f, 1.0f, 0.0f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  2, CustomDrawer = HsvDrawer}));
            colTensionMax     = Config.Bind("", "Tension Max",                        new Vector3(0.5f, 1.0f, 0.8f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  1, CustomDrawer = HsvDrawer}));
            
            modEnabled.SettingChanged += onEnableDisable;
            
            staticLogger = Logger;
            
            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            this.isCheat = false;
            this.isEnabled = true;
            PolyTechMain.registerMod(this);
        }
        
        private sealed class ColorCacheEntry
        {
          public Vector3 Last;
          public Texture2D Tex;
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
        
        private static void SetColor(Material m, Color c)
        {
            if (!originalColor.ContainsKey(m))
            {
                originalColor[m] = m.color;
            }
            m.color = c;
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
                
                if(stressExponentKey.Value.IsDown())
                {
                    selectedExponent = (selectedExponent + 1) % 3;
                }
                
                var bridgeLinkMeshRendererField = typeof(BridgeLink).GetField("m_MeshRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (BridgeEdge edge in BridgeEdges.m_Edges)
                {
                    float stress = 0.0f;
                    if(edge.m_PhysicsEdge != null)
                    {
                        stress = maxStressSelected ? maxStress[edge.m_PhysicsEdge] : curStress[edge.m_PhysicsEdge];
                    }
                    bool compression = stress > 0.0;
                    stress = edge.m_IsBroken ? 0.0f : stress;
                    stress = Mathf.Pow(Mathf.Abs(stress), stressExponent[selectedExponent].Value);
                    Vector3 hsv = compression ? Vector3.Lerp(colCompressionMin.Value, colCompressionMax.Value, stress) : Vector3.Lerp(colTensionMin.Value, colTensionMax.Value, stress);
                    Color stressCol = Color.HSVToRGB(hsv.x, hsv.y, hsv.z);
                    if ((edge.m_Material.m_MaterialType == BridgeMaterialType.ROPE || edge.m_Material.m_MaterialType == BridgeMaterialType.CABLE) && BridgeRopes.m_BridgeRopes.Count > 0)
                    {
                        foreach (BridgeRope bridgeRope in BridgeRopes.m_BridgeRopes)
                        {
                            if ((UnityEngine.Object) bridgeRope.m_ParentEdge == (UnityEngine.Object) edge)
                            {
                                bridgeRope.SetStressColor(0.0f);
                                foreach (BridgeLink link in bridgeRope.m_Links)
                                {
                                    SetColor(((MeshRenderer)bridgeLinkMeshRendererField.GetValue(link)).material, stressCol);
                                    
                                }
                            }
                        }
                    }
                    else if (edge.m_Material.m_MaterialType == BridgeMaterialType.SPRING && BridgeSprings.m_BridgeSprings.Count > 0)
                    {
                        BridgeSprings.SetStressColorForEdge(edge, 0.0f);
                        SetColor(edge.m_SpringCoilVisualization.m_FrontLink.m_MeshRenderer.material, stressCol);
                        SetColor(edge.m_SpringCoilVisualization.m_BackLink.m_MeshRenderer.material, stressCol);
                    }
                    else
                    {
                        edge.SetStressColor(0.0f);
                        SetColor(edge.m_MeshRenderer.material, stressCol);
                        if (edge.m_Material.m_MaterialType == BridgeMaterialType.REINFORCED_ROAD)
                        {
                            SetColor(edge.m_MeshRenderer.materials[1], stressCol);
                        }
                    }
                    
                    if (edge.m_HydraulicEdgeVisualization != null)
                    {
                        edge.m_HydraulicEdgeVisualization.SetStressColorForEdge(edge, 0.0f);
                        foreach (MeshRenderer meshRenderer in edge.m_HydraulicEdgeVisualization.GetComponentsInChildren<MeshRenderer>())
                        {
                            SetColor(meshRenderer.material, stressCol);
                        }
                    }
                }
            }
        }
        
        private static void RestoreColor(Material m)
        {
            if (originalColor.ContainsKey(m))
            {
                m.color = originalColor[m];
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
                                RestoreColor(((MeshRenderer)bridgeLinkMeshRendererField.GetValue(link)).material);
                            }
                        }
                    }
                }
                else if (edge.m_Material.m_MaterialType == BridgeMaterialType.SPRING && BridgeSprings.m_BridgeSprings.Count > 0)
                {
                    RestoreColor(edge.m_SpringCoilVisualization.m_FrontLink.m_MeshRenderer.material);
                    RestoreColor(edge.m_SpringCoilVisualization.m_BackLink.m_MeshRenderer.material);
                }
                else
                {
                    RestoreColor(edge.m_MeshRenderer.material);
                    if (edge.m_Material.m_MaterialType == BridgeMaterialType.REINFORCED_ROAD)
                    {
                        RestoreColor(edge.m_MeshRenderer.materials[1]);
                    }
                }
                
                if (edge.m_HydraulicEdgeVisualization != null)
                {
                    foreach (MeshRenderer meshRenderer in edge.m_HydraulicEdgeVisualization.GetComponentsInChildren<MeshRenderer>())
                    {
                        RestoreColor(meshRenderer.material);
                    }
                }
            }
        }
        
        private static void SetDebrisColor(Material mn, Material mb)
        {
            if (!originalColor.ContainsKey(mn))
            {
                if (originalColor.ContainsKey(mb))
                {
                    originalColor[mn] = originalColor[mb];
                }
            }
        }
        
        [HarmonyPatch(typeof(PolyPhysics.BridgeEdgeListener), "CreateBridgeEdgeFromEdge")]
        static class Patch_BridgeEdgeListener_CreateBridgeEdgeFromEdge
        {
            [HarmonyPostfix]
            static void Postfix(ref BridgeEdge __result)
            {
                if(!modEnabled.Value) return;
                
                newEdge = __result;
            }
        }
        
        [HarmonyPatch(typeof(PolyPhysics.BridgeEdgeListener), "CreateDebris")]
        static class Patch_BridgeEdgeListener_CreateDebris 
        {
            [HarmonyPostfix]
            static void Postfix(ref Poly.Physics.EdgeHandle e, ref BridgeEdge brokenEdge)
            {
                if(!modEnabled.Value) return;
                
                if(!newEdge)
                {
                    staticLogger.LogError("no new edge set in create debris");
                    return;
                }
                SetDebrisColor(newEdge.m_MeshRenderer.material, brokenEdge.m_MeshRenderer.material);
                if (brokenEdge.m_Material.m_MaterialType == BridgeMaterialType.REINFORCED_ROAD)
                {
                    SetDebrisColor(newEdge.m_MeshRenderer.materials[1], brokenEdge.m_MeshRenderer.materials[1]);
                }
                if (brokenEdge.m_HydraulicEdgeVisualization != null)
                {
                    foreach (MeshRenderer meshRenderer in newEdge.m_HydraulicEdgeVisualization.GetComponentsInChildren<MeshRenderer>())
                    {
                        SetDebrisColor(meshRenderer.material, brokenEdge.m_HydraulicEdgeVisualization.GetComponentsInChildren<MeshRenderer>()[0].material);
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
        static class Patch_World_FixedUpdate_Manual
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
