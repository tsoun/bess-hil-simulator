namespace BessSim.Core
{
    // Snapshot of the BESS state at a specific simulation step (k)
    public struct PlantOutput
    {
        public double Timestamp;

        // --- EMS References (u[k]) ---
        public double SetpointP; // Active Power Reference (MW)
        public double SetpointQ; // Reactive Power Reference (MVAR)

        // --- Plant State (x[k]) ---
        public double PhysP;
        public double PhysQ;
        public double PhysPF;
        public double PhysV;
        public double PhysF;
        public double PhysI;

        // --- SCADA/Meter Feedback (y[k]) ---
        public double MeasP;
        public double MeasQ;
        public double MeasPF;
        public double MeasV;
        public double MeasF;
        public double MeasI;

        // --- Capability Limits ---
        public double MaxQ;
        public double MinQ;

        // --- BESS state for closed-loop EMS demos ---
        // Soc: integrated state of charge (0..100 %), reduced by
        // discharge (P > 0) and increased by charge (P < 0).
        // Soh: state of health (0..100 %); static 100 today.
        // Available: 1.0 = inverter ready to dispatch, 0.0 = down.
        // TempCelsius: cell-temperature placeholder (static).
        public double Soc;
        public double Soh;
        public double Available;
        public double TempCelsius;
    }

    // Command structure for incoming setpoints
    public struct SetpointCommand
    {
        public double P;
        public double Q;
        public double Timestamp;
    }
}
