using System;

namespace LabLockdown.AI.Hunter
{
    public enum AttackState { Idle, WindingUp, Cooldown }

    public enum AttackEvent { None, WindUpStarted, WindUpCancelled, Fired }

    public struct AttackResult
    {
        public AttackEvent Event;
        /// <summary>Only meaningful when Event is Fired.</summary>
        public bool Hit;
    }

    /// <summary>
    /// The decision part of a ranged hitscan attack. It never touches Unity: the caller says what it can see
    /// and the model says what happens. The Unity side casts the actual ray and applies the damage.
    ///
    ///   Idle      : line of sight and in range and not on cooldown  ->  start winding up
    ///   WindingUp : sight or range lost -> cancel (no shot). Wind-up finished -> fire, roll to hit.
    ///   Cooldown  : wait, then Idle
    ///
    /// Hit chance falls linearly with distance:
    ///     chance(d) = MaxAccuracy + (MinAccuracy - MaxAccuracy) * d / Range        (0 beyond Range)
    /// </summary>
    public sealed class AttackModel
    {
        private readonly AttackParameters parameters;
        private readonly Func<float> random01;
        private float windUpEnd;
        private float cooldownEnd;

        /// <param name="random01">Returns a uniform value in [0, 1). Injected so tests are deterministic.</param>
        public AttackModel(AttackParameters parameters, Func<float> random01)
        {
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            this.random01 = random01 ?? throw new ArgumentNullException(nameof(random01));
            State = AttackState.Idle;
        }

        public AttackState State { get; private set; }

        public AttackParameters Parameters => parameters;

        /// <summary>True while the Hunter should stand still and aim.</summary>
        public bool IsWindingUp => State == AttackState.WindingUp;

        public float HitChance(float distance)
        {
            if (distance < 0f) distance = 0f;
            if (distance > parameters.Range) return 0f;
            float t = distance / parameters.Range;
            return parameters.MaxAccuracy + (parameters.MinAccuracy - parameters.MaxAccuracy) * t;
        }

        public void Reset()
        {
            State = AttackState.Idle;
            windUpEnd = 0f;
            cooldownEnd = 0f;
        }

        public AttackResult Update(float now, bool hasLineOfSight, float distance)
        {
            var result = new AttackResult { Event = AttackEvent.None };
            bool canEngage = hasLineOfSight && distance <= parameters.Range;

            switch (State)
            {
                case AttackState.Idle:
                    if (canEngage && now >= cooldownEnd)
                    {
                        State = AttackState.WindingUp;
                        windUpEnd = now + parameters.WindUp;
                        result.Event = AttackEvent.WindUpStarted;
                    }
                    break;

                case AttackState.WindingUp:
                    if (!canEngage)
                    {
                        State = AttackState.Idle;
                        result.Event = AttackEvent.WindUpCancelled;
                    }
                    else if (now >= windUpEnd)
                    {
                        result.Event = AttackEvent.Fired;
                        result.Hit = random01() < HitChance(distance);
                        State = AttackState.Cooldown;
                        cooldownEnd = now + parameters.Cooldown;
                    }
                    break;

                case AttackState.Cooldown:
                    if (now >= cooldownEnd) State = AttackState.Idle;
                    break;
            }
            return result;
        }
    }
}
