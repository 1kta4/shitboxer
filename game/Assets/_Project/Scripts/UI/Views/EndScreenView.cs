using Shitboxer.Meta;
using UnityEngine.UIElements;

namespace Shitboxer.UI.Views
{
    /// <summary>
    /// The run-over / season-cleared result screen as a UI Toolkit plate (v3 — pixel, since it's a menu
    /// takeover like the garage, not an in-race overlay): headline, last-race verdict, a run summary,
    /// the owned-parts list, and two exits — NEW RUN (StartNewRun: same chassis and stake, instant
    /// retry) and MAIN MENU (QuitToMenu: back to the front-end to change car or stop). Replaces the
    /// IMGUI GarageScreen.DrawEndScreen. (The per-track lap records are a later add.)
    /// </summary>
    public sealed class EndScreenView
    {
        public VisualElement Root { get; }

        public EndScreenView(IRunHost host, string headline)
        {
            RunState run = host.Run;

            var screen = new VisualElement();
            screen.AddToClassList("sb-screen");
            screen.AddToClassList("end");

            var panel = new VisualElement();
            panel.AddToClassList("end-panel");

            var h = new Label { text = headline };
            h.AddToClassList("end-headline");
            panel.Add(h);

            if (!string.IsNullOrEmpty(host.LastRaceSummary))
            {
                var sub = new Label { text = host.LastRaceSummary };
                sub.AddToClassList("end-sub");
                panel.Add(sub);
            }

            panel.Add(Section("RUN SUMMARY"));
            panel.Add(Line($"Circuits cleared: {run.CircuitIndex}/{run.TotalCircuits}"));
            panel.Add(Line($"Reached race {run.RaceIndex}/{run.RacesPerCircuit}"));
            panel.Add(Line($"Final money: ${run.Money}"));
            panel.Add(Line($"Lives remaining: {run.Lives}"));

            panel.Add(Section($"OWNED PARTS ({run.OwnedParts.Count})"));
            if (run.OwnedParts.Count == 0)
            {
                panel.Add(Dim("(none — bought nothing this run)"));
            }
            else
            {
                foreach (PartDef part in run.OwnedParts)
                {
                    if (!part) continue;
                    string equipped = run.IsEquipped(part) ? "  — EQUIPPED" : "";
                    panel.Add(Line($"{part.DisplayName}  [{part.Category}]{equipped}"));
                }
            }

            var cta = new Button(() => host.StartNewRun()) { text = "NEW RUN" };
            cta.AddToClassList("end-cta");
            panel.Add(cta);

            var menu = new Button(() => host.QuitToMenu()) { text = "MAIN MENU" };
            menu.AddToClassList("end-cta");
            panel.Add(menu);

            screen.Add(panel);
            Root = screen;
        }

        private static Label Section(string text)
        {
            var l = new Label { text = "-- " + text + " --" };
            l.AddToClassList("end-section");
            return l;
        }

        private static Label Line(string text)
        {
            var l = new Label { text = text };
            l.AddToClassList("end-line");
            return l;
        }

        private static Label Dim(string text)
        {
            var l = new Label { text = text };
            l.AddToClassList("end-line");
            l.AddToClassList("end-dim");
            return l;
        }
    }
}
