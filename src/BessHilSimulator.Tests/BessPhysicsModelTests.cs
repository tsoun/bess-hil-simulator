using System;
using Xunit;
using BessHilSimulator;

namespace TestingSimulator.Tests
{
    public class BessPhysicsModelTests
    {
        [Fact]
        public void TestLagFilterTracking()
        {
            // Ts = 0.1s, LagP = 0.2s, LagQ = 0.1s, Delay = 0.5s, MaxMva = 0.21
            var model = new BessPhysicsModel(
                tickSeconds: 0.1,
                lagSecondsP: 0.2,
                lagSecondsQ: 0.1,
                delaySeconds: 0.5,
                maxMva: 0.21,
                capacityMwh: 1.0,
                initialSocPercent: 50.0
            );

            // Step 1: uP = 0.1. PhysP returned is the pre-update state (0.0).
            var output1 = model.Step(uP: 0.1, uQ: 0.0, distV: 1.0, distF: 50.0, time: 0.1);
            Assert.Equal(0.0, output1.PhysP);

            // Step 2: uP = 0.1. PhysP returned is the state after Step 1.
            // ad_p = e^(-0.1 / 0.2) = e^(-0.5) approx 0.60653
            // bd_p = 1 - ad_p approx 0.39347
            // P_phys after Step 1 = 0.60653 * 0 + 0.39347 * 0.1 = 0.039347
            var output2 = model.Step(uP: 0.1, uQ: 0.0, distV: 1.0, distF: 50.0, time: 0.2);
            
            Assert.True(output2.PhysP > 0.038 && output2.PhysP < 0.041, $"Expected PhysP around 0.039, got {output2.PhysP}");
            Assert.Equal(0.1, output2.SetpointP);
        }

        [Fact]
        public void TestTransportDelay()
        {
            // Ts = 0.1s, Delay = 0.5s -> 5 steps delay
            var model = new BessPhysicsModel(
                tickSeconds: 0.1,
                lagSecondsP: 0.2,
                lagSecondsQ: 0.1,
                delaySeconds: 0.5,
                maxMva: 0.21
            );

            // Give a large command
            double cmd = 0.1;
            PlantOutput output = default;

            // Pre-update physP_now starts at 0.0 and is enqueued.
            // Queue initialized with 5 zeros.
            // Step 1 (k=0): Enqueue 0.0 (physP_now). Dequeue 0.0 (from repeat). Queue has [0,0,0,0,0.0]. PhysP becomes 0.039.
            // Step 2 (k=1): Enqueue 0.039. Dequeue 0.0 (from repeat). Queue has [0,0,0,0.0,0.039].
            // Step 3 (k=2): Enqueue 0.063. Dequeue 0.0 (from repeat).
            // Step 4 (k=3): Enqueue 0.077. Dequeue 0.0 (from repeat).
            // Step 5 (k=4): Enqueue 0.086. Dequeue 0.0 (from repeat).
            // Step 6 (k=5): Enqueue 0.091. Dequeue 0.0 (the value enqueued in Step 1).
            // Step 7 (k=6): Enqueue 0.095. Dequeue 0.039 (the value enqueued in Step 2).
            for (int k = 0; k < 6; k++)
            {
                output = model.Step(uP: cmd, uQ: 0.0, distV: 1.0, distF: 50.0, time: k * 0.1);
                Assert.Equal(0.0, output.MeasP);
            }

            // Step 7 (time = 0.6) should dequeue the non-zero value enqueued during Step 2
            output = model.Step(uP: cmd, uQ: 0.0, distV: 1.0, distF: 50.0, time: 0.6);
            Assert.True(output.MeasP > 0.0, $"Expected MeasP to be > 0 on step 7, got {output.MeasP}");
        }

