using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;

namespace BessHilSimulator
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
    
    // Command handler for receiving setpoints
    public class CommandHandler
    {
        private Queue<SetpointCommand> _commandQueue = new Queue<SetpointCommand>();
        private object _lockObject = new object();
        
        public void AddCommand(double p, double q, double timestamp)
        {
            lock (_lockObject)
            {
                _commandQueue.Enqueue(new SetpointCommand { P = p, Q = q, Timestamp = timestamp });
            }
        }
        
        public SetpointCommand? GetNextCommand(double currentTime)
        {
            lock (_lockObject)
            {
                if (_commandQueue.Count > 0)
                {
                    var nextCommand = _commandQueue.Peek();
                    if (nextCommand.Timestamp <= currentTime)
                    {
                        return _commandQueue.Dequeue();
                    }
                }
                return null;
            }
        }
        
        public bool HasPendingCommands => _commandQueue.Count > 0;
    }

    public class InputRow
    {
        public double Time { get; set; }
        public double SetpointP { get; set; }
        public double SetpointQ { get; set; }
        public double GridV { get; set; } = 1.0;
        public double GridF { get; set; } = 50.0;
    }

    class Program
    {
        private static bool _running = true;
        private static CommandHandler _commandHandler = new CommandHandler();
        private static double _currentTime = 0.0;

        static void RunCsvSimulation(string inputPath, string outputPath)
        {
            Console.WriteLine("=== BESS SIMULATOR: OFFLINE BATCH VALIDATION MODE ===");
            Console.WriteLine($"Reading from input CSV: {inputPath}");
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file {inputPath} does not exist.");
                return;
            }

            var rows = new List<InputRow>();
            string[] lines = File.ReadAllLines(inputPath);
            
            // Identify header positions
            int timeIdx = -1, pIdx = -1, qIdx = -1, vIdx = -1, fIdx = -1;
            if (lines.Length > 0)
            {
                string[] headers = lines[0].Split(',');
                for (int i = 0; i < headers.Length; i++)
                {
                    string h = headers[i].Trim().ToLower();
                    if (h.StartsWith("time")) timeIdx = i;
                    else if (h.Contains("setp") || h.Contains("active")) pIdx = i;
                    else if (h.Contains("setq") || h.Contains("reactive")) qIdx = i;
                    else if (h.Contains("gridv") || h.Contains("voltage")) vIdx = i;
                    else if (h.Contains("gridf") || h.Contains("frequency")) fIdx = i;
                }
            }

            // Fallback to position-based indexing if headers are not recognized
            if (timeIdx == -1) timeIdx = 0;
            if (pIdx == -1) pIdx = 1;
            if (qIdx == -1) qIdx = 2;
            if (vIdx == -1) vIdx = 3;
            if (fIdx == -1) fIdx = 4;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                
                string[] parts = line.Split(',');
                if (parts.Length <= Math.Max(timeIdx, Math.Max(pIdx, qIdx))) continue;

                try
                {
                    var row = new InputRow();
                    row.Time = double.Parse(parts[timeIdx]);
                    row.SetpointP = double.Parse(parts[pIdx]);
                    row.SetpointQ = double.Parse(parts[qIdx]);
                    
                    if (vIdx < parts.Length && double.TryParse(parts[vIdx], out double v))
                    {
                        row.GridV = v;
                    }
                    if (fIdx < parts.Length && double.TryParse(parts[fIdx], out double f))
                    {
                        row.GridF = f;
                    }
                    rows.Add(row);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not parse line {i + 1}: {ex.Message}");
                }
            }

            if (rows.Count == 0)
            {
                Console.WriteLine("Error: No valid simulation rows found in CSV.");
                return;
            }

            // Determine time step Ts (difference between consecutive timestamps)
            double Ts = 0.1;
            if (rows.Count > 1)
            {
                Ts = rows[1].Time - rows[0].Time;
                if (Ts <= 0.0) Ts = 0.1;
            }

            Console.WriteLine($"Detected simulation step size (Ts) = {Ts} seconds. Total steps = {rows.Count}.");

            // Initialize Model
            double Tdelay = 0.5; // SCADA delay (seconds)
            var plant = new BessPhysicsModel(Ts, 0.2, 0.1, Tdelay, 0.21);

            // Ensure output directory exists
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            Console.WriteLine($"Writing simulation results to: {outputPath}");

            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("Time_s,SetP_MW,SetQ_MVAR,PhysP_MW,PhysQ_MVAR,PhysPF,PhysV_pu,PhysF_Hz,PhysI_kA,MaxQ_MVAR,MinQ_MVAR,MeasP_MW,MeasQ_MVAR,MeasPF,MeasV_pu,MeasF_Hz,MeasI_kA,Soc,Soh,Available,TempCelsius");

                foreach (var row in rows)
                {
                    var y = plant.Step(row.SetpointP, row.SetpointQ, row.GridV, row.GridF, row.Time);

                    string csvLine = $"{y.Timestamp:F2},{y.SetpointP:F4},{y.SetpointQ:F4}," +
                                     $"{y.PhysP:F4},{y.PhysQ:F4},{y.PhysPF:F4},{y.PhysV:F4},{y.PhysF:F2},{y.PhysI:F4}," +
                                     $"{y.MaxQ:F4},{y.MinQ:F4}," +
                                     $"{y.MeasP:F4},{y.MeasQ:F4},{y.MeasPF:F4},{y.MeasV:F4},{y.MeasF:F2},{y.MeasI:F4}," +
                                     $"{y.Soc:F2},{y.Soh:F2},{y.Available:F1},{y.TempCelsius:F2}";
                    
                    writer.WriteLine(csvLine);
                }
            }

            Console.WriteLine("Simulation completed successfully.");
        }

        static void Main(string[] args)
        {
            string? inputCsvPath = null;
            string outputCsvPath = Path.Combine("output", "BessData_sim.csv");
            bool validationMode = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--input" && i + 1 < args.Length)
                {
                    inputCsvPath = args[i + 1];
                }
                else if (args[i] == "--output" && i + 1 < args.Length)
                {
                    outputCsvPath = args[i + 1];
                }
                else if (args[i] == "--validation")
                {
                    validationMode = true;
                }
                else if (args[i] == "--mode" && i + 1 < args.Length)
                {
                    if (string.Equals(args[i + 1], "validation", StringComparison.OrdinalIgnoreCase))
                    {
                        validationMode = true;
                    }
                }
            }

            if (validationMode || !string.IsNullOrEmpty(inputCsvPath))
            {
                if (string.IsNullOrEmpty(inputCsvPath))
                {
                    inputCsvPath = "input_profile.csv";
                }
                RunCsvSimulation(inputCsvPath, outputCsvPath);
                return;
            }

            string csvFilePath = "BessData.csv";
            bool consoleInputEnabled = ShouldStartConsoleInput(args);
            
            // Simulation Parameters
            double Ts = 0.1;      // Sampling Time (100ms)
            double Tdelay = 0.5;  // Total measurement loop latency (500ms)
            
            // Init Model
            var plant = new BessPhysicsModel(Ts, 0.2, 0.1, Tdelay, 0.21);

            Console.WriteLine("=== BESS SIMULATOR: REAL-TIME MODE WITH P-Q CAPABILITY CURVE ===");
            
            // [MODBUS] 1. Start the Server
            ModbusServerWrapper.Start(502); 

            Console.WriteLine($"Writing data to: {Path.GetFullPath(csvFilePath)}");
            if (consoleInputEnabled)
            {
                Console.WriteLine("Commands: P[MW] Q[MVAR] (e.g., '1.0 0.5') or 'exit' to quit");
            }
            else
            {
                Console.WriteLine("Console input disabled. Use Modbus for setpoints.");
            }
            Console.WriteLine(new string('-', 190));
            
            Console.Write($"| {"Time",-5} |");
            Console.Write($" {"INPUT REGISTERS",-19} |");
            Console.Write($" {"PHYSICS (INTERNAL STATE)",-47} |");
            Console.WriteLine($" {"MEASUREMENTS (OUTPUT)",-37} |");

            Console.Write($"| {"(s)",-5} |");
            Console.Write($" {"Set P",6} {"Set Q",6} {"Cmd",5} |"); 
            Console.Write($" {"P",6} {"Q",6} {"PF",5} {"V",5} {"F",4} {"I",6} {"Qmax",6} {"Qmin",6} |"); 
            Console.WriteLine($" {"P",6} {"Q",6} {"PF",5} {"V",5} {"F",4} {"I",6} |"); 
            Console.WriteLine(new string('-', 190));

            if (consoleInputEnabled)
            {
                // Start input thread
                Thread inputThread = new Thread(ReadInput);
                inputThread.IsBackground = true;
                inputThread.Start();
            }

            double setpointP = 0.0;
            double setpointQ = 0.0;

            using (StreamWriter writer = new StreamWriter(csvFilePath))
            {
                writer.WriteLine("Time_s,SetP_MW,SetQ_MVAR,PhysP_MW,PhysQ_MVAR,PhysPF,PhysV_pu,PhysF_Hz,PhysI_kA,MaxQ_MVAR,MinQ_MVAR,MeasP_MW,MeasQ_MVAR,MeasPF,MeasV_pu,MeasF_Hz,MeasI_kA");

                // Main simulation loop
                while (_running)
                {
                    // Check for new commands from Console
                    var command = _commandHandler.GetNextCommand(_currentTime);
                    if (command.HasValue)
                    {
                        setpointP = command.Value.P;
                        setpointQ = command.Value.Q;
                        Console.WriteLine($"\n>>> Command received: P={setpointP:F2} MW, Q={setpointQ:F2} MVAR");
                    }

                    // [MODBUS] 2. Check for commands from Modbus Client (EMS)
                    // If a new Modbus command arrived, it overrides the current setpoint
                    if (ModbusServerWrapper.GetSetpointCommands(out double modbusP, out double modbusQ))
                    {
                        setpointP = modbusP;
                        setpointQ = modbusQ;
                        Console.WriteLine($"\n[MODBUS] Command received: P={setpointP:F2}, Q={setpointQ:F2}");
                    }

                    // Grid conditions
                    double gridV = 1.0;
                    if (_currentTime >= 30.0 && _currentTime < 40.0) gridV = 0.95;
                    if (_currentTime >= 60.0 && _currentTime < 70.0) gridV = 1.05;

                    var y = plant.Step(setpointP, setpointQ, gridV, 50.0, _currentTime);

                    // [MODBUS] 3. Update Modbus Registers with new physics data
                    ModbusServerWrapper.UpdateMeasurementRegisters(y);

                    string cmdStatus = (setpointP > 0 || setpointQ > 0) ? "ON" : "OFF";
                    Console.Write($"| {_currentTime,5:F1} | {y.SetpointP,6:F2} {y.SetpointQ,6:F2} {cmdStatus,5} |");
                    Console.Write($" {y.PhysP,6:F3} {y.PhysQ,6:F3} {y.PhysPF,5:F2} {y.PhysV,5:F2} {y.PhysF,4:F0} {y.PhysI,6:F3} {y.MaxQ,6:F3} {y.MinQ,6:F3} |");
                    Console.WriteLine($" {y.MeasP,6:F3} {y.MeasQ,6:F3} {y.MeasPF,5:F2} {y.MeasV,5:F2} {y.MeasF,4:F0} {y.MeasI,6:F3} |");

                    // Write to CSV
                    string csvLine = $"{y.Timestamp:F2},{y.SetpointP:F4},{y.SetpointQ:F4}," +
                                     $"{y.PhysP:F4},{y.PhysQ:F4},{y.PhysPF:F4},{y.PhysV:F4},{y.PhysF:F2},{y.PhysI:F4}," +
                                     $"{y.MaxQ:F4},{y.MinQ:F4}," +
                                     $"{y.MeasP:F4},{y.MeasQ:F4},{y.MeasPF:F4},{y.MeasV:F4},{y.MeasF:F2},{y.MeasI:F4}";
                    
                    writer.WriteLine(csvLine);
                    writer.Flush();
                    
                    _currentTime += Ts;
                    Thread.Sleep((int)(Ts * 1000));
                }
            }

            Console.WriteLine("\nSimulation stopped.");
        }

        static bool ShouldStartConsoleInput(string[] args)
        {
            bool enabled = IsConsoleInputEnabledByEnvironment();

            foreach (string arg in args)
            {
                if (string.Equals(arg, "--no-console", StringComparison.OrdinalIgnoreCase))
                {
                    enabled = false;
                }
                else if (string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase))
                {
                    enabled = true;
                }
            }

            return enabled;
        }

        static bool IsConsoleInputEnabledByEnvironment()
        {
            string? value = Environment.GetEnvironmentVariable("BESS_HIL_CONSOLE");
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
        }

        static void ReadInput()
        {
            while (_running)
            {
                Console.Write("\nEnter command (P Q) or 'exit': ");
                string? input = Console.ReadLine()?.Trim();
                
                if (string.IsNullOrEmpty(input))
                    continue;
                    
                if (input.ToLower() == "exit")
                {
                    _running = false;
                    break;
                }

                try
                {
                    string[] parts = input.Split(' ');
                    if (parts.Length == 2)
                    {
                        double p = double.Parse(parts[0]);
                        double q = double.Parse(parts[1]);
                        
                        _commandHandler.AddCommand(p, q, _currentTime);
                        Console.WriteLine($">>> Command queued: P={p:F2} MW, Q={q:F2} MVAR (will take effect next cycle)");
                    }
                    else
                    {
                        Console.WriteLine(">>> Invalid format. Use: P[MW] Q[MVAR] (e.g., '1.0 0.5')");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($">>> Error parsing command: {ex.Message}");
                }
            }
        }
    }
}
