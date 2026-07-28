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
    /// One-click Phase 2 worlds: rebuilds every grey-box circuit in <see cref="Layouts"/> (same box
    /// construction as TestTrackBuilder, minus the slalom/ramp/crates so bots can lap cleanly). Each
    /// scene gets a 24-waypoint TrackPath along its corridor centreline, 1 player car and 7
    /// skill-varied bots gridded on the south straight, RaceManager + RaceHud, a RunRig, and a
    /// Build Settings entry — RunDirector rotates through the scenes one per race, so it can only
    /// reach a layout that is registered.
    ///
    /// Geometry is derived from each layout's two boxes rather than hand-written, so a new circuit is
    /// one entry in the table. Reuses the Phase 1 materials/prefabs (bootstraps them via
    /// TestTrackBuilder.Build() if missing). Every scene is regenerated from scratch — hand edits to
    /// them do not survive, including anything the inspector serialized onto the RunRig.
    /// </summary>
    public static class RaceTrackBuilder
    {
        private const string MaterialsDir = "Assets/_Project/Settings/Materials";
        private const string PrefabsDir = "Assets/_Project/Prefabs";
        private const string ScenesDir = "Assets/_Project/Scenes";

        private const int RaceLaps = 3;
        private const float CutoffFraction = 0.15f;

        /// <summary>
        /// One greybox circuit: a rectangular corridor between an outer wall ring and a solid inner
        /// block, both centred on the origin. Everything else derives from those two boxes — the
        /// centreline runs midway between them — so the only knobs are corridor width, track size and
        /// corner radius. Those happen to be the three things that actually change how a race here
        /// plays: how hard passing is, how long a lap takes, and how much Grip matters against Power.
        ///
        /// The corridor must be the SAME width on the long and short sides (Outer-Inner equal in both
        /// axes), or the centreline stops being centred and the grid slots drift toward a wall.
        /// </summary>
        private struct TrackLayout
        {
            public string SceneName;
            public string Character;      // what this layout is FOR — shown in the build log
            public float OuterHalfX, OuterHalfZ;
            public float InnerHalfX, InnerHalfZ;
            public float CornerRadiusM;

            /// <summary>Centreline half-extents: midway between the block and the wall.</summary>
            public float CentreHalfX => (OuterHalfX + InnerHalfX) * 0.5f;
            public float CentreHalfZ => (OuterHalfZ + InnerHalfZ) * 0.5f;

            /// <summary>Drivable width between block and wall.</summary>
            public float CorridorWidthM => OuterHalfX - InnerHalfX;

            public string ScenePath => $"{ScenesDir}/{SceneName}.unity";
        }

        // Eight layouts — one signature venue per circuit of the 24-race season (doc 06: "8 circuits,
        // 8 signature tracks", greyboxed as drivable layouts FIRST, art pass later; doc 08 open
        // question 5). Varied along the axes that change racing rather than looks: room to pass, lap
        // length, and corner speed. Each new entry's Character notes the doc-06 theme it will wear
        // when the art pass lands. Circuit order lives in RunDirector.DefaultRaceScenes.
        private static readonly TrackLayout[] Layouts =
        {
            // The original, unchanged: 40 m of room, ~686 m lap, 20 m corners. The balanced baseline.
            new TrackLayout
            {
                SceneName = "RaceTest", Character = "balanced baseline",
                OuterHalfX = 130f, OuterHalfZ = 90f, InnerHalfX = 90f, InnerHalfZ = 50f,
                CornerRadiusM = 20f,
            },
            // Half the room (22 m). Passing is a fight and contact is near-constant, so position
            // sticks and attack parts earn their slot. The corner pinches to ~6 m of clearance — that
            // pinch IS the character; it's the one place a rival cannot go around you.
            new TrackLayout
            {
                SceneName = "RaceGauntlet", Character = "narrow — contact and track position",
                OuterHalfX = 130f, OuterHalfZ = 90f, InnerHalfX = 108f, InnerHalfZ = 68f,
                CornerRadiusM = 22f,
            },
            // Two ~260 m straights and 30 m sweepers: top speed and slipstream over cornering, and the
            // longest lap of the three (~808 m), so the survival cutoff has real room to bite.
            new TrackLayout
            {
                SceneName = "RaceSpeedway", Character = "long straights — slipstream and Power",
                OuterHalfX = 180f, OuterHalfZ = 75f, InnerHalfX = 140f, InnerHalfZ = 35f,
                CornerRadiusM = 30f,
            },
            // Fast and flowing: a 40 m corridor into 34 m sweepers you barely lift for. Grip cars
            // carry speed through, Power cars claw it back on the exits. (doc 06 #2, Coastal Highway.)
            new TrackLayout
            {
                SceneName = "RaceCoastal", Character = "fast sweepers — commitment and exit speed (doc06: Coastal Highway)",
                OuterHalfX = 170f, OuterHalfZ = 95f, InnerHalfX = 130f, InnerHalfZ = 55f,
                CornerRadiusM = 34f,
            },
            // Mid-size and workmanlike: a 28 m corridor with 16 m corners — proper braking zones
            // without Gauntlet's claustrophobia. The all-skills mid-season check. (doc 06 #3,
            // Industrial Docks.)
            new TrackLayout
            {
                SceneName = "RaceDocks", Character = "mid-width, real braking zones (doc06: Industrial Docks)",
                OuterHalfX = 120f, OuterHalfZ = 85f, InnerHalfX = 92f, InnerHalfZ = 57f,
                CornerRadiusM = 16f,
            },
            // Long-ish lap squeezed to 24 m with 14 m corners: pace lives on the straights but every
            // corner entry is a contested door. Contact-heavy by geometry. (doc 06 #4, Desert Canyon.)
            new TrackLayout
            {
                SceneName = "RaceCanyon", Character = "narrow doors at speed — contested entries (doc06: Desert Canyon)",
                OuterHalfX = 140f, OuterHalfZ = 80f, InnerHalfX = 116f, InnerHalfZ = 56f,
                CornerRadiusM = 14f,
            },
            // The thin one: a 20 m ribbon around the second-longest lap. Single-file at pace —
            // overtakes are built over half a lap of pressure, and defense is a real skill.
            // (doc 06 #7, Frozen Lake's slot until surface zones exist.)
            new TrackLayout
            {
                SceneName = "RaceRibbon", Character = "thin and long — pressure racing, earned passes (doc06: Frozen Lake slot)",
                OuterHalfX = 178f, OuterHalfZ = 80f, InnerHalfX = 158f, InnerHalfZ = 60f,
                CornerRadiusM = 20f,
            },
            // The showcase: the biggest lap of the eight (~990 m) on a full 40 m corridor with 26 m
            // corners — every skill the season taught, at scale, for the finale. (doc 06 #8, Final
            // Circuit.)
            new TrackLayout
            {
                SceneName = "RaceColosseum", Character = "the big one — everything, at scale (doc06: Final Circuit)",
                OuterHalfX = 190f, OuterHalfZ = 110f, InnerHalfX = 150f, InnerHalfZ = 70f,
                CornerRadiusM = 26f,
            },
        };

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

        [MenuItem("Shitboxer/Build Race Scenes")]
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

            if (!AssetDatabase.IsValidFolder(ScenesDir))
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");

            foreach (TrackLayout layout in Layouts)
                BuildLayout(layout, ground, wall, trackPhys, gripPrefab, powerPrefab);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Shitboxer] Built {Layouts.Length} race scenes and added them to Build Settings. " +
                      "Open any and press Play; RunDirector rotates through them one per race.");
        }

        /// <summary>Regenerates one layout's scene from scratch, run-mode wired and build-settings registered.</summary>
        private static void BuildLayout(TrackLayout layout, Material ground, Material wall,
            PhysicsMaterial trackPhys, GameObject gripPrefab, GameObject powerPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            light.shadows = LightShadows.Soft;

            BuildWorld(layout, ground, wall, trackPhys);
            TrackPath trackPath = BuildTrackPath(layout);
            List<VehicleController> cars = SpawnGrid(layout, scene, gripPrefab, powerPrefab, trackPath,
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
            rig.AddComponent<RaceDebugLogger>().Configure(manager);
            // Race telemetry: watches what the player does to each rival so the career memory layer has
            // something to learn from. Purely observational — it applies no forces and drives nothing.
            rig.AddComponent<RaceObserverHost>().Configure(manager, trackPath);

            EditorSceneManager.SaveScene(scene, layout.ScenePath);

            // A scene the run can't load is a scene the run will never show. Every layout has to be in
            // Build Settings or SceneManager.LoadScene throws the moment the rotation reaches it.
            EnsureInBuildSettings(layout.ScenePath);

            // Rebuilding from scratch drops the RunRig (RunDirector). Without it the race completes but
            // never advances to the garage — the player just keeps driving a finished track. Every
            // layout gets one so any of them is play-ready standalone; RunDirector is a
            // DontDestroyOnLoad singleton, so the duplicates that load mid-run destroy themselves.
            MetaAssetsBuilder.AddRunModeToRaceScene(layout.ScenePath);

            Debug.Log($"[Shitboxer] Built {layout.SceneName} — {layout.Character}: " +
                      $"{layout.CorridorWidthM:F0} m corridor, {layout.CornerRadiusM:F0} m corners.");
        }

        /// <summary>Adds a scene to Build Settings if it isn't already there (idempotent).</summary>
        private static void EnsureInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
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

        /// <summary>Same box construction as TestTrackBuilder, sized from the layout, no mid-corridor obstacles.</summary>
        private static void BuildWorld(TrackLayout layout, Material ground, Material wall, PhysicsMaterial trackPhys)
        {
            var world = new GameObject("World").transform;

            StaticBox(world, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(400f, 1f, 400f), ground, trackPhys);

            // Arena perimeter so nothing escapes. Fixed 400x400 — every layout's outer ring fits inside.
            StaticBox(world, "Perimeter_N", new Vector3(0f, 2f, 199.5f), new Vector3(400f, 4f, 1f), wall, trackPhys);
            StaticBox(world, "Perimeter_S", new Vector3(0f, 2f, -199.5f), new Vector3(400f, 4f, 1f), wall, trackPhys);
            StaticBox(world, "Perimeter_E", new Vector3(199.5f, 2f, 0f), new Vector3(1f, 4f, 400f), wall, trackPhys);
            StaticBox(world, "Perimeter_W", new Vector3(-199.5f, 2f, 0f), new Vector3(1f, 4f, 400f), wall, trackPhys);

            // The circuit: a corridor between the outer wall ring and the solid inner block. Wall slabs
            // overhang by 1 m (the +1 on each span) so the corners close cleanly instead of leaving a gap
            // a car could squeeze through.
            Ring(world, "Outer", layout.OuterHalfX, layout.OuterHalfZ, wall, trackPhys);
            Ring(world, "Inner", layout.InnerHalfX, layout.InnerHalfZ, wall, trackPhys);

            // Start/finish stripe across the south straight (visual only, no collider).
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "StartFinishLine";
            Object.DestroyImmediate(stripe.GetComponent<Collider>());
            stripe.transform.SetParent(world);
            stripe.transform.position = new Vector3(0f, 0.02f, -layout.CentreHalfZ);
            stripe.transform.localScale = new Vector3(1.5f, 0.04f, layout.CorridorWidthM - 1f);
            stripe.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/CrashBox.mat");
        }

        /// <summary>Four wall slabs forming an axis-aligned rectangle at the given half-extents.</summary>
        private static void Ring(Transform world, string prefix, float halfX, float halfZ,
            Material wall, PhysicsMaterial trackPhys)
        {
            float spanX = halfX * 2f + 1f;
            float spanZ = halfZ * 2f + 1f;
            StaticBox(world, $"{prefix}_N", new Vector3(0f, 1.25f, halfZ), new Vector3(spanX, 2.5f, 1f), wall, trackPhys);
            StaticBox(world, $"{prefix}_S", new Vector3(0f, 1.25f, -halfZ), new Vector3(spanX, 2.5f, 1f), wall, trackPhys);
            StaticBox(world, $"{prefix}_E", new Vector3(halfX, 1.25f, 0f), new Vector3(1f, 2.5f, spanZ), wall, trackPhys);
            StaticBox(world, $"{prefix}_W", new Vector3(-halfX, 1.25f, 0f), new Vector3(1f, 2.5f, spanZ), wall, trackPhys);
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

        private static TrackPath BuildTrackPath(TrackLayout layout)
        {
            var go = new GameObject("TrackPath");
            var trackPath = go.AddComponent<TrackPath>();

            List<Vector3> points = BuildCenterlineWaypoints(layout);
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
        /// Corridor centreline: a rectangle through the layout's centre half-extents with rounded
        /// corners, counter-clockwise (east along the south straight). WP_00 = start/finish at
        /// (0, -CentreHalfZ). 24 points for every layout — the shape scales, the topology doesn't.
        /// At the shipped RaceTest layout this reproduces the previous hand-written waypoints exactly.
        /// </summary>
        private static List<Vector3> BuildCenterlineWaypoints(TrackLayout layout)
        {
            const float y = 0.25f;
            float cx = layout.CentreHalfX;
            float cz = layout.CentreHalfZ;
            float r = layout.CornerRadiusM;
            float ax = cx - r;          // corner-arc centres sit one radius in from each extent
            float az = cz - r;
            float midX = ax * 0.5f;     // mid-straight point, so the spline can't sag between corners

            var pts = new List<Vector3>
            {
                new Vector3(0f, y, -cz),    // start/finish
                new Vector3(midX, y, -cz),
            };
            AddCornerArc(pts, new Vector3(ax, y, -az), r, -90f, 0f);    // SE corner
            pts.Add(new Vector3(cx, y, 0f));
            AddCornerArc(pts, new Vector3(ax, y, az), r, 0f, 90f);      // NE corner
            pts.Add(new Vector3(midX, y, cz));
            pts.Add(new Vector3(0f, y, cz));
            pts.Add(new Vector3(-midX, y, cz));
            AddCornerArc(pts, new Vector3(-ax, y, az), r, 90f, 180f);   // NW corner
            pts.Add(new Vector3(-cx, y, 0f));
            AddCornerArc(pts, new Vector3(-ax, y, -az), r, 180f, 270f); // SW corner
            pts.Add(new Vector3(-midX, y, -cz));
            return pts; // 24 points
        }

        private static void AddCornerArc(List<Vector3> pts, Vector3 centre, float radius, float fromDeg, float toDeg)
        {
            const int steps = 3; // 4 points including both tangent points
            for (int i = 0; i <= steps; i++)
            {
                float angle = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                pts.Add(centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        // ------------------------------------------------------------------ cars

        private static List<VehicleController> SpawnGrid(TrackLayout layout, Scene scene,
            GameObject gripPrefab, GameObject powerPrefab, TrackPath trackPath, out VehicleController playerCar)
        {
            var cars = new List<VehicleController>(8);
            Quaternion facingEast = Quaternion.Euler(0f, 90f, 0f); // direction of travel on the south straight

            // Columns straddle the south straight's centreline. 6 m on a 40 m corridor, but narrower
            // layouts get proportionally less so the outside column never starts against a wall.
            float colOffset = Mathf.Min(6f, layout.CorridorWidthM * 0.25f);
            float startZ = -layout.CentreHalfZ;

            // Grid slot 0 = pole. Rows of two march back (-x) from the start line at x = 0. The
            // furthest row sits at x = -35, well inside the shortest layout's south straight.
            Vector3 GridSlot(int index)
            {
                int row = index / 2;
                int col = index % 2;
                return new Vector3(-8f - row * 9f, 1.0f, startZ + (col == 0 ? colOffset : -colOffset));
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
                // Bake the stable grid slot so the run layer can bind a persistent named rival to this car
                // without depending on hierarchy order, which is not contractual and shifts under a rebuild.
                driver.ConfigureRivalSlot(i);
                cars.Add(botGo.GetComponent<VehicleController>());
            }

            return cars;
        }
    }
}
