﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PolyTechFramework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterStress
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(PolyTechMain.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(ConfigurationManager.ConfigurationManager.GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class BetterStressMain : PolyTechMod
    {
        public new const String PluginGuid = "draradech.pb2plugins.BetterStress";
        public new const String PluginName = "Better Stress";
        public new const String PluginVersion = "0.9.6";

        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<bool> replayStress;
        private static ConfigEntry<bool> highlightWorst;
        private static ConfigEntry<bool> markWorst;
        private static ConfigEntry<Vector3> colCompressionMin;
        private static ConfigEntry<Vector3> colCompressionMax;
        private static ConfigEntry<Vector3> colTensionMin;
        private static ConfigEntry<Vector3> colTensionMax;
        private static ConfigEntry<Color> colHighlight;
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
        private static FieldInfo bridgeLinkMeshRendererField = typeof(BridgeLink).GetField("m_MeshRenderer", BindingFlags.NonPublic | BindingFlags.Instance);

        void Awake()
        {
            stressExponent = new ConfigEntry<float>[3];

            modEnabled        = Config.Bind("", "Mod Enabled",                        true,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 15}));
            replayStress      = Config.Bind("", "Replay records stress view",         false,                                                  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 14}));
            highlightWorst    = Config.Bind("", "Highlight worst stress during sim",  false,                                                  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 13}));
            markWorst         = Config.Bind("", "Mark worst stress in editor (if no break)", false,                                           new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 12}));
            stressSmoothing   = Config.Bind("", "Current stress smoothing",           0.8f,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 11}));
            maxStressHotkey   = Config.Bind("", "Toggle max stress / current stress", new BepInEx.Configuration.KeyboardShortcut(KeyCode.X),  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order = 10}));
            stressExponent[0] = Config.Bind("", "Stress exponent 1",                  0.33f,                                                  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  9}));
            stressExponent[1] = Config.Bind("", "Stress exponent 2",                  1.0f,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  8}));
            stressExponent[2] = Config.Bind("", "Stress exponent 3",                  3.0f,                                                   new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  7}));
            stressExponentKey = Config.Bind("", "Stress exponent key",                new BepInEx.Configuration.KeyboardShortcut(KeyCode.J),  new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  6}));
            colCompressionMax = Config.Bind("", "Compression Max",                    new Vector3(0.0f, 1.0f, 0.8f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  5, CustomDrawer = HsvDrawer}));
            colCompressionMin = Config.Bind("", "Compression Min",                    new Vector3(0.0f, 1.0f, 0.0f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  4, CustomDrawer = HsvDrawer}));
            colTensionMin     = Config.Bind("", "Tension Min",                        new Vector3(0.5f, 1.0f, 0.0f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  3, CustomDrawer = HsvDrawer}));
            colTensionMax     = Config.Bind("", "Tension Max",                        new Vector3(0.5f, 1.0f, 0.8f),                          new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  2, CustomDrawer = HsvDrawer}));
            colHighlight      = Config.Bind("", "Worst stress highlight color",       new Color(1.0f, 0.0f, 1.0f),                            new ConfigDescription("", null, new ConfigurationManagerAttributes{Order =  1}));

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

        private static void SetMaterialColor(Material m, Color c)
        {
            if (!originalColor.ContainsKey(m))
            {
                originalColor[m] = m.color;
            }
            m.color = c;
        }

        private static void SetEdgeColor(BridgeEdge edge, Color stressCol)
        {
            if (edge.m_Material.m_MaterialType == BridgeMaterialType.ROPE || edge.m_Material.m_MaterialType == BridgeMaterialType.CABLE)
            {
                foreach (BridgeRope bridgeRope in BridgeRopes.m_BridgeRopes)
                {
                    if ((UnityEngine.Object)bridgeRope.m_ParentEdge == (UnityEngine.Object)edge)
                    {
                        bridgeRope.SetStressColor(0.0f);
                        foreach (BridgeLink link in bridgeRope.m_Links)
                        {
                            SetMaterialColor(((MeshRenderer)bridgeLinkMeshRendererField.GetValue(link)).material, stressCol);
                        }
                    }
                }
            }

            if (edge.m_Material.m_MaterialType == BridgeMaterialType.SPRING)
            {
                BridgeSprings.SetStressColorForEdge(edge, 0.0f);
                SetMaterialColor(edge.m_SpringCoilVisualization.m_FrontLink.m_MeshRenderer.material, stressCol);
                SetMaterialColor(edge.m_SpringCoilVisualization.m_BackLink.m_MeshRenderer.material, stressCol);
            }

            if (edge.m_Material.m_MaterialType == BridgeMaterialType.REINFORCED_ROAD)
            {
                SetMaterialColor(edge.m_MeshRenderer.materials[1], stressCol);
            }

            if (edge.m_HydraulicEdgeVisualization != null)
            {
                edge.m_HydraulicEdgeVisualization.SetStressColorForEdge(edge, 0.0f);
                foreach (MeshRenderer meshRenderer in edge.m_HydraulicEdgeVisualization.GetComponentsInChildren<MeshRenderer>())
                {
                    SetMaterialColor(meshRenderer.material, stressCol);
                }
            }

            edge.SetStressColor(0.0f);
            SetMaterialColor(edge.m_MeshRenderer.material, stressCol);
        }

        private static void RestoreColor(Material m)
        {
            if (originalColor.ContainsKey(m))
            {
                m.color = originalColor[m];
            }
        }

        private static void SetOriginalColor()
        {
            foreach (BridgeEdge edge in BridgeEdges.m_Edges)
            {
                if (edge.m_Material.m_MaterialType == BridgeMaterialType.ROPE || edge.m_Material.m_MaterialType == BridgeMaterialType.CABLE)
                {
                    foreach (BridgeRope bridgeRope in BridgeRopes.m_BridgeRopes)
                    {
                        if ((UnityEngine.Object)bridgeRope.m_ParentEdge == (UnityEngine.Object)edge)
                        {
                            foreach (BridgeLink link in bridgeRope.m_Links)
                            {
                                RestoreColor(((MeshRenderer)bridgeLinkMeshRendererField.GetValue(link)).material);
                            }
                        }
                    }
                }

                if (edge.m_Material.m_MaterialType == BridgeMaterialType.SPRING)
                {
                    RestoreColor(edge.m_SpringCoilVisualization.m_FrontLink.m_MeshRenderer.material);
                    RestoreColor(edge.m_SpringCoilVisualization.m_BackLink.m_MeshRenderer.material);
                }

                if (edge.m_Material.m_MaterialType == BridgeMaterialType.REINFORCED_ROAD)
                {
                    RestoreColor(edge.m_MeshRenderer.materials[1]);
                }

                if (edge.m_HydraulicEdgeVisualization != null)
                {
                    foreach (MeshRenderer meshRenderer in edge.m_HydraulicEdgeVisualization.GetComponentsInChildren<MeshRenderer>())
                    {
                        RestoreColor(meshRenderer.material);
                    }
                }

                RestoreColor(edge.m_MeshRenderer.material);
            }
        }

        private static void SetDebrisColor(Material matNew, Material matBroken)
        {
            if (!originalColor.ContainsKey(matNew))
            {
                if (originalColor.ContainsKey(matBroken))
                {
                    originalColor[matNew] = originalColor[matBroken];
                }
            }
        }

        [HarmonyPatch(typeof(ReplayCamera), "OnPreRender")]
        static class Patch_ReplayCamera_OnPreRender
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                if (!modEnabled.Value) return;
                if (!replayStress.Value) return;

                if (Profile.m_StressViewEnabled)
                {
                    BridgeEdges.SetStressColor();
                }
            }
        }

        [HarmonyPatch(typeof(MainCamera), "OnPreRender")]
        static class Patch_MainCamera_OnPreRender
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                if (!modEnabled.Value) return;

                if (maxStressHotkey.Value.IsDown())
                {
                    maxStressSelected = !maxStressSelected;
                }

                if (stressExponentKey.Value.IsDown())
                {
                    selectedExponent = (selectedExponent + 1) % 3;
                }
            }
        }

        [HarmonyPatch(typeof(BridgeEdges), "SetStressColor")]
        static class Patch_BridgeEdges_SetStressColor
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                if (!modEnabled.Value) return;

                float worstEdgeStress = 0.0f;
                BridgeEdge worstEdge = null;
                foreach (BridgeEdge edge in BridgeEdges.m_Edges)
                {
                    float stress = 0.0f;
                    if (edge.m_PhysicsEdge)
                    {
                        stress = maxStressSelected ? maxStress[edge.m_PhysicsEdge] : curStress[edge.m_PhysicsEdge];
                    }
                    bool compression = stress > 0.0;
                    stress = edge.m_IsBroken ? 0.0f : stress;
                    stress = Mathf.Abs(stress);
                    if(stress > worstEdgeStress)
                    {
                        worstEdgeStress = stress;
                        worstEdge = edge;
                    }
                    stress = Mathf.Pow(stress, stressExponent[selectedExponent].Value);
                    Vector3 hsv = compression ? Vector3.Lerp(colCompressionMin.Value, colCompressionMax.Value, stress) : Vector3.Lerp(colTensionMin.Value, colTensionMax.Value, stress);
                    Color stressCol = Color.HSVToRGB(hsv.x, hsv.y, hsv.z);

                    SetEdgeColor(edge, stressCol);
                }

                if (highlightWorst.Value && worstEdge) SetEdgeColor(worstEdge, colHighlight.Value);
            }
        }

        [HarmonyPatch(typeof(BridgeRopes), "Add")]
        static class Patch_BridgeRopes_Add
        {
            [HarmonyPrefix]
            static void Prefix(PolyPhysics.Rope rope)
            {
                if (!modEnabled.Value) return;

                BridgeEdge edge = rope.userData != null ? (BridgeEdge)rope.userData : (BridgeEdge)rope.edge.userData;
                RestoreColor(edge.m_MeshRenderer.material);
            }
        }

        [HarmonyPatch(typeof(BridgeEdges), "SetOriginalColor")]
        static class Patch_BridgeEdges_SetOriginalColor
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                if (!modEnabled.Value) return;

                SetOriginalColor();
            }
        }

        [HarmonyPatch(typeof(PolyPhysics.BridgeEdgeListener), "CreateBridgeEdgeFromEdge")]
        static class Patch_BridgeEdgeListener_CreateBridgeEdgeFromEdge
        {
            [HarmonyPostfix]
            static void Postfix(ref BridgeEdge __result)
            {
                if (!modEnabled.Value) return;

                newEdge = __result;
            }
        }

        [HarmonyPatch(typeof(PolyPhysics.BridgeEdgeListener), "CreateDebris")]
        static class Patch_BridgeEdgeListener_CreateDebris
        {
            [HarmonyPostfix]
            static void Postfix(ref Poly.Physics.EdgeHandle e, ref BridgeEdge brokenEdge)
            {
                if (!modEnabled.Value) return;

                if (!newEdge)
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

                newEdge = null;
            }
        }

        [HarmonyPatch(typeof(GameStateManager), "ChangeState")]
        static class Patch_GameStateManager_ChangeState
        {
            [HarmonyPrefix]
            static void Prefix(GameState state)
            {
                if (!modEnabled.Value) return;

                if(GameStateManager.GetState() == GameState.SIM && state == GameState.BUILD)
                {
                    if (markWorst.Value && GameStateSim.m_NumBridgeBreaks == 0)
                    {
                        float worstStress = 0.0f;
                        PolyPhysics.Edge worstEdge = null;
                        foreach (var edge in maxStress)
                        {
                            if (Mathf.Abs(edge.Value) > worstStress)
                            {
                                worstStress = Mathf.Abs(edge.Value);
                                worstEdge = edge.Key;
                            }
                        }
                        BridgeEdge byPhysicsEdge = BridgeEdges.FindByPhysicsEdge(worstEdge);
                        if (byPhysicsEdge && worstStress > 0.01f)
                        {
                            BridgeEdgeProxy proxy = new BridgeEdgeProxy(byPhysicsEdge);
                            proxy.m_NodeA_Guid = (UnityEngine.Object)byPhysicsEdge.m_StartSimJointA != (UnityEngine.Object)null ? byPhysicsEdge.m_StartSimJointA.m_Guid : proxy.m_NodeA_Guid;
                            proxy.m_NodeB_Guid = (UnityEngine.Object)byPhysicsEdge.m_StartSimJointB != (UnityEngine.Object)null ? byPhysicsEdge.m_StartSimJointB.m_Guid : proxy.m_NodeB_Guid;
                            GameStateBuild.SetFirstBreakEdge(proxy);
                        }
                    }
                }

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
                if (!modEnabled.Value) return;

                foreach (BridgeEdge edge in BridgeEdges.m_Edges)
                {
                    if (edge.m_PhysicsEdge != null)
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
