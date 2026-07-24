using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;
using UnityEngine.InputSystem;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The decision-14 active-item core (doc 08): each item declares its own charge condition, all
    /// deploy through one bind, and the reservoir is the proven DraftBoost model underneath. Pure
    /// plain-C# state — the Unity glue (sensors, combat events, the actual keyboard) stays in
    /// ActivePartRunner and is exercised in play.
    /// </summary>
    public class ActivePartTests : TestBase
    {
        private static ActiveSpec Spec(ActiveCharge charge, float boost = 1.15f, float drain = 0.5f,
            float fill = 0.35f, float perEvent = 0.34f, float minCharge = 1f, int useCost = 0)
        {
            return new ActiveSpec
            {
                Charge = charge,
                BoostMult = boost,
                DrainPerSecond = drain,
                FillPerSecond = fill,
                ChargePerEvent = perEvent,
                MinCharge01 = minCharge,
                UseCost = useCost,
            };
        }

        private static ActivePartState Armed(ActiveSpec spec)
        {
            var state = new ActivePartState();
            state.Arm(spec);
            return state;
        }

        private static int TickSeconds(ActivePartState state, float seconds, bool filling,
            bool activate = false, int money = 0)
        {
            int spent = 0;
            var signals = new ActivePartState.Signals { Filling = filling };
            for (float t = 0f; t < seconds; t += 0.02f)
                spent += state.Tick(0.02f, signals, activate, money);
            return spent;
        }

        [Test]
        public void UnarmedState_IsAPerfectNoOp()
        {
            var state = new ActivePartState();
            Assert.IsFalse(state.Armed);
            Assert.AreEqual(1f, state.BoostMult, 1e-6f, "no active equipped must read as no boost");
            Assert.AreEqual(0, state.Tick(0.02f, default, activatePressed: true, money: 100),
                "an unarmed tick spends nothing");
            Assert.IsFalse(state.ReadyToDeploy(100));

            state.Arm(Spec(ActiveCharge.None));
            Assert.IsFalse(state.Armed, "Charge == None (every pre-existing asset) must never arm");
        }

        [Test]
        public void Drafting_FillsWhileFilling_AndDeploysPastItsMinCharge()
        {
            var state = Armed(Spec(ActiveCharge.Drafting, fill: 0.35f, minCharge: 0.25f));

            TickSeconds(state, 1f, filling: false);
            Assert.AreEqual(0f, state.Charge01, 1e-4f, "no tow, no charge");

            TickSeconds(state, 1f, filling: true);
            Assert.AreEqual(0.35f, state.Charge01, 0.02f, "a second in the tow fills at the authored rate");
            Assert.IsTrue(state.ReadyToDeploy(0), "past min charge with no use cost = ready");

            state.Tick(0.02f, new ActivePartState.Signals { Filling = true }, activatePressed: true, money: 0);
            Assert.IsTrue(state.Deployed, "the bind deploys once past min charge");
            Assert.AreEqual(1.15f, state.BoostMult, 1e-4f, "deployed boost is the authored multiplier");

            TickSeconds(state, 2f, filling: false);
            Assert.IsFalse(state.Deployed, "the reservoir drains dry and the boost releases");
            Assert.AreEqual(1f, state.BoostMult, 1e-6f, "released boost reads exactly nominal");
        }

        [Test]
        public void FullChargeGate_RefusesAnEarlyDeploy()
        {
            var state = Armed(Spec(ActiveCharge.SectorLine, perEvent: 0.34f, minCharge: 1f));

            state.Tick(0.02f, new ActivePartState.Signals { EventCharge = 0.68f }, activatePressed: true, money: 0);
            Assert.IsFalse(state.Deployed, "two of three sector chunks is not a full reservoir");

            state.Tick(0.02f, new ActivePartState.Signals { EventCharge = 0.34f }, activatePressed: false, money: 0);
            Assert.AreEqual(1f, state.Charge01, 1e-4f, "chunks accumulate and cap at full");
            Assert.IsTrue(state.ReadyToDeploy(0));

            state.Tick(0.02f, default, activatePressed: true, money: 0);
            Assert.IsTrue(state.Deployed, "full-charge deploys");
        }

        [Test]
        public void OncePerRace_StartsFull_AndNeverRefills()
        {
            var state = Armed(Spec(ActiveCharge.OncePerRace, boost: 1.3f));
            Assert.AreEqual(1f, state.Charge01, 1e-6f, "once-per-race arms FULL — that is its whole design");

            state.Tick(0.02f, default, activatePressed: true, money: 0);
            Assert.IsTrue(state.Deployed);
            TickSeconds(state, 3f, filling: true); // filling signal must mean nothing to it
            Assert.IsFalse(state.Deployed);
            Assert.AreEqual(0f, state.Charge01, 1e-4f, "spent is spent — no refill route exists");
            Assert.IsFalse(state.ReadyToDeploy(100), "one big push per race, never two");
        }

        [Test]
        public void PaidUse_IsAlwaysReady_AndEveryDeployCostsMoney()
        {
            var state = Armed(Spec(ActiveCharge.PaidUse, drain: 0.8f, useCost: 2));
            Assert.IsTrue(state.ReadyToDeploy(2), "affordable = ready, no behaviour required");
            Assert.IsFalse(state.ReadyToDeploy(1), "a wallet below the tab is the one gate");

            int spent = state.Tick(0.02f, default, activatePressed: true, money: 5);
            Assert.AreEqual(2, spent, "the deploy transition charges the use cost exactly once");
            spent = TickSeconds(state, 0.5f, filling: false, activate: true, money: 5);
            Assert.AreEqual(0, spent, "holding the key mid-boost never double-pays");

            TickSeconds(state, 2f, filling: false); // drain dry
            Assert.IsFalse(state.Deployed);
            Assert.IsTrue(state.ReadyToDeploy(2), "the reservoir refills instantly — money is the meter");

            spent = state.Tick(0.02f, default, activatePressed: true, money: 1);
            Assert.AreEqual(0, spent, "an unaffordable press is refused, not billed");
            Assert.IsFalse(state.Deployed);
        }

        [Test]
        public void Cooldown_FillsUnconditionally()
        {
            var state = Armed(Spec(ActiveCharge.Cooldown, fill: 0.5f));
            TickSeconds(state, 1f, filling: false); // the host reports "not filling" — a cooldown doesn't care
            Assert.AreEqual(0.5f, state.Charge01, 0.03f, "a cooldown is a timer, not a behaviour");
        }

        [Test]
        public void AuthoredBoost_IsClampedToTheModelsAbsoluteCeiling()
        {
            var state = Armed(Spec(ActiveCharge.OncePerRace, boost: 9f));
            state.Tick(0.02f, default, activatePressed: true, money: 0);
            Assert.IsTrue(state.Deployed);
            Assert.LessOrEqual(state.BoostMult, DraftBoostModel.AbsoluteMaxBoostMult + 1e-4f,
                "no authored value may exceed the KERS model's hard 1.5x ceiling");
        }

        [Test]
        public void ZeroDtTick_NeitherChargesNorDeploys()
        {
            // The dev pause runs Update with Time.deltaTime == 0: a key pressed into a frozen menu
            // must not deploy (the model treats a zero-dt step as a no-op).
            var state = Armed(Spec(ActiveCharge.OncePerRace));
            int spent = state.Tick(0f, default, activatePressed: true, money: 100);
            Assert.AreEqual(0, spent);
            Assert.IsFalse(state.Deployed, "a paused frame can neither charge nor deploy");
        }

        [Test]
        public void AddCharge_RejectsJunkAndClampsAtFull()
        {
            var model = new DraftBoostModel();
            model.AddCharge(-1f);
            model.AddCharge(float.NaN);
            Assert.AreEqual(0f, model.Charge01, 1e-6f, "junk chunks are rejected");
            model.AddCharge(0.7f);
            model.AddCharge(0.7f);
            Assert.AreEqual(1f, model.Charge01, 1e-6f, "chunks clamp at a full reservoir");
        }

        [Test]
        public void ActivateKeyBinding_ParsesKnownKeys_AndFallsBackToQ()
        {
            Assert.AreEqual(Key.E, ActivateKeyBinding.Parse("e"), "parse is case-insensitive");
            Assert.AreEqual(Key.LeftShift, ActivateKeyBinding.Parse("LeftShift"));
            Assert.AreEqual(Key.Q, ActivateKeyBinding.Parse(null), "null falls back to Q");
            Assert.AreEqual(Key.Q, ActivateKeyBinding.Parse("NotAKey"), "junk falls back to Q");
            Assert.AreEqual(Key.Q, ActivateKeyBinding.Parse("None"), "None falls back to Q — the bind must always exist");
        }

        [Test]
        public void ActivateKeyBinding_NextCyclesTheCuratedListAndWraps()
        {
            string first = ActivateKeyBinding.Choices[0];
            string last = ActivateKeyBinding.Choices[ActivateKeyBinding.Choices.Length - 1];
            Assert.AreEqual(ActivateKeyBinding.Choices[1], ActivateKeyBinding.Next(first));
            Assert.AreEqual(first, ActivateKeyBinding.Next(last), "the cycle wraps");
            Assert.AreEqual(last, ActivateKeyBinding.Next(first, -1), "backwards wraps too");
            Assert.AreEqual(ActivateKeyBinding.Choices[1], ActivateKeyBinding.Next("garbage"),
                "an unknown stored value re-enters the cycle at the start");
        }
    }
}