        [Fact]
        public void TestSocDischargeFloor()
        {
            // Set initial SOC below floor of 5% (e.g. 4.0%)
            var model = new BessPhysicsModel(
                tickSeconds: 0.1,
                lagSecondsP: 0.2,
                lagSecondsQ: 0.1,
                delaySeconds: 0.5,
                maxMva: 0.21,
                capacityMwh: 0.21,
                initialSocPercent: 4.0,
                socDischargeFloor: 5.0
            );

            // Attempting to discharge (positive active power)
            var output = model.Step(uP: 0.1, uQ: 0.0, distV: 1.0, distF: 50.0, time: 0.1);

            // Output setpoint P should be clamped to 0.0 due to floor derating
            Assert.Equal(0.0, output.SetpointP);
        }

        [Fact]
        public void TestSocChargeCeilingTaper()
        {
            // SOC charge ceiling = 90.0%
            // Initial SOC = 95.0%
            // Max apparent power = 0.20
            // Since SOC is 95%, ratio = (95 - 90) / (100 - 90) = 5 / 10 = 0.5
            // So charge should be clamped to 0.20 * (1 - 0.5) = 0.10
            var model = new BessPhysicsModel(
                tickSeconds: 0.1,
                lagSecondsP: 0.2,
                lagSecondsQ: 0.1,
                delaySeconds: 0.5,
                maxMva: 0.20,
                capacityMwh: 1.0,
                initialSocPercent: 95.0,
                socChargeCeiling: 90.0
            );

            // Attempting to charge at full power (-0.20 MW)
            var output = model.Step(uP: -0.20, uQ: 0.0, distV: 1.0, distF: 50.0, time: 0.1);

            // Charge setpoint should be clamped to -0.10 MW (since absorption is negative active power)
            Assert.Equal(-0.10, output.SetpointP);
        }

        [Fact]
        public void TestChargeEfficiency()
        {
            // Capacity = 1.0 MWh
            // Initial SOC = 50.0%
            // Charge efficiency = 80% (0.8)
            var model = new BessPhysicsModel(
                tickSeconds: 3600.0, // 1 hour step to make math simple
                lagSecondsP: 0.01,   // small lag to reach setpoint immediately
                lagSecondsQ: 0.01,
                delaySeconds: 0.1,
                maxMva: 1.0,
                capacityMwh: 1.0,
                initialSocPercent: 50.0,
                chargeEfficiency: 0.8
            );

            // Step with charging active power = -1.0 MW
            // Energy step is -1.0 MW * 1 hour * 0.8 = -0.8 MWh
            // Since we subtract energyStep / capacity (which is -0.8 / 1.0),
            // SOC should increase by 80% to 50 + 80 = 100% (or clamped to 100%)
            // Let's use a smaller command so it doesn't clamp.
            // Charge command = -0.5 MW. Energy step = -0.5 MW * 1 hour * 0.8 = -0.4 MWh.
            // New SOC = 50 - (-0.4 / 1.0)*100 = 90%
            var output = model.Step(uP: -0.5, uQ: 0.0, distV: 1.0, distF: 50.0, time: 3600.0);

            Assert.Equal(90.0, output.Soc);
        }

        [Fact]
        public void TestPqCapabilityCurveInterpolation()
        {
            // Instantiates capability curve
            var curve = new PqCapabilityCurve("pq-curves.json");

            // From pq-curves.json:
            // Voltages: [0.9, 1.0, 1.1]
            // CosTheta: [-1, 0, 1]
            // QMaxData for all voltages is [0.5, 0.5, 0.5]
            
            // Interpolating at V = 0.95, activePower = 0
            var limits = curve.GetInterpolatedReactiveLimits(voltage: 0.95, activePower: 0.0, apparentPowerLimit: 0.21);

            // Max Q = 0.5 * 0.21 = 0.105
            // Min Q = -0.5 * 0.21 = -0.105
            Assert.Equal(0.105, limits.maxQ, precision: 5);
            Assert.Equal(-0.105, limits.minQ, precision: 5);
        }
    }
}
