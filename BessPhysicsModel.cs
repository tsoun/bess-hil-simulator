using System;
using System.Linq;
using System.Collections.Generic;

namespace BessHilSimulator
{
     // Simulates Inverter Dynamics: 1st Order Lag + Transport Delay + Saturation
    public class BessPhysicsModel
    {
        // Discrete-time filter coefficients (Z-domain)
        private readonly double _ad_p, _bd_p, _ad_q, _bd_q;

        // Inverter capability
        private readonly double _maxApparentPower;
        private readonly PqCapabilityCurve _capabilityCurve;

        // Measurement latency (N steps)
        private readonly int _delaySteps;

        // Tick length (s) — also used by the SOC integrator below.
        private readonly double _tickSeconds;

        // BESS energy state. Capacity in MWh, SOC in 0..100 %.
        private readonly double _capacityMwh;
        private double _socPercent;

        // Static placeholders for SOH and temperature so the EMS gets
        // valid-shaped BMS telemetry until a richer model lands.
        private const double SohPercent = 100.0;
        private const double TempCelsius = 22.0;

        // Internal states (Previous output y[k-1])
        private double P_physical = 0.0;
        private double Q_physical = 0.0;

        // FIFO buffers to model transport delay (Z^-N)
        private Queue<double> _bufferP;
        private Queue<double> _bufferQ;
        private Queue<double> _bufferV;
        private Queue<double> _bufferF;

        public BessPhysicsModel(
            double tickSeconds, double lagSecondsP, double lagSecondsQ,
            double delaySeconds, double maxMva,
            double capacityMwh = 0.21, double initialSocPercent = 50.0)
        {
            // Calculate discrete coefficients for 1st order Low Pass Filter
            _ad_p = Math.Exp(-tickSeconds / lagSecondsP); _bd_p = 1.0 - _ad_p;
            _ad_q = Math.Exp(-tickSeconds / lagSecondsQ); _bd_q = 1.0 - _ad_q;

            _maxApparentPower = maxMva;
            _capabilityCurve = new PqCapabilityCurve();
            _delaySteps = (int)(delaySeconds / tickSeconds);
            _tickSeconds = tickSeconds;

            _capacityMwh = capacityMwh;
            _socPercent = Math.Clamp(initialSocPercent, 0.0, 100.0);

            // Initialize delay lines with steady-state zeros
            _bufferP = new Queue<double>(Enumerable.Repeat(0.0, _delaySteps));
            _bufferQ = new Queue<double>(Enumerable.Repeat(0.0, _delaySteps));
            _bufferV = new Queue<double>(Enumerable.Repeat(1.0, _delaySteps));
            _bufferF = new Queue<double>(Enumerable.Repeat(50.0, _delaySteps));
        }

        public PlantOutput Step(double uP, double uQ, double distV, double distF, double time)
        {
            // 1. Capture State at t=k (Pre-update)
            double physP_now = P_physical;
            double physQ_now = Q_physical;
            double physV_now = distV;
            double physF_now = distF;

            // Calculate derived electrical quantities
            double physS = Math.Sqrt(physP_now * physP_now + physQ_now * physQ_now);
            double physI = (physV_now > 0.001) ? physS / physV_now : 0.0;
            double physPF = (physS > 0.001) ? Math.Abs(physP_now) / physS : 1.0;
            if (physP_now < 0) physPF = -physPF; // Negative for generation

            // 2. Apply P-Q Capability Curve Saturation
            // var (maxQ, minQ) = _capabilityCurve.GetReactiveLimits(physV_now, uP, _maxApparentPower);
            var (maxQ, minQ) = _capabilityCurve.GetInterpolatedReactiveLimits(physV_now, uP, _maxApparentPower);

            // Clamp Q to capability limits
            uQ = Math.Max(minQ, Math.Min(maxQ, uQ));
            
            // Also ensure apparent power doesn't exceed maximum
            double apparentPower = Math.Sqrt(uP * uP + uQ * uQ);
            if (apparentPower > _maxApparentPower)
            {
                double ratio = _maxApparentPower / apparentPower;
                uP *= ratio;
                uQ *= ratio;
            }

            // 3. Physics Evolution (Infinite Impulse Response - IIR Filter)
            P_physical = (_ad_p * P_physical) + (_bd_p * uP);
            P_physical = Math.Round(P_physical, 6);
            Q_physical = (_ad_q * Q_physical) + (_bd_q * uQ);
            Q_physical = Math.Round(Q_physical, 6);

            // 3a. SOC integration (after the IIR step so the energy
            // bookkeeping uses the freshly updated physical power):
            // discharge (P > 0) drains the battery, charge (P < 0)
            // refills it. Energy this tick = P[MW] * Ts[s] / 3600
            // gives MWh; divided by capacity in MWh and scaled to %
            // SOC produces the per-step delta. SOC is clamped to
            // 0..100 — when the battery hits an end stop the EMS
            // sees a flat plateau and SOC-MIN / SOC-MAX safety
            // logic in the consumer takes over.
            if (_capacityMwh > 0.0)
            {
                double energyStepMwh = P_physical * _tickSeconds / 3600.0;
                _socPercent -= (energyStepMwh / _capacityMwh) * 100.0;
                _socPercent = Math.Clamp(_socPercent, 0.0, 100.0);
            }

            // 4. Apply Measurement/Transport Delay
            _bufferP.Enqueue(physP_now);
            _bufferQ.Enqueue(physQ_now);
            _bufferV.Enqueue(physV_now);
            _bufferF.Enqueue(physF_now);

            double yP = _bufferP.Dequeue();
            double yQ = _bufferQ.Dequeue();
            double yV = _bufferV.Dequeue();
            double yF = _bufferF.Dequeue();

            // Re-calculate derived values based on delayed telemetry
            double yS = Math.Sqrt(yP * yP + yQ * yQ);
            double yI = (yV > 0.001) ? yS / yV : 0.0;
            double yPF = (yS > 0.001) ? Math.Abs(yP) / yS : 1.0;
            if (yP < 0) yPF = -yPF;

            return new PlantOutput
            {
                Timestamp = time,
                SetpointP = uP, SetpointQ = uQ,
                PhysP = physP_now, PhysQ = physQ_now, PhysPF = physPF, PhysV = physV_now, PhysF = physF_now, PhysI = physI,
                MeasP = yP, MeasQ = yQ, MeasPF = yPF, MeasV = yV, MeasF = yF, MeasI = yI,
                MaxQ = maxQ, MinQ = minQ,
                Soc = _socPercent, Soh = SohPercent,
                Available = 1.0, TempCelsius = TempCelsius,
            };
        }
    }

}