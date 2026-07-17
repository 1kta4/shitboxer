using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Structural guard for the one-way FX edge. Shitboxer.Fx (presentation / juice / audio)
    /// references Vehicle + Race and must be referenced by NOBODY — that is what keeps effects able
    /// only to READ simulation state, never write it, so the physics core stays headless-safe.
    ///
    /// The rule is enforced by parsing every .asmdef in the project rather than by trusting a comment,
    /// so it cannot rot: the day someone adds "Shitboxer.Fx" to another assembly's references — by
    /// name or by GUID — this test goes red. Same spirit as the repo's other structural invariants
    /// (Race can't reference Meta, etc.), made executable.
    /// </summary>
    public class FxAssemblyGuardTests : TestBase
    {
        private const string FxName = "Shitboxer.Fx";

        [Serializable]
        private class AsmDef { public string name; public string[] references; }

        private static AsmDef Parse(string path) => JsonUtility.FromJson<AsmDef>(File.ReadAllText(path));

        [Test]
        public void FxAssembly_ExistsAndIsReferencedByNobody()
        {
            string[] asmdefs = Directory.GetFiles(Application.dataPath, "*.asmdef", SearchOption.AllDirectories);

            // Locate Fx's own asmdef and, if Unity has imported it, its GUID — so a GUID-form
            // reference is caught too, not just a by-name one. (This repo references by name today,
            // but the guard shouldn't assume that stays true.)
            string fxPath = Array.Find(asmdefs, p => Parse(p)?.name == FxName);
            Assert.That(fxPath, Is.Not.Null,
                $"{FxName}.asmdef must exist — it is the referenced-by-nobody FX assembly.");

            string fxGuid = null;
            string meta = fxPath + ".meta";
            if (File.Exists(meta))
            {
                Match m = Regex.Match(File.ReadAllText(meta), @"guid:\s*([0-9a-fA-F]{32})");
                if (m.Success) fxGuid = m.Groups[1].Value;
            }

            var offenders = new List<string>();
            foreach (string path in asmdefs)
            {
                AsmDef def = Parse(path);
                if (def == null || def.name == FxName || def.references == null) continue;

                foreach (string reference in def.references)
                {
                    string normalized = reference.Replace("GUID:", "").Replace("guid:", "");
                    if (reference == FxName || (fxGuid != null && normalized == fxGuid))
                    {
                        offenders.Add(def.name);
                        break;
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                $"{FxName} must be referenced by nobody (the one-way FX edge keeps physics headless-safe), "
                + $"but these assemblies reference it: {string.Join(", ", offenders)}");
        }
    }
}
