using System.Collections.Generic;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Watches one race and records what the PLAYER did, per rival, as attributed events.
    ///
    /// WHY ONE OBSERVER, NOT DETECTION INSIDE EACH BOT. <see cref="BotNeighbor"/> deliberately throws
    /// identity away, so pairwise attribution is impossible from inside a brain; and seven bots each
    /// deciding "was that a divebomb" would give seven inconsistent answers at seven times the cost. One
    /// observer sees the whole field and produces one account of the race.
    ///
    /// ONLY PAIRS INVOLVING THE PLAYER are tracked — 7 pairs, not 56. Rival-vs-rival racecraft is not a
    /// shipped feature; it would double CPU and save size for nothing the player can perceive.
    ///
    /// Pure C#: <paramref name="dt"/> and the race clock are passed in, never read from <c>Time</c>; no
    /// Random, no scene access. Same contract as <see cref="BotBrain.Step"/>, so a headless server can run
    /// it and it is testable by driving synthetic <see cref="CarFrame"/> arrays through a hand-rolled loop.
    /// </summary>
    public sealed class RaceObserver
    {
        // --- Engagement / pass geometry -----------------------------------------------------------------
        public const float ProximityM = 20f;      // counts as "racing each other" for exposure
        public const float EngageRangeM = 25f;    // a pass contest can open inside this
        public const float MinRacingSpeed = 8f;   // below this nobody is contesting anything
        public const float CommitGapM = 8f;       // attacker must be at least this close to commit
        public const float CommitLateralM = 1.6f; // ...and genuinely in another lane (car is ~1.8 wide)
        public const float CompleteGapM = 6f;     // clear by this much...
        public const float CompleteHoldS = 1.0f;  // ...for this long, and the pass stands
        public const float AbortGapM = 12f;       // dropped back this far = gave up
        public const float AbortLateralM = 1.0f;  // ...or tucked back in behind
        public const float AbortHoldS = 0.6f;
        public const float EngageTimeoutS = 8f;
        public const float PairCooldownS = 2f;    // stops a side-by-side battle emitting 40 events
        public const float RetakeWindowS = 8f;    // a pass immediately reversed was not really a pass
        public const float RanWideLateralM = 9f;
        public const float RanWideSpeedLoss = 0.30f;

        // --- Contact ------------------------------------------------------------------------------------
        public const float ContactSeverityGate = 0.08f;
        /// <summary>
        /// Collapses (a) PhysX firing several OnCollisionEnter for one scrape and (b) BOTH cars reporting
        /// the same collision with complementary blame. Without it a single sidewipe reads as half a dozen
        /// collisions with contradictory fault.
        /// </summary>
        public const float ContactDedupeS = 0.5f;
        public const float ContactResolvesPassS = 3f;
        public const float FaultDecisiveMargin = 0.15f; // |fault - 0.5| beyond this is not "mutual"

        // --- Divebomb scoring ---------------------------------------------------------------------------
        public const float DiveThreshold = 0.55f;
        public const float DiveLateBrakeSpanM = 25f;
        public const float DiveOverspeedSpan = 0.20f;
        public const float DiveClosingRateFloor = 1.5f;
        public const float DiveClosingRateSpan = 3.0f;
        public const float DiveTurnInGapM = 2.5f;
        /// <summary>Lateral established this far before turn-in is a normal side-by-side, not a lunge.</summary>
        public const float DiveEarlyLineM = 60f;
        public const float DiveEarlyLineCap = 0.40f;

        // --- Defence / bluff ----------------------------------------------------------------------------
        public const float DefendShiftM = 1.0f;
        public const float BluffLateralM = 1.4f;
        public const float BluffOutS = 0.8f;
        public const float BluffReturnM = 0.5f;
        public const float BluffReturnS = 1.5f;
        public const float BluffThrottleCommit = 0.85f;
        public const float BluffCooldownS = 3f;

        /// <summary>
        /// Mirrors RaceManager's own teleport guard. BotDriver's flip-recovery and reset-to-track both
        /// teleport cars mid-race, and ShuffleGrid moves everyone at the start; without this a teleport
        /// reads as a spectacular overtake.
        /// </summary>
        public const float MaxPlausibleStepM = 10f;

        private enum Phase : byte { Idle, Engaged, Committed }

        /// <summary>Per-rival tracking. One allocation at registration, none per step.</summary>
        private sealed class Pair
        {
            public int RivalKey;
            public RivalEncounterSummary Sum;

            public Phase Phase;
            public bool PlayerIsAttacker;
            public float PhaseEnteredS;
            public float CommittedS;
            public PassSide Side;

            // Rolling geometry
            public float PrevGap;          // + = player ahead
            public float PrevLateralDelta;
            public bool HasPrev;
            public float ClearHoldS;       // how long the completing car has been clear
            public float TuckHoldS;        // how long the attacker has been tucked back in
            public float MinAbsGapM;
            public float MaxClosingRate;   // lateral closing rate over the contest
            public float AttackerEntrySpeed;
            public float DefenderEntrySpeed;
            public float GapAtCommitM;
            public float LateralEstablishedAtM; // distance-before-corner where the lateral was taken

            // Braking comparison (the cleanest dive signal: same corner, same moment, both cars)
            public float PlayerBrakeOnsetToCornerM;
            public float RivalBrakeOnsetToCornerM;
            public bool PlayerBraked, RivalBraked;
            public float ActiveCornerEntryM;
            public bool HasActiveCorner;

            // Defence
            public float DefenderFreeLateralM;
            public bool HasDefenderFreeLateral;

            // Bluff
            public float BluffAnchorLateralM;
            public float BluffPeakLateralM;
            public float BluffStartS;
            public bool BluffOut;
            public float LastBluffS;
            public float BluffMaxThrottle;

            // Bookkeeping
            public float LastContactS;
            public float LastPassS;
            public bool LastPassByPlayer;
            public float GapIntegral;
            public float GapSeconds;
            public float CooldownUntilS;
        }

        private readonly List<Pair> _pairs = new List<Pair>(8);
        private float _lastRaceTimeS;

        public RaceObserver(int rivalCapacity = 8) => _pairs.Capacity = Mathf.Max(1, rivalCapacity);

        /// <summary>Clears all accumulated state. Call once at the start of a race.</summary>
        public void Reset()
        {
            _pairs.Clear();
            _lastRaceTimeS = 0f;
        }

        /// <summary>Starts tracking a rival. Keys must be &gt; 0; 0 is reserved for the player.</summary>
        public void RegisterRival(int rivalKey)
        {
            if (rivalKey <= 0 || FindPair(rivalKey) != null) return;
            _pairs.Add(new Pair
            {
                RivalKey = rivalKey,
                Sum = new RivalEncounterSummary { RivalKey = rivalKey, ClosestApproachM = float.MaxValue },
            });
        }

        private Pair FindPair(int key)
        {
            for (int i = 0; i < _pairs.Count; i++)
                if (_pairs[i].RivalKey == key) return _pairs[i];
            return null;
        }

        /// <summary>
        /// Observes one physics step. <paramref name="frames"/> must contain the player (Key 0); rivals are
        /// matched by key. Frames whose progress jumped more than <see cref="MaxPlausibleStepM"/> since the
        /// last step are treated as teleports and skipped for that pair.
        /// </summary>
        public void Observe(float raceTimeS, float dt, CarFrame[] frames, int count, CornerTable corners = null)
        {
            if (frames == null || count <= 0 || dt <= 0f) return;
            _lastRaceTimeS = raceTimeS;

            // Find the player once.
            int playerIdx = -1;
            for (int i = 0; i < count && i < frames.Length; i++)
                if (frames[i].Key == 0) { playerIdx = i; break; }
            if (playerIdx < 0) return;

            CarFrame player = frames[playerIdx];
            if (!player.Racing) return;

            for (int i = 0; i < count && i < frames.Length; i++)
            {
                if (i == playerIdx) continue;
                CarFrame rival = frames[i];
                if (rival.Key <= 0 || !rival.Racing) continue;

                Pair p = FindPair(rival.Key);
                if (p == null) continue;

                StepPair(p, raceTimeS, dt, player, rival, corners);
            }
        }

        private void StepPair(Pair p, float t, float dt, in CarFrame player, in CarFrame rival, CornerTable corners)
        {
            float gap = player.TotalDistanceM - rival.TotalDistanceM; // + = player ahead
            float lateralDelta = player.LateralM - rival.LateralM;
            float absGap = Mathf.Abs(gap);

            // Teleport guard: an implausible jump in the gap since last step is a recovery teleport or the
            // grid shuffle, never racing. Drop the step for this pair and re-anchor.
            if (p.HasPrev && Mathf.Abs(gap - p.PrevGap) > MaxPlausibleStepM)
            {
                p.PrevGap = gap;
                p.PrevLateralDelta = lateralDelta;
                ResetContest(p, t);
                return;
            }

            // --- Exposure: the denominators that make every rate comparable across careers ---
            if (absGap <= ProximityM)
            {
                p.Sum.ProximitySeconds += dt;
                if (absGap < p.Sum.ClosestApproachM) p.Sum.ClosestApproachM = absGap;
            }
            p.GapIntegral += gap * dt;
            p.GapSeconds += dt;

            bool bothRacing = player.SpeedMps > MinRacingSpeed && rival.SpeedMps > MinRacingSpeed;

            // --- Corner context ---
            bool inCornerCtx = corners != null
                && corners.TryGetCornerAt(player.ProgressM, DiveEarlyLineM, out Corner corner);
            Corner activeCorner = default;
            if (inCornerCtx) corners.TryGetCornerAt(player.ProgressM, DiveEarlyLineM, out activeCorner);

            TrackBraking(p, player, rival, activeCorner, inCornerCtx);

            if (!p.HasPrev)
            {
                p.PrevGap = gap;
                p.PrevLateralDelta = lateralDelta;
                p.HasPrev = true;
                return;
            }

            float closingRate = Mathf.Abs(lateralDelta - p.PrevLateralDelta) / dt;
            if (closingRate > p.MaxClosingRate && absGap < EngageRangeM) p.MaxClosingRate = closingRate;
            if (absGap < p.MinAbsGapM) p.MinAbsGapM = absGap;

            switch (p.Phase)
            {
                case Phase.Idle:
                    if (t >= p.CooldownUntilS && bothRacing && absGap < EngageRangeM)
                        EnterEngaged(p, t, gap, player, rival);
                    else
                        DetectBluff(p, t, dt, player, rival, absGap);
                    break;

                case Phase.Engaged:
                    if (!bothRacing || absGap > EngageRangeM || t - p.PhaseEnteredS > EngageTimeoutS)
                    {
                        ResetContest(p, t);
                        break;
                    }
                    DetectBluff(p, t, dt, player, rival, absGap);
                    DetectDefensiveMove(p, player, rival, activeCorner, inCornerCtx);

                    // Commit: the attacker is close AND genuinely in another lane AND still closing.
                    float attackerGap = p.PlayerIsAttacker ? -gap : gap; // + = attacker behind by this much
                    bool closing = p.PlayerIsAttacker ? gap > p.PrevGap : gap < p.PrevGap;
                    if (attackerGap < CommitGapM && Mathf.Abs(lateralDelta) > CommitLateralM && closing)
                        EnterCommitted(p, t, player, rival, activeCorner, inCornerCtx);
                    break;

                case Phase.Committed:
                    StepCommitted(p, t, dt, gap, lateralDelta, player, rival);
                    break;
            }

            p.PrevGap = gap;
            p.PrevLateralDelta = lateralDelta;
        }

        private void EnterEngaged(Pair p, float t, float gap, in CarFrame player, in CarFrame rival)
        {
            p.Phase = Phase.Engaged;
            p.PhaseEnteredS = t;
            p.PlayerIsAttacker = gap < 0f; // player behind = player attacking
            p.MinAbsGapM = Mathf.Abs(gap);
            p.MaxClosingRate = 0f;
            p.Sum.Engagements++;

            // Snapshot the defender's free line so a defensive shift can be measured against where they
            // would otherwise have been — without this baseline the metric is 90% racing line, 10% racecraft.
            p.DefenderFreeLateralM = p.PlayerIsAttacker ? rival.LateralM : player.LateralM;
            p.HasDefenderFreeLateral = true;
            p.LateralEstablishedAtM = -1f;
        }

        private void EnterCommitted(Pair p, float t, in CarFrame player, in CarFrame rival,
            in Corner corner, bool hasCorner)
        {
            p.Phase = Phase.Committed;
            p.CommittedS = t;

            float attackerLateral = p.PlayerIsAttacker ? player.LateralM : rival.LateralM;
            float defenderLateral = p.PlayerIsAttacker ? rival.LateralM : player.LateralM;
            float sideSign = Mathf.Sign(attackerLateral - defenderLateral);

            // "Inside" is corner-relative, never left/right. On a straight there is no inside — and that is
            // a real answer: a slipstream pass down the same side of the same straight every lap says
            // nothing about the driver, only about the track.
            p.Side = !hasCorner ? PassSide.Straight
                : Mathf.Approximately(sideSign, corner.Sign) ? PassSide.Inside
                : PassSide.Outside;

            p.AttackerEntrySpeed = p.PlayerIsAttacker ? player.SpeedMps : rival.SpeedMps;
            p.DefenderEntrySpeed = p.PlayerIsAttacker ? rival.SpeedMps : player.SpeedMps;
            p.GapAtCommitM = Mathf.Abs(player.TotalDistanceM - rival.TotalDistanceM);

            if (hasCorner && p.LateralEstablishedAtM < 0f)
                p.LateralEstablishedAtM = Mathf.Max(0f, corner.EntryM - player.ProgressM);

            p.ClearHoldS = 0f;
            p.TuckHoldS = 0f;
        }

        private void StepCommitted(Pair p, float t, float dt, float gap, float lateralDelta,
            in CarFrame player, in CarFrame rival)
        {
            float attackerGap = p.PlayerIsAttacker ? gap : -gap; // + = attacker now ahead

            if (attackerGap > CompleteGapM)
            {
                p.ClearHoldS += dt;
                if (p.ClearHoldS >= CompleteHoldS)
                {
                    ResolvePass(p, t, PassOutcome.Completed);
                    return;
                }
            }
            else p.ClearHoldS = 0f;

            // Backed out: dropped a long way back, or tucked in behind again while still behind.
            if (attackerGap < -AbortGapM)
            {
                ResolvePass(p, t, PassOutcome.Aborted);
                return;
            }
            if (attackerGap < 0f && Mathf.Abs(lateralDelta) < AbortLateralM)
            {
                p.TuckHoldS += dt;
                if (p.TuckHoldS >= AbortHoldS)
                {
                    ResolvePass(p, t, PassOutcome.Aborted);
                    return;
                }
            }
            else p.TuckHoldS = 0f;

            // Ran out of road, or threw the corner away without touching anyone.
            float attackerLateral = p.PlayerIsAttacker ? player.LateralM : rival.LateralM;
            float attackerSpeed = p.PlayerIsAttacker ? player.SpeedMps : rival.SpeedMps;
            bool lostIt = Mathf.Abs(attackerLateral) > RanWideLateralM
                || (p.AttackerEntrySpeed > 1f
                    && attackerSpeed < p.AttackerEntrySpeed * (1f - RanWideSpeedLoss)
                    && t - p.CommittedS < 2f);
            if (lostIt)
            {
                ResolvePass(p, t, PassOutcome.RanWide);
                return;
            }

            if (t - p.CommittedS > EngageTimeoutS) ResolvePass(p, t, PassOutcome.Aborted);
        }

        /// <summary>Books a resolved contest and starts the pair cooldown.</summary>
        private void ResolvePass(Pair p, float t, PassOutcome outcome)
        {
            bool byPlayer = p.PlayerIsAttacker;
            bool succeeded = outcome == PassOutcome.Completed;

            if (succeeded)
            {
                if (byPlayer)
                {
                    p.Sum.PlayerPassesOnRival++;
                    switch (p.Side)
                    {
                        case PassSide.Inside: p.Sum.PlayerPassesInside++; break;
                        case PassSide.Outside: p.Sum.PlayerPassesOutside++; break;
                        default: p.Sum.PlayerPassesStraight++; break;
                    }
                    p.Sum.PlayerPassesCompletedClean++;

                    // Immediately reversing an earlier pass means neither was really a pass — retract the
                    // rival's, so a two-car see-saw doesn't inflate both drivers' success rates.
                    if (!p.LastPassByPlayer && t - p.LastPassS < RetakeWindowS && p.Sum.RivalPassesOnPlayer > 0)
                        p.Sum.RivalPassesOnPlayer--;
                }
                else
                {
                    p.Sum.RivalPassesOnPlayer++;
                    if (p.LastPassByPlayer && t - p.LastPassS < RetakeWindowS && p.Sum.PlayerPassesOnRival > 0)
                    {
                        p.Sum.PlayerPassesOnRival--;
                        if (p.Sum.PlayerPassesCompletedClean > 0) p.Sum.PlayerPassesCompletedClean--;
                    }
                }
                p.LastPassS = t;
                p.LastPassByPlayer = byPlayer;
            }
            else if (byPlayer)
            {
                if (outcome == PassOutcome.Aborted) p.Sum.PlayerAttemptsAborted++;
                else if (outcome == PassOutcome.RanWide) p.Sum.PlayerAttemptsRanWide++;
            }

            // Divebomb: scored on the attempt, not the result. A dive that works is still a dive — and a
            // player whose dives SUCCEED is exactly the one a rival most needs to cover the inside against.
            if (byPlayer && p.Side == PassSide.Inside)
            {
                float score = ScoreDive(p);
                if (score >= DiveThreshold)
                {
                    p.Sum.PlayerDiveAttempts++;
                    p.Sum.PlayerDiveScoreTotal += score;
                    if (succeeded) p.Sum.PlayerDivesConverted++;
                }
            }

            ResetContest(p, t);
            p.CooldownUntilS = t + PairCooldownS;
        }

        /// <summary>
        /// 0..1 "how much of a lunge was that". Scored rather than booleaned because a divebomb is defined
        /// by lack of control, not by the line: a clean inside pass and a lunge take the same piece of road.
        /// </summary>
        private float ScoreDive(Pair p)
        {
            // d1 — braked later than the car being passed. This is the strongest signal available, because
            // it is a PAIRED comparison at the same corner at the same moment: track shape, grip level, tyre
            // wear and the bot-strength ramp all cancel out.
            float d1 = 0f;
            if (p.PlayerBraked && p.RivalBraked)
                d1 = Mathf.Clamp01((p.RivalBrakeOnsetToCornerM - p.PlayerBrakeOnsetToCornerM) / DiveLateBrakeSpanM);
            else if (p.RivalBraked && !p.PlayerBraked)
                d1 = 1f; // flat-out where the other car braked is the extreme of late braking, not a no-read

            // d2 — absolute over-commitment: carrying more speed than the car ahead at turn-in.
            float d2 = p.DefenderEntrySpeed > 1f
                ? Mathf.Clamp01((p.AttackerEntrySpeed / p.DefenderEntrySpeed - 1f) / DiveOverspeedSpan)
                : 0f;

            // d3 — arriving sideways. A clean inside pass establishes its lane early and holds it; a
            // divebomb closes the lateral gap fast and late. Most discriminative of the four.
            float d3 = Mathf.Clamp01((p.MaxClosingRate - DiveClosingRateFloor) / DiveClosingRateSpan);

            // d4 — not alongside by turn-in: the nose was never really there.
            float d4 = Mathf.Clamp01((DiveTurnInGapM - p.GapAtCommitM) / DiveTurnInGapM);

            float score = 0.30f * d1 + 0.20f * d2 + 0.30f * d3 + 0.20f * d4;

            // A lane taken well before the corner is a normal side-by-side, however it ends.
            if (p.LateralEstablishedAtM > DiveEarlyLineM) score = Mathf.Min(score, DiveEarlyLineCap);

            // Require at least one ABSOLUTE sign of over-commitment, not merely a relative one. Without
            // this, a timid bot braking early makes every ordinary pass on it read as a divebomb.
            if (d2 < 0.2f && d3 < 0.3f) score *= 0.5f;

            return score;
        }

        /// <summary>
        /// Records where each car first hit the brakes relative to the corner ahead — the input to the
        /// paired late-braking comparison.
        /// </summary>
        private void TrackBraking(Pair p, in CarFrame player, in CarFrame rival, in Corner corner, bool hasCorner)
        {
            if (!hasCorner)
            {
                p.HasActiveCorner = false;
                return;
            }

            if (!p.HasActiveCorner || !Mathf.Approximately(p.ActiveCornerEntryM, corner.EntryM))
            {
                p.HasActiveCorner = true;
                p.ActiveCornerEntryM = corner.EntryM;
                p.PlayerBraked = false;
                p.RivalBraked = false;
            }

            float toCorner = corner.EntryM - player.ProgressM;
            if (toCorner < 0f) return; // already in it

            if (!p.PlayerBraked && player.Brake >= 0.25f)
            {
                p.PlayerBraked = true;
                p.PlayerBrakeOnsetToCornerM = toCorner;
            }
            if (!p.RivalBraked && rival.Brake >= 0.25f)
            {
                p.RivalBraked = true;
                p.RivalBrakeOnsetToCornerM = Mathf.Max(0f, corner.EntryM - rival.ProgressM);
            }
        }

        /// <summary>
        /// A defensive move is the DEFENDER shifting toward the attacker's side — measured as a delta from
        /// the line they were on before the contest opened, not as an absolute position.
        /// </summary>
        private void DetectDefensiveMove(Pair p, in CarFrame player, in CarFrame rival,
            in Corner corner, bool hasCorner)
        {
            if (!p.HasDefenderFreeLateral || p.PlayerIsAttacker) return; // only measure the PLAYER defending

            float shift = player.LateralM - p.DefenderFreeLateralM;
            if (Mathf.Abs(shift) < DefendShiftM) return;

            // Sign it corner-relative so "+ = covered the inside" means the same thing on every corner of
            // every track. On a straight there is no inside, so there is nothing to learn.
            if (!hasCorner) return;

            p.Sum.PlayerDefensiveMoves++;
            p.Sum.PlayerDefendShiftTotal += shift * corner.Sign;
            p.HasDefenderFreeLateral = false; // one reading per contest
        }

        /// <summary>
        /// A bluff is a committed-LOOKING lateral move, in a threatening context, that reverses without any
        /// speed commitment and never becomes a real attempt.
        ///
        /// Honest caveat: this is the weakest detector here. Real players feint far less than designers
        /// expect and a gamepad car has genuine lateral noise, so expect very few hits — which is the
        /// correct outcome, not a bug. Anything ambiguous is classified as an aborted real attempt instead,
        /// deliberately: miscounting genuine attacks as bluffs would make rivals under-rate an aggressive
        /// player, which is the more damaging error.
        /// </summary>
        private void DetectBluff(Pair p, float t, float dt, in CarFrame player, in CarFrame rival, float absGap)
        {
            if (t - p.LastBluffS < BluffCooldownS) return;
            if (absGap > 15f || player.SpeedMps < 10f) return;

            // Only the trailing player can bluff — a leading car moving about is defending, not feinting.
            if (player.TotalDistanceM > rival.TotalDistanceM) { p.BluffOut = false; return; }

            if (!p.BluffOut)
            {
                p.BluffAnchorLateralM = player.LateralM;
                p.BluffPeakLateralM = player.LateralM;
                p.BluffStartS = t;
                p.BluffMaxThrottle = player.Throttle;
                p.BluffOut = true;
                return;
            }

            p.BluffMaxThrottle = Mathf.Max(p.BluffMaxThrottle, player.Throttle);
            if (Mathf.Abs(player.LateralM - p.BluffAnchorLateralM) > Mathf.Abs(p.BluffPeakLateralM - p.BluffAnchorLateralM))
                p.BluffPeakLateralM = player.LateralM;

            float outFor = t - p.BluffStartS;
            float excursion = Mathf.Abs(p.BluffPeakLateralM - p.BluffAnchorLateralM);

            // Re-anchor if they never went anywhere, or took too long to be a feint.
            if (outFor > BluffOutS + BluffReturnS)
            {
                p.BluffOut = false;
                return;
            }

            bool wentOut = excursion >= BluffLateralM && outFor <= BluffOutS + BluffReturnS;
            bool cameBack = Mathf.Abs(player.LateralM - p.BluffAnchorLateralM) <= BluffReturnM;
            bool noCommitment = p.BluffMaxThrottle < BluffThrottleCommit;

            if (wentOut && cameBack && noCommitment)
            {
                p.Sum.PlayerBluffs++;
                p.LastBluffS = t;
                p.BluffOut = false;
            }
        }

        private void ResetContest(Pair p, float t)
        {
            p.Phase = Phase.Idle;
            p.PhaseEnteredS = t;
            p.ClearHoldS = 0f;
            p.TuckHoldS = 0f;
            p.MaxClosingRate = 0f;
            p.MinAbsGapM = float.MaxValue;
            p.HasDefenderFreeLateral = false;
            p.BluffOut = false;
        }

        /// <summary>
        /// Records an already-attributed car-to-car contact. <paramref name="playerFault01"/> is the
        /// PLAYER's share of the blame, derived by the host from
        /// <see cref="VehicleCombat.Aggressorness"/>.
        ///
        /// De-duplicated: both cars raise their own report for one collision, and PhysX fires repeatedly
        /// through a scrape, so a repeat for the same pair inside <see cref="ContactDedupeS"/> is dropped.
        /// </summary>
        public void RecordContact(float raceTimeS, int rivalKey, float severity01, float playerFault01)
        {
            Pair p = FindPair(rivalKey);
            if (p == null) return;
            if (severity01 < ContactSeverityGate) return;
            if (raceTimeS - p.LastContactS < ContactDedupeS) return;
            p.LastContactS = raceTimeS;

            float fault = Mathf.Clamp01(playerFault01);
            p.Sum.ContactSeverityTotal += severity01;

            if (fault > 0.5f + FaultDecisiveMargin)
            {
                p.Sum.ContactsPlayerFault++;
                p.Sum.PlayerFaultSeverityTotal += severity01 * fault;
            }
            else if (fault < 0.5f - FaultDecisiveMargin)
            {
                p.Sum.ContactsRivalFault++;
            }
            else
            {
                p.Sum.ContactsMutual++;
                p.Sum.PlayerFaultSeverityTotal += severity01 * fault;
            }

            // Contact during a live contest decides how that contest ended.
            if (p.Phase == Phase.Committed && raceTimeS - p.CommittedS <= ContactResolvesPassS)
            {
                bool attackerAtFault = p.PlayerIsAttacker ? fault > 0.5f : fault < 0.5f;
                PassOutcome outcome = Mathf.Abs(fault - 0.5f) <= FaultDecisiveMargin ? PassOutcome.Clashed
                    : attackerAtFault ? PassOutcome.Punted
                    : PassOutcome.Blocked;
                ResolvePass(p, raceTimeS, outcome);
            }
        }

        /// <summary>
        /// Rolls up everything observed so far. Pure over accumulated state and safe to call any number of
        /// times, in any order relative to <see cref="Observe"/> — deliberately PULLED rather than pushed at
        /// race end, because <c>RunDirector</c> polls <c>RaceComplete</c> from <c>Update</c> while the
        /// observer steps in <c>FixedUpdate</c>, and a push design would race with script execution order.
        /// </summary>
        public RaceObservationSummary Summarize(int playerFinishPosition, int fieldSize)
        {
            var rivals = new RivalEncounterSummary[_pairs.Count];
            for (int i = 0; i < _pairs.Count; i++)
            {
                Pair p = _pairs[i];
                RivalEncounterSummary s = p.Sum;
                s.MeanSignedGapM = p.GapSeconds > 0f ? p.GapIntegral / p.GapSeconds : 0f;
                if (s.ClosestApproachM == float.MaxValue) s.ClosestApproachM = 0f;
                rivals[i] = s;
            }

            return new RaceObservationSummary
            {
                RaceDurationS = _lastRaceTimeS,
                PlayerFinishPosition = playerFinishPosition,
                FieldSize = fieldSize,
                Rivals = rivals,
            };
        }
    }
}
