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
        new public const String PluginVersion = "0.9.0";
        
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<Color> colNeutral;
        public static ConfigEntry<Color> colCompression;
        public static ConfigEntry<Color> colTension;
        public static ConfigEntry<float> stressSmoothing;
        public static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> maxStressHotkey;
        
        private static BepInEx.Logging.ManualLogSource staticLogger;
        private static Dictionary<Material, Color> originalColor = new Dictionary<Material, Color>();
        private static Dictionary<PolyPhysics.Edge, float> maxStress = new Dictionary<PolyPhysics.Edge, float>();
        private static Dictionary<PolyPhysics.Edge, float> curStress = new Dictionary<PolyPhysics.Edge, float>();
        private static bool maxStressSelected = false;
        
        void Awake()
        {
            modEnabled      = Config.Bind("", "Mod Enabled",                        true,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 6}));
            colNeutral      = Config.Bind("", "Neutral Stress Color",               new Color(0.0f, 0.0f, 0.0f),                            new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 5}));
            colCompression  = Config.Bind("", "Max Compression Color",              new Color(1.0f, 0.0f, 0.0f),                            new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 4}));
            colTension      = Config.Bind("", "Max Tension Color",                  new Color(0.0f, 1.0f, 1.0f),                            new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 3}));
            stressSmoothing = Config.Bind("", "Current stress smoothing",           0.8f,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 2}));
            maxStressHotkey = Config.Bind("", "Toggle max stress / current stress", new BepInEx.Configuration.KeyboardShortcut(KeyCode.X),  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 1}));
            
            modEnabled.SettingChanged += onEnableDisable;
            
            staticLogger = Logger;
            
            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            this.isCheat = true;
            this.isEnabled = true;
            PolyTechMain.registerMod(this);
        }
        
        private void onEnableDisable(object sender, EventArgs e)
        {
            if (modEnabled.Value) enableMod();
            else disableMod();
            this.isEnabled = modEnabled.Value;
        }
        
        public override void enableMod()
        {
        }
        
        public override void disableMod()
        {
            SetOriginalColor();
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
                        float selectedStress = maxStressSelected ? maxStress[edge.m_PhysicsEdge] : curStress[edge.m_PhysicsEdge];
                        Color stressCol = (selectedStress > 0.0) ? Color.Lerp(colNeutral.Value, colCompression.Value, selectedStress) : Color.Lerp(colNeutral.Value, colTension.Value, -selectedStress);
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
}

internal sealed class ConfigurationManagerAttributes
{
    public int? Order;
}