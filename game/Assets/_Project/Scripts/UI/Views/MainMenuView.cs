using System;
using System.Collections.Generic;
using Shitboxer.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shitboxer.UI.Views
{
    /// <summary>
    /// The v3 front-end: a title + five switchable sections built into one VisualElement tree —
    /// MAIN (menu), CAR SELECT (chassis pick → start a run), COLLECTION (unlocks), PROFILE (lifetime
    /// stats) and SETTINGS (sound/video/gameplay, saved+applied live). Reads MetaProgress + GameSettings
    /// and calls back to the host to start a run (with the chosen chassis) or quit. Styled entirely by
    /// USS (Tokens + Shitboxer + Garage + MainMenu) with the baked chrome sprites.
    /// </summary>
    public sealed class MainMenuView
    {
        private readonly MetaProgress _meta;
        private readonly GameSettings _settings;
        private readonly PartPool _pool;
        private readonly Action<int> _onStartRun;
        private readonly Action _onQuit;

        public VisualElement Root { get; }

        private VisualElement _body;
        private VisualElement _main, _carSelect, _collection, _profile, _settingsSection;
        private int _chassis;

        public MainMenuView(MetaProgress meta, GameSettings settings, PartPool pool,
            Action<int> onStartRun, Action onQuit)
        {
            _meta = meta ?? new MetaProgress();
            _settings = settings ?? new GameSettings();
            _pool = pool;
            _onStartRun = onStartRun;
            _onQuit = onQuit;

            var screen = new VisualElement();
            screen.AddToClassList("sb-screen");
            screen.AddToClassList("menu");

            var head = new VisualElement();
            head.AddToClassList("menu-head");
            var title = new Label { text = "SHITBOXER" };
            title.AddToClassList("menu-title");
            var tag = new Label { text = "CONTACT RACING ROGUELIKE" };
            tag.AddToClassList("menu-tag");
            head.Add(title);
            head.Add(tag);
            screen.Add(head);

            _body = new VisualElement();
            _body.AddToClassList("menu-body");
            _main = BuildMain();
            _carSelect = BuildCarSelect();
            _collection = BuildCollection();
            _profile = BuildProfile();
            _settingsSection = BuildSettings();
            _body.Add(_main);
            _body.Add(_carSelect);
            _body.Add(_collection);
            _body.Add(_profile);
            _body.Add(_settingsSection);
            screen.Add(_body);

            Root = screen;
            Show(_main);
        }

        private void Show(VisualElement section)
        {
            foreach (VisualElement s in new[] { _main, _carSelect, _collection, _profile, _settingsSection })
                s.style.display = s == section ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── MAIN ───────────────────────────────────────────────────────────────────────────────────
        private VisualElement BuildMain()
        {
            var section = Section();
            var col = new VisualElement();
            col.AddToClassList("menu-list");
            col.Add(MenuButton("START RUN", () => Show(_carSelect), primary: true));
            col.Add(MenuButton("COLLECTION", () => Show(_collection)));
            col.Add(MenuButton("PROFILE", () => Show(_profile)));
            col.Add(MenuButton("SETTINGS", () => Show(_settingsSection)));
            col.Add(MenuButton("QUIT", () => _onQuit?.Invoke()));
            section.Add(col);
            return section;
        }

        // ── CAR SELECT ─────────────────────────────────────────────────────────────────────────────
        private VisualElement BuildCarSelect()
        {
            var section = Section("SELECT CHASSIS");
            var cards = new VisualElement();
            cards.AddToClassList("menu-cards");

            var buttons = new List<VisualElement>();
            foreach (ChassisInfo c in ChassisCatalog.All)
            {
                bool unlocked = ChassisCatalog.IsUnlocked(c, _meta);
                var card = new VisualElement();
                card.AddToClassList("chassis-card");
                if (!unlocked) card.AddToClassList("locked");

                var name = new Label { text = c.Name };
                name.AddToClassList("chassis-name");
                var blurb = new Label { text = unlocked ? c.Blurb : "LOCKED — " + c.Blurb };
                blurb.AddToClassList("chassis-blurb");
                card.Add(name);
                card.Add(blurb);

                if (unlocked)
                {
                    int id = c.Id;
                    card.RegisterCallback<ClickEvent>(_ => SelectChassis(id, buttons));
                }
                cards.Add(card);
                buttons.Add(card);
            }
            section.Add(cards);
            SelectChassis(0, buttons);

            var foot = new VisualElement();
            foot.AddToClassList("menu-foot");
            foot.Add(MenuButton("BACK", () => Show(_main)));
            var start = MenuButton("START >", () => _onStartRun?.Invoke(_chassis), primary: true);
            foot.Add(start);
            section.Add(foot);
            return section;
        }

        private void SelectChassis(int id, List<VisualElement> cards)
        {
            _chassis = id;
            for (int i = 0; i < cards.Count && i < ChassisCatalog.All.Count; i++)
                cards[i].EnableInClassList("selected", ChassisCatalog.All[i].Id == id);
        }

        // ── COLLECTION ─────────────────────────────────────────────────────────────────────────────
        private VisualElement BuildCollection()
        {
            var section = Section("COLLECTION");
            var scroll = new ScrollView();
            scroll.AddToClassList("menu-scroll");

            scroll.Add(GroupLabel("CHASSIS"));
            foreach (ChassisInfo c in ChassisCatalog.All)
                scroll.Add(CollectItem(c.Name, ChassisCatalog.IsUnlocked(c, _meta)));

            scroll.Add(GroupLabel("PARTS"));
            if (_pool != null && _pool.Parts != null)
                foreach (PartDef p in _pool.Parts)
                    if (p != null) scroll.Add(CollectItem($"{p.DisplayName}  [{p.Category}]", true));
            else
                scroll.Add(Dim("(part pool not wired)"));

            scroll.Add(GroupLabel("TRACKS"));
            foreach (string t in new[] { "RaceTest", "RaceGauntlet", "RaceSpeedway" })
            {
                float best = _meta.BestLap(t);
                bool driven = best > MetaProgress.NoLapRecord;
                scroll.Add(CollectItem(driven ? $"{t}  —  best {best:0.00}s" : t, driven));
            }

            scroll.Add(GroupLabel("LICENSES"));
            for (int s = 0; s <= 3; s++)
                scroll.Add(CollectItem($"STAKE {s}", _meta.IsStakeUnlocked(s)));

            scroll.Add(GroupLabel("COSMETICS"));
            scroll.Add(Dim("(none yet — a later content pass)"));

            section.Add(scroll);
            section.Add(BackFoot());
            return section;
        }

        // ── PROFILE ────────────────────────────────────────────────────────────────────────────────
        private VisualElement BuildProfile()
        {
            var section = Section("PROFILE");
            var list = new VisualElement();
            list.AddToClassList("menu-stats");
            list.Add(StatRow("RUNS PLAYED", _meta.totalRuns.ToString()));
            list.Add(StatRow("SEASONS CLEARED", _meta.seasonsCleared.ToString()));
            list.Add(StatRow("BEST CIRCUIT REACHED", _meta.bestCircuitReached.ToString()));
            list.Add(StatRow("LIFETIME MONEY", "$" + _meta.lifetimeMoney));
            list.Add(StatRow("HIGHEST LICENSE", "STAKE " + _meta.HighestUnlockedStake));
            list.Add(StatRow("TRACKS WITH A LAP", CountLapRecords().ToString()));
            section.Add(list);
            section.Add(BackFoot());
            return section;
        }

        private int CountLapRecords()
        {
            int n = 0;
            foreach (string t in new[] { "RaceTest", "RaceGauntlet", "RaceSpeedway" })
                if (_meta.BestLap(t) > MetaProgress.NoLapRecord) n++;
            return n;
        }

        // ── SETTINGS ───────────────────────────────────────────────────────────────────────────────
        private VisualElement BuildSettings()
        {
            var section = Section("SETTINGS");
            var scroll = new ScrollView();
            scroll.AddToClassList("menu-scroll");

            scroll.Add(GroupLabel("SOUND"));
            scroll.Add(SliderField("MASTER", _settings.masterVolume, v => { _settings.masterVolume = v; ApplySettings(); }));
            scroll.Add(SliderField("MUSIC", _settings.musicVolume, v => { _settings.musicVolume = v; ApplySettings(); }));
            scroll.Add(SliderField("SFX", _settings.sfxVolume, v => { _settings.sfxVolume = v; ApplySettings(); }));

            scroll.Add(GroupLabel("VIDEO"));
            scroll.Add(Check("FULLSCREEN", _settings.fullscreen, v => { _settings.fullscreen = v; ApplySettings(); }));
            scroll.Add(Check("V-SYNC", _settings.vsync, v => { _settings.vsync = v; ApplySettings(); }));
            scroll.Add(QualityDropdown());

            scroll.Add(GroupLabel("GAMEPLAY"));
            scroll.Add(Check("SCREEN SHAKE", _settings.screenShake, v => { _settings.screenShake = v; ApplySettings(); }));
            scroll.Add(Check("DAMAGE FLASH", _settings.damageFlash, v => { _settings.damageFlash = v; ApplySettings(); }));

            scroll.Add(GroupLabel("CONTROLS"));
            scroll.Add(ActivateKeyDropdown());

            section.Add(scroll);
            section.Add(BackFoot());
            return section;
        }

        /// <summary>
        /// The single ACTIVATE bind (doc 08 decision 14): deploys the equipped active item in a race.
        /// A curated choice list rather than free listening — every entry is guaranteed reachable and
        /// parseable (ActivateKeyBinding), and a stale settings value falls back to Q.
        /// </summary>
        private VisualElement ActivateKeyDropdown()
        {
            var names = new List<string>(ActivateKeyBinding.Choices);
            int current = names.FindIndex(n =>
                string.Equals(n, _settings.activateKey, StringComparison.OrdinalIgnoreCase));
            var dd = new DropdownField("ACTIVATE KEY", names, Mathf.Clamp(current, 0, names.Count - 1));
            dd.AddToClassList("menu-field");
            dd.RegisterValueChangedCallback(evt =>
            {
                _settings.activateKey = evt.newValue;
                ApplySettings();
            });
            return dd;
        }

        private void ApplySettings()
        {
            _settings.Apply();
            GameSettings.Save(_settings);
        }

        private VisualElement QualityDropdown()
        {
            var names = new List<string>(QualitySettings.names);
            int current = _settings.qualityLevel >= 0 && _settings.qualityLevel < names.Count
                ? _settings.qualityLevel
                : QualitySettings.GetQualityLevel();
            var dd = new DropdownField("QUALITY", names, Mathf.Clamp(current, 0, names.Count - 1));
            dd.AddToClassList("menu-field");
            dd.RegisterValueChangedCallback(evt =>
            {
                _settings.qualityLevel = names.IndexOf(evt.newValue);
                ApplySettings();
            });
            return dd;
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────────
        private static VisualElement Section(string heading = null)
        {
            var e = new VisualElement();
            e.AddToClassList("menu-section");
            if (heading != null)
            {
                var h = new Label { text = heading };
                h.AddToClassList("menu-heading");
                e.Add(h);
            }
            return e;
        }

        private VisualElement BackFoot()
        {
            var foot = new VisualElement();
            foot.AddToClassList("menu-foot");
            foot.Add(MenuButton("BACK", () => Show(_main)));
            return foot;
        }

        private static Button MenuButton(string text, Action onClick, bool primary = false)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text };
            b.AddToClassList("menu-btn");
            if (primary) b.AddToClassList("primary");
            return b;
        }

        private static Label GroupLabel(string text)
        {
            var l = new Label { text = "-- " + text + " --" };
            l.AddToClassList("menu-group");
            return l;
        }

        private static VisualElement CollectItem(string text, bool unlocked)
        {
            var row = new VisualElement();
            row.AddToClassList("collect-item");
            if (!unlocked) row.AddToClassList("locked");
            var l = new Label { text = unlocked ? text : "??? — LOCKED" };
            l.AddToClassList("collect-name");
            row.Add(l);
            return row;
        }

        private static VisualElement StatRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("stat-row");
            var l = new Label { text = label };
            l.AddToClassList("stat-row__label");
            var v = new Label { text = value };
            v.AddToClassList("stat-row__value");
            row.Add(l);
            row.Add(v);
            return row;
        }

        private static VisualElement SliderField(string label, float value, Action<float> onChange)
        {
            var s = new Slider(label, 0f, 1f) { value = Mathf.Clamp01(value) };
            s.AddToClassList("menu-field");
            s.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            return s;
        }

        private static VisualElement Check(string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = value };
            t.AddToClassList("menu-field");
            t.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            return t;
        }

        private static Label Dim(string text)
        {
            var l = new Label { text = text };
            l.AddToClassList("menu-dim");
            return l;
        }
    }
}
