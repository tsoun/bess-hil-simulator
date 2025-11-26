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

    class Program
    {
        private static bool _running = true;
        private static CommandHandler _commandHandler = new CommandHandler();
        private static double _currentTime = 0.0;
        
        static void Main(string[] args)
        {
            string csvFilePath = "BessData.csv";
            
            // Simulation Parameters
            double Ts = 0.1;      // Sampling Time (100ms)
            double Tdelay = 0.5;  // Total measurement loop latency (500ms)
            
            // Init Model
            var plant = new BessPhysicsModel(Ts, 0.2, 0.1, Tdelay, 0.21);

            Console.WriteLine("=== BESS SIMULATOR: REAL-TIME MODE WITH P-Q CAPABILITY CURVE ===");
            
            // [MODBUS] 1. Start the Server
            ModbusServerWrapper.Start(502); 

            Console.WriteLine($"Writing data to: {Path.GetFullPath(csvFilePath)}");
            Console.WriteLine("Commands: P[MW] Q[MVAR] (e.g., '1.0 0.5') or 'exit' to quit");
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

            // Start input thread
            Thread inputThread = new Thread(ReadInput);
            inputThread.IsBackground = true;
            inputThread.Start();

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