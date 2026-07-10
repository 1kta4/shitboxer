using System.IO;
using Shitboxer.Cameras;
using Shitboxer.Race;
using Shitboxer.TestDrive;
using Shitboxer.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shitboxer.Editor
{
    /// <summary>
    /// One-click Phase 1 world: builds materials, the two starter car specs (Grip vs Power),
    /// car prefabs, and the grey-box test track scene. Idempotent — spec assets are only
    /// created if missing so hand-tuning in the inspector survives a rebuild; prefabs and
    /// the scene are regenerated from scratch each run.
    /// </summary>
    public static class TestTrackBuilder
    {
        private const string SettingsDir = "Assets/_Project/Settings";
        private const string MaterialsDir = "Assets/_Project/Settings/Materials";
        private const string VehiclesDir = "Assets/_Project/Settings/Vehicles";
        private const string PrefabsDir = "Assets/_Project/Prefabs";
        private const string ScenePath = "Assets/_Project/Scenes/TestTrack.unity";
        private const int VehicleLayer = 8;

        [MenuItem("Shitboxer/Build Grey-Box Test Track")]
        public static void Build()
        {
            EnsureFolders();
            var mats = BuildMaterials();
            var physMats = BuildPhysicsMaterials();

            VehicleSpecAsset gripSpec = EnsureSpecAsset($"{VehiclesDir}/GripBox.asset", ConfigureGripBox);
            VehicleSpecAsset powerSpec = EnsureSpecAsset($"{VehiclesDir}/PowerBox.asset", ConfigurePowerBox);

            GameObject gripPrefab = BuildCarPrefab("GripBox", gripSpec, mats.gripCar, physMats.carBody);
            GameObject powerPrefab = BuildCarPrefab("PowerBox", powerSpec, mats.powerCar, physMats.carBody);

            BuildScene(gripPrefab, powerPrefab, mats, physMats);

            AssetDatabase.SaveAssets();
            Debug.Log("[Shitboxer] Grey-box test track built. Open Assets/_Project/Scenes/TestTrack.unity and press Play.");
        }

        private static void EnsureFolders()
        {
            foreach (string dir in new[] { SettingsDir, MaterialsDir, VehiclesDir, PrefabsDir })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string parent = Path.GetDirectoryName(dir)!.Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
                }
            }
        }

        // ------------------------------------------------------------------ specs

        private static VehicleSpecAsset EnsureSpecAsset(string path, System.Action<VehicleSpec> configure)
        {
            var existing = AssetDatabase.LoadAssetAtPath<VehicleSpecAsset>(path);
            if (existing != null) return existing;

            var asset = ScriptableObject.CreateInstance<VehicleSpecAsset>();
            configure(asset.Spec);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Light, planted, AWD — the "corners on rails, wins nothing on the straight" car.</summary>
        private static void ConfigureGripBox(VehicleSpec s)
        {
            s.MassKg = 1050f;
            s.CentreOfMassOffset = new Vector3(0f, -0.4f, 0f);
            s.Layout = DriveLayout.AllWheelDrive;

            s.FrontTyre = s.RearTyre = new TyreSpec
            {
                PeakMu = 1.32f,
                SlideMu = 1.1f,
                PeakSlipAngleDeg = 6f,
                PeakSlipRatio = 0.11f,
                FalloffSharpness = 0.6f,   // forgiving breakaway
                LoadSensitivity = 0.08f,
                RatedLoadN = 3200f,
            };

            s.SpringRateNPerM = 68000f;
            s.DamperRateNPerMps = 6200f;
            s.AntiRollBarNPerM = 12000f;
            s.DownforceCoeff = 2.4f;
            s.DragCoeff = 0.36f;
            s.ExtraGravity = 1.0f;
            s.YawAssist = 0.7f;
            s.LateralVelocityDamping = 2.2f;
            s.FlatRideDamping = 2.0f;

            s.Engine = new EngineSpec
            {
                IdleRpm = 1000f,
                RedlineRpm = 7400f,
                PeakTorqueNm = 205f,
                PeakTorqueRpm = 5200f,
                LowEndFraction = 0.6f,
                TopEndFraction = 0.8f,
                EngineBrakeNm = 70f,
            };
            s.GearRatios = new[] { 3.4f, 2.2f, 1.6f, 1.25f, 1.0f };
            s.FinalDriveRatio = 4.7f;
            s.UpshiftRpm = 7000f;
            s.DownshiftRpm = 3800f;

            s.BrakeTorqueNm = 2800f;
            s.HandbrakeGripFactor = 0.6f;
        }

        /// <summary>Heavy, loose, RWD torque monster — the "wins the drag race, prays in corners" car.</summary>
        private static void ConfigurePowerBox(VehicleSpec s)
        {
            s.MassKg = 1350f;
            s.CentreOfMassOffset = new Vector3(0f, -0.32f, 0.15f); // slight nose weight, taller CoM
            s.Layout = DriveLayout.RearWheelDrive;

            s.FrontTyre = new TyreSpec
            {
                PeakMu = 1.12f,
                SlideMu = 0.95f,
                PeakSlipAngleDeg = 7f,
                PeakSlipRatio = 0.13f,
                FalloffSharpness = 0.9f,
                LoadSensitivity = 0.1f,
                RatedLoadN = 3800f,
            };
            s.RearTyre = new TyreSpec
            {
                PeakMu = 1.08f,            // rear still lets go first, but not knife-edged
                SlideMu = 0.95f,
                PeakSlipAngleDeg = 7.5f,
                PeakSlipRatio = 0.14f,
                FalloffSharpness = 0.8f,
                LoadSensitivity = 0.1f,
                RatedLoadN = 3800f,
            };

            s.SpringRateNPerM = 56000f;
            s.DamperRateNPerMps = 5300f;
            s.AntiRollBarNPerM = 6000f;
            s.DownforceCoeff = 0.6f;
            s.DragCoeff = 0.4f;
            s.ExtraGravity = 0.8f;
            s.YawAssist = 0.45f;
            s.LateralVelocityDamping = 1.2f;
            s.FlatRideDamping = 1.5f;

            s.Engine = new EngineSpec
            {
                IdleRpm = 850f,
                RedlineRpm = 6400f,
                PeakTorqueNm = 360f,
                PeakTorqueRpm = 3800f,
                LowEndFraction = 0.6f,     // still shovey, no longer instant wheelspin
                TopEndFraction = 0.65f,
                EngineBrakeNm = 90f,
            };
            s.GearRatios = new[] { 3.0f, 1.9f, 1.4f, 1.1f, 0.9f };
            s.FinalDriveRatio = 4.1f;
            s.UpshiftRpm = 6100f;
            s.DownshiftRpm = 3000f;

            s.BrakeTorqueNm = 3000f;
            s.HandbrakeGripFactor = 0.5f;
        }

        // ------------------------------------------------------------------ materials

        private struct Mats
        {
            public Material ground, wall, obstacle, ramp, crashBox, gripCar, powerCar, wheel;
        }

        private struct PhysMats
        {
            public PhysicsMaterial track, carBody;
        }

        private static Mats BuildMaterials()
        {
            return new Mats
            {
                ground = LitMaterial("Ground", new Color(0.35f, 0.35f, 0.37f)),
                wall = LitMaterial("Wall", new Color(0.22f, 0.22f, 0.25f)),
                obstacle = LitMaterial("Obstacle", new Color(0.9f, 0.5f, 0.1f)),
                ramp = LitMaterial("Ramp", new Color(0.2f, 0.45f, 0.85f)),
                crashBox = LitMaterial("CrashBox", new Color(0.9f, 0.8f, 0.15f)),
                gripCar = LitMaterial("GripCar", new Color(0.15f, 0.7f, 0.3f)),
                powerCar = LitMaterial("PowerCar", new Color(0.8f, 0.15f, 0.15f)),
                wheel = LitMaterial("Wheel", new Color(0.08f, 0.08f, 0.08f)),
            };
        }

        private static Material LitMaterial(string name, Color color)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static PhysMats BuildPhysicsMaterials()
        {
            // Tyres are simulated by raycast, so these only govern chassis scrapes and crashes:
            // low friction so wall contact grinds instead of gluing, a touch of bounce for weight.
            var track = LoadOrCreatePhysMat("Track", 0.4f, 0.05f);
            var carBody = LoadOrCreatePhysMat("CarBody", 0.15f, 0.1f);
            carBody.frictionCombine = PhysicsMaterialCombine.Minimum;
            carBody.bounceCombine = PhysicsMaterialCombine.Maximum;
            return new PhysMats { track = track, carBody = carBody };
        }

        private static PhysicsMaterial LoadOrCreatePhysMat(string name, float friction, float bounce)
        {
            string path = $"{MaterialsDir}/{name}.physicMaterial";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (pm == null)
            {
                pm = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(pm, path);
            }
            pm.staticFriction = friction;
            pm.dynamicFriction = friction;
            pm.bounciness = bounce;
            EditorUtility.SetDirty(pm);
            return pm;
        }

        // ------------------------------------------------------------------ car prefab

        private static GameObject BuildCarPrefab(string name, VehicleSpecAsset spec, Material bodyMat, PhysicsMaterial physMat)
        {
            var root = new GameObject(name);
            try
            {
                root.layer = VehicleLayer;
                var body = root.AddComponent<Rigidbody>();
                body.mass = spec.Spec.MassKg;

                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.15f, 0f);
                box.size = new Vector3(1.7f, 0.9f, 4.0f);
                box.sharedMaterial = physMat;

                // Visual body: a box plus a cabin so orientation reads at a glance.
                AddVisual(root.transform, PrimitiveType.Cube, "BodyVisual",
                    new Vector3(0f, 0.05f, 0f), new Vector3(1.7f, 0.7f, 4.0f), bodyMat);
                AddVisual(root.transform, PrimitiveType.Cube, "CabinVisual",
                    new Vector3(0f, 0.65f, -0.4f), new Vector3(1.4f, 0.5f, 1.6f), bodyMat);

                var controller = root.AddComponent<VehicleController>();
                controller.SetSpec(spec);
                controller.SetGroundMask(~(1 << VehicleLayer));

                var wheels = new Transform[VehicleSim.WheelCount];
                var sim = new VehicleSim(spec.Spec); // just for attach positions
                string[] wheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                {
                    var pivot = new GameObject(wheelNames[i]);
                    pivot.transform.SetParent(root.transform, false);
                    pivot.transform.localPosition = sim.WheelLocalPosition(i);
                    pivot.layer = VehicleLayer;

                    float r = spec.Spec.WheelRadiusM;
                    var tyre = AddVisual(pivot.transform, PrimitiveType.Cylinder, "Tyre",
                        Vector3.zero, new Vector3(r * 2f, 0.125f, r * 2f), null);
                    tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    tyre.GetComponent<MeshRenderer>().sharedMaterial =
                        AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/Wheel.mat");
                    wheels[i] = pivot.transform;
                }
                controller.SetWheelVisuals(wheels);

                root.AddComponent<VehicleInputProvider>();
                // Contact resolver: gives every car weighty collisions (self-rattle) and lets it
                // carry attack parts. Inert by default; RunDirector fills the player's profile.
                root.AddComponent<VehicleCombat>();

                string path = $"{PrefabsDir}/{name}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject AddVisual(Transform parent, PrimitiveType type, string name,
            Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = parent.gameObject.layer;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // visuals must never collide or block wheel rays
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------------ scene

        private static void BuildScene(GameObject gripPrefab, GameObject powerPrefab, Mats mats, PhysMats physMats)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            light.shadows = LightShadows.Soft;

            // --- Static world ---
            var world = new GameObject("World").transform;

            StaticBox(world, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(400f, 1f, 400f), mats.ground, physMats.track);

            // Arena perimeter so nothing escapes.
            StaticBox(world, "Perimeter_N", new Vector3(0f, 2f, 199.5f), new Vector3(400f, 4f, 1f), mats.wall, physMats.track);
            StaticBox(world, "Perimeter_S", new Vector3(0f, 2f, -199.5f), new Vector3(400f, 4f, 1f), mats.wall, physMats.track);
            StaticBox(world, "Perimeter_E", new Vector3(199.5f, 2f, 0f), new Vector3(1f, 4f, 400f), mats.wall, physMats.track);
            StaticBox(world, "Perimeter_W", new Vector3(-199.5f, 2f, 0f), new Vector3(1f, 4f, 400f), mats.wall, physMats.track);

            // Circuit: 40 m corridor between an outer 260x180 ring and an inner 180x100 block.
            StaticBox(world, "Outer_N", new Vector3(0f, 1.25f, 90f), new Vector3(261f, 2.5f, 1f), mats.wall, physMats.track);
            StaticBox(world, "Outer_S", new Vector3(0f, 1.25f, -90f), new Vector3(261f, 2.5f, 1f), mats.wall, physMats.track);
            StaticBox(world, "Outer_E", new Vector3(130f, 1.25f, 0f), new Vector3(1f, 2.5f, 181f), mats.wall, physMats.track);
            StaticBox(world, "Outer_W", new Vector3(-130f, 1.25f, 0f), new Vector3(1f, 2.5f, 181f), mats.wall, physMats.track);
            StaticBox(world, "Inner_N", new Vector3(0f, 1.25f, 50f), new Vector3(181f, 2.5f, 1f), mats.wall, physMats.track);
            StaticBox(world, "Inner_S", new Vector3(0f, 1.25f, -50f), new Vector3(181f, 2.5f, 1f), mats.wall, physMats.track);
            StaticBox(world, "Inner_E", new Vector3(90f, 1.25f, 0f), new Vector3(1f, 2.5f, 101f), mats.wall, physMats.track);
            StaticBox(world, "Inner_W", new Vector3(-90f, 1.25f, 0f), new Vector3(1f, 2.5f, 101f), mats.wall, physMats.track);

            // Slalom pillars mid-way down the south straight.
            for (int i = 0; i < 5; i++)
            {
                float x = -60f + i * 30f;
                float z = i % 2 == 0 ? -78f : -62f;
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = $"Pillar_{i}";
                pillar.transform.SetParent(world);
                pillar.transform.position = new Vector3(x, 1.5f, z);
                pillar.transform.localScale = new Vector3(3f, 1.5f, 3f);
                pillar.GetComponent<MeshRenderer>().sharedMaterial = mats.obstacle;
                pillar.GetComponent<Collider>().sharedMaterial = physMats.track;
                GameObjectUtility.SetStaticEditorFlags(pillar, StaticEditorFlags.BatchingStatic);
            }

            // Jump ramp on the north straight (drive east to west or west to east).
            var rampGo = StaticBox(world, "Ramp", new Vector3(0f, 0.55f, 70f), new Vector3(16f, 0.5f, 10f), mats.ramp, physMats.track);
            rampGo.transform.rotation = Quaternion.Euler(0f, 0f, 8f);

            // --- Crashables ---
            var props = new GameObject("Props").transform;

            // Pyramid of pushable boxes in the west corridor.
            int idx = 0;
            for (int row = 0; row < 3; row++)
            {
                int count = 3 - row;
                for (int c = 0; c < count; c++)
                {
                    var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    crate.name = $"Crate_{idx++}";
                    crate.transform.SetParent(props);
                    crate.transform.localScale = Vector3.one * 1.4f;
                    crate.transform.position = new Vector3(
                        -110f + (c - (count - 1) * 0.5f) * 1.45f,
                        0.7f + row * 1.45f,
                        20f);
                    crate.GetComponent<MeshRenderer>().sharedMaterial = mats.crashBox;
                    crate.GetComponent<Collider>().sharedMaterial = physMats.track;
                    var rb = crate.AddComponent<Rigidbody>();
                    rb.mass = 120f;
                }
            }

            // --- Cars ---
            var grip = (GameObject)PrefabUtility.InstantiatePrefab(gripPrefab, scene);
            grip.transform.SetPositionAndRotation(new Vector3(-105f, 1.0f, -65f), Quaternion.Euler(0f, 90f, 0f));
            var power = (GameObject)PrefabUtility.InstantiatePrefab(powerPrefab, scene);
            power.transform.SetPositionAndRotation(new Vector3(-105f, 1.0f, -75f), Quaternion.Euler(0f, 90f, 0f));

            // --- Camera + rig ---
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.72f, 0.88f);
            camGo.AddComponent<AudioListener>();
            var chase = camGo.AddComponent<ChaseCamera>();
            camGo.transform.position = grip.transform.position - grip.transform.forward * 8f + Vector3.up * 3f;
            chase.SetTarget(grip.transform);

            var rig = new GameObject("TestDriveRig");
            var switcher = rig.AddComponent<CarSwitcher>();
            switcher.Configure(new[]
            {
                grip.GetComponent<VehicleController>(),
                power.GetComponent<VehicleController>(),
            }, chase);
            rig.AddComponent<VehicleDebugHud>().Configure(switcher);

            EditorSceneManager.SaveScene(scene, ScenePath);
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
            go.GetComponent<Collider>().sharedMaterial = physMat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }
    }
}
