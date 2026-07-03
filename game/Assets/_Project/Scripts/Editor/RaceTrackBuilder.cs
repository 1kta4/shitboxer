using System.Collections.Generic;
using Shitboxer.Cameras;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shitboxer.Editor
{
    /// <summary>
    /// One-click Phase 2 world: rebuilds the grey-box circuit (same wall constants as
    /// TestTrackBuilder, minus the slalom/ramp/crates so bots can lap cleanly), lays a
    /// 24-waypoint TrackPath along the corridor centreline, grids up 1 player car and
    /// 7 skill-varied bots on the south straight, and wires RaceManager + RaceHud.
    /// Reuses the Phase 1 materials/prefabs (bootstraps them via TestTrackBuilder.Build()
    /// if missing). The RaceTest scene is regenerated from scratch each run.
    /// </summary>
    public static class RaceTrackBuilder
    {
        private const string MaterialsDir = "Assets/_Project/Settings/Materials";
        private const string PrefabsDir = "Assets/_Project/Prefabs";
        private const string ScenesDir = "Assets/_Project/Scenes";
        private const string ScenePath = ScenesDir + "/RaceTest.unity";

        private const float CornerRadiusM = 20f;
        private const int RaceLaps = 3;
        private const float CutoffFraction = 0.15f;

        private struct BotPreset
        {
            public bool UsePowerBox;
            public float CornerMult, Aggression, LookaheadM, LateralOffsetM;

            public BotPreset(bool power, float corner, float aggression, float lookahead, float lateralOffset)
            {
                UsePowerBox = power;
                CornerMult = corner;
                Aggression = aggression;
                LookaheadM = lookahead;
                LateralOffsetM = lateralOffset;
            }
        }

        // 7 personalities so the field strings out instead of driving as a train; lateral
        // offsets spread them across the 40 m corridor so lines cross and contact happens.
        private static readonly BotPreset[] BotPresets =
        {
            new BotPreset(true,  0.95f, 1.00f, 12f,  -6f),
            new BotPreset(false, 1.05f, 1.10f, 10f,   3f),
            new BotPreset(true,  0.85f, 0.85f, 14f,   7f),
            new BotPreset(false, 0.98f, 0.95f, 12f,  -3f),
            new BotPreset(true,  1.00f, 1.05f, 11f,   0f),
            new BotPreset(false, 0.80f, 0.75f, 16f,   5f),
            new BotPreset(true,  0.90f, 0.90f, 13f,  -7f),
        };

        [MenuItem("Shitboxer/Build Race Test Scene")]
        public static void Build()
        {
            EnsurePhase1Assets();

            var ground = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/Ground.mat");
            var wall = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/Wall.mat");
            var trackPhys = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>($"{MaterialsDir}/Track.physicMaterial");
            var gripPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/GripBox.prefab");
            var powerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/PowerBox.prefab");
            if (!ground || !wall || !gripPrefab || !powerPrefab)
            {
                Debug.LogError("[Shitboxer] Phase 1 assets missing even after bootstrap — run 'Shitboxer/Build Grey-Box Test Track' manually first.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            light.shadows = LightShadows.Soft;

            BuildWorld(ground, wall, trackPhys);
            TrackPath trackPath = BuildTrackPath();
            List<VehicleController> cars = SpawnGrid(scene, gripPrefab, powerPrefab, trackPath,
                out VehicleController playerCar);

            // --- Camera on the player ---
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.72f, 0.88f);
            camGo.AddComponent<AudioListener>();
            var chase = camGo.AddComponent<ChaseCamera>();
            camGo.transform.position = playerCar.transform.position
                - playerCar.transform.forward * 8f + Vector3.up * 3f;
            chase.SetTarget(playerCar.transform);

            // --- Race rig ---
            var rig = new GameObject("RaceRig");
            var manager = rig.AddComponent<RaceManager>();
            manager.Configure(trackPath, cars, RaceLaps, CutoffFraction);
            rig.AddComponent<RaceHud>().Configure(manager, playerCar);
            rig.AddComponent<RaceDebugLogger>().Configure(manager);

            if (!AssetDatabase.IsValidFolder(ScenesDir))
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Shitboxer] Race test scene built. Open {ScenePath} and press Play — 3 laps, 15% survival cutoff.");
        }

        private static void EnsurePhase1Assets()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/GripBox.prefab") == null ||
                AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/Ground.mat") == null)
            {
                TestTrackBuilder.Build();
            }
        }

        // ------------------------------------------------------------------ world

        /// <summary>Same circuit constants as TestTrackBuilder, without the mid-corridor obstacles.</summary>
        private static void BuildWorld(Material ground, Material wall, PhysicsMaterial trackPhys)
        {
            var world = new GameObject("World").transform;

            StaticBox(world, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(400f, 1f, 400f), ground, trackPhys);

            // Arena perimeter so nothing escapes.
            StaticBox(world, "Perimeter_N", new Vector3(0f, 2f, 199.5f), new Vector3(400f, 4f, 1f), wall, trackPhys);
            StaticBox(world, "Perimeter_S", new Vector3(0f, 2f, -199.5f), new Vector3(400f, 4f, 1f), wall, trackPhys);
            StaticBox(world, "Perimeter_E", new Vector3(199.5f, 2f, 0f), new Vector3(1f, 4f, 400f), wall, trackPhys);
            StaticBox(world, "Perimeter_W", new Vector3(-199.5f, 2f, 0f), new Vector3(1f, 4f, 400f), wall, trackPhys);

            // Circuit: 40 m corridor between an outer 260x180 ring and an inner 180x100 block.
            StaticBox(world, "Outer_N", new Vector3(0f, 1.25f, 90f), new Vector3(261f, 2.5f, 1f), wall, trackPhys);
            StaticBox(world, "Outer_S", new Vector3(0f, 1.25f, -90f), new Vector3(261f, 2.5f, 1f), wall, trackPhys);
            StaticBox(world, "Outer_E", new Vector3(130f, 1.25f, 0f), new Vector3(1f, 2.5f, 181f), wall, trackPhys);
            StaticBox(world, "Outer_W", new Vector3(-130f, 1.25f, 0f), new Vector3(1f, 2.5f, 181f), wall, trackPhys);
            StaticBox(world, "Inner_N", new Vector3(0f, 1.25f, 50f), new Vector3(181f, 2.5f, 1f), wall, trackPhys);
            StaticBox(world, "Inner_S", new Vector3(0f, 1.25f, -50f), new Vector3(181f, 2.5f, 1f), wall, trackPhys);
            StaticBox(world, "Inner_E", new Vector3(90f, 1.25f, 0f), new Vector3(1f, 2.5f, 101f), wall, trackPhys);
            StaticBox(world, "Inner_W", new Vector3(-90f, 1.25f, 0f), new Vector3(1f, 2.5f, 101f), wall, trackPhys);

            // Start/finish stripe across the south straight (visual only, no collider).
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "StartFinishLine";
            Object.DestroyImmediate(stripe.GetComponent<Collider>());
            stripe.transform.SetParent(world);
            stripe.transform.position = new Vector3(0f, 0.02f, -70f);
            stripe.transform.localScale = new Vector3(1.5f, 0.04f, 39f);
            stripe.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/CrashBox.mat");
        }

        private static GameObject StaticBox(Transform parent, string name, Vector3 pos, Vector3 scale,
            Material mat, PhysicsMaterial physMat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (physMat) go.GetComponent<Collider>().sharedMaterial = physMat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        // ------------------------------------------------------------------ track path

        private static TrackPath BuildTrackPath()
        {
            var go = new GameObject("TrackPath");
            var trackPath = go.AddComponent<TrackPath>();

            List<Vector3> points = BuildCenterlineWaypoints();
            var waypoints = new List<Transform>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var wp = new GameObject($"WP_{i:00}");
                wp.transform.SetParent(go.transform);
                wp.transform.position = points[i];
                waypoints.Add(wp.transform);
            }

            trackPath.SetWaypoints(waypoints);
            return trackPath;
        }

        /// <summary>
        /// Corridor centreline: rectangle through (+/-110, +/-70) with 20 m rounded corners,
        /// counter-clockwise (east along the south straight). WP_00 = start/finish at (0, -70).
        /// </summary>
        private static List<Vector3> BuildCenterlineWaypoints()
        {
            const float y = 0.25f;
            var pts = new List<Vector3>
            {
                new Vector3(0f, y, -70f),   // start/finish
                new Vector3(45f, y, -70f),
            };
            AddCornerArc(pts, new Vector3(90f, y, -50f), -90f, 0f);    // SE corner
            pts.Add(new Vector3(110f, y, 0f));
            AddCornerArc(pts, new Vector3(90f, y, 50f), 0f, 90f);      // NE corner
            pts.Add(new Vector3(45f, y, 70f));
            pts.Add(new Vector3(0f, y, 70f));
            pts.Add(new Vector3(-45f, y, 70f));
            AddCornerArc(pts, new Vector3(-90f, y, 50f), 90f, 180f);   // NW corner
            pts.Add(new Vector3(-110f, y, 0f));
            AddCornerArc(pts, new Vector3(-90f, y, -50f), 180f, 270f); // SW corner
            pts.Add(new Vector3(-45f, y, -70f));
            return pts; // 24 points
        }

        private static void AddCornerArc(List<Vector3> pts, Vector3 centre, float fromDeg, float toDeg)
        {
            const int steps = 3; // 4 points including both tangent points
            for (int i = 0; i <= steps; i++)
            {
                float angle = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                pts.Add(centre + new Vector3(Mathf.Cos(angle) * CornerRadiusM, 0f, Mathf.Sin(angle) * CornerRadiusM));
            }
        }

        // ------------------------------------------------------------------ cars

        private static List<VehicleController> SpawnGrid(Scene scene, GameObject gripPrefab, GameObject powerPrefab,
            TrackPath trackPath, out VehicleController playerCar)
        {
            var cars = new List<VehicleController>(8);
            Quaternion facingEast = Quaternion.Euler(0f, 90f, 0f); // direction of travel on the south straight

            // Grid slot 0 = pole. Rows of two march back (-x) from the start line at x = 0.
            // Columns sit +/-6 m either side of the z = -70 centreline (corridor is 40 m wide).
            Vector3 GridSlot(int index)
            {
                int row = index / 2;
                int col = index % 2;
                return new Vector3(-8f - row * 9f, 1.0f, col == 0 ? -64f : -76f);
            }

            // Player on pole so the first race is easy to observe.
            var playerGo = (GameObject)PrefabUtility.InstantiatePrefab(gripPrefab, scene);
            playerGo.name = "Player_GripBox";
            playerGo.transform.SetPositionAndRotation(GridSlot(0), facingEast);
            playerCar = playerGo.GetComponent<VehicleController>();
            cars.Add(playerCar);

            for (int i = 0; i < BotPresets.Length; i++)
            {
                BotPreset preset = BotPresets[i];
                GameObject prefab = preset.UsePowerBox ? powerPrefab : gripPrefab;
                var botGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                botGo.name = $"Bot_{i + 1}_{(preset.UsePowerBox ? "Power" : "Grip")}";
                botGo.transform.SetPositionAndRotation(GridSlot(i + 1), facingEast);

                // Bots must not carry the human input provider. Removing a component from a
                // prefab instance is not allowed, so unpack first (scene is regenerated anyway).
                PrefabUtility.UnpackPrefabInstance(botGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Object.DestroyImmediate(botGo.GetComponent<VehicleInputProvider>());

                var driver = botGo.AddComponent<BotDriver>();
                driver.Configure(trackPath, preset.CornerMult, preset.Aggression, preset.LookaheadM, preset.LateralOffsetM);
                cars.Add(botGo.GetComponent<VehicleController>());
            }

            return cars;
        }
    }
}
