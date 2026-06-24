using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BessHilSimulator;

namespace BessDashboard
{
    public class HilSimulationService
    {
        private Thread? _simThread;
        private bool _running;
        private readonly object _lock = new object();
        
        // Simulation parameters
        private const double Ts = 0.1; // 100ms
        private const double Tdelay = 0.5; // 500ms SCADA delay
        private double _currentTime;
        private double _setpointP;
        private double _setpointQ;
        private BessPhysicsModel? _plant;
        private StreamWriter? _csvWriter;
        
        // Sliding history window for UI charting (last 300 points = 30 seconds at 10Hz)
        private readonly List<SimStep> _history = new List<SimStep>();
        private const int MaxHistoryPoints = 300;

        public bool IsRunning => _running;
        public int Port { get; private set; } = 502;

        public void Start(int port = 502)
        {
            lock (_lock)
            {
                if (_running) return;
                
                Port = port;
                _currentTime = 0.0;
                _setpointP = 0.0;
                _setpointQ = 0.0;
                _history.Clear();
                
                // Initialize model (parameters matching original Program.cs real-time initialization)
                _plant = new BessPhysicsModel(Ts, 0.2, 0.1, Tdelay, 0.21);
                
                // Initialize CSV writer
                try
                {
                    string csvFilePath = "BessData.csv";
                    _csvWriter = new StreamWriter(new FileStream(csvFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
                    _csvWriter.WriteLine("Time_s,SetP_MW,SetQ_MVAR,PhysP_MW,PhysQ_MVAR,PhysPF,PhysV_pu,PhysF_Hz,PhysI_kA,MaxQ_MVAR,MinQ_MVAR,MeasP_MW,MeasQ_MVAR,MeasPF,MeasV_pu,MeasF_Hz,MeasI_kA");
                    _csvWriter.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HIL] Error initializing CSV logging: {ex.Message}");
                }

                // Start Modbus Server
                ModbusServerWrapper.Start(Port);
                
                _running = true;
                _simThread = new Thread(RunLoop)
                {
                    IsBackground = true,
                    Name = "BessHilSimulationLoop"
                };
                _simThread.Start();
                Console.WriteLine($"[HIL] Simulation service started on port {Port}");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_running) return;
                
                _running = false;
                
                // Wait for simulation thread to finish
                if (_simThread != null && _simThread.IsAlive)
                {
                    _simThread.Join(500);
                }
                
                // Stop Modbus Server
                ModbusServerWrapper.Stop();
                
                // Close CSV writer
                if (_csvWriter != null)
                {
                    try
                    {
                        _csvWriter.Flush();
                        _csvWriter.Close();
                        _csvWriter.Dispose();
                    }
                    catch { }
                    _csvWriter = null;
                }
                
                _plant = null;
                Console.WriteLine("[HIL] Simulation service stopped.");
            }
        }

        public void SetSetpoint(double p, double q)
        {
            lock (_lock)
            {
                _setpointP = p;
                _setpointQ = q;
                Console.WriteLine($"[HIL] Manual setpoint applied: P={_setpointP:F2} MW, Q={_setpointQ:F2} MVAR");
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                bool wasRunning = _running;
                if (wasRunning) Stop();
                _history.Clear();
                if (wasRunning) Start(Port);
            }
        }

        public (bool isRunning, int port, double currentP, double currentQ, double time, List<SimStep> steps) GetLatestState()
        {
            lock (_lock)
            {
                var stepsCopy = new List<SimStep>(_history);
                return (_running, Port, _setpointP, _setpointQ, _currentTime, stepsCopy);
            }
        }

        private void RunLoop()
        {
            while (_running)
            {
                try
                {
                    double p, q;
                    lock (_lock)
                    {
                        p = _setpointP;
                        q = _setpointQ;
                    }

                    // 1. Fetch Modbus setpoint command overrides if they arrived
                    if (ModbusServerWrapper.GetSetpointCommands(out double modbusP, out double modbusQ))
                    {
                        p = modbusP;
                        q = modbusQ;
                        lock (_lock)
                        {
                            _setpointP = modbusP;
                            _setpointQ = modbusQ;
                        }
                    }

                    // 2. Compute grid conditions (V changes at specific intervals matching original Console App simulation)
                    double gridV = 1.0;
                    if (_currentTime >= 30.0 && _currentTime < 40.0) gridV = 0.95;
                    if (_currentTime >= 60.0 && _currentTime < 70.0) gridV = 1.05;

                    // 3. Step physics model
                    PlantOutput y = default;
                    lock (_lock)
                    {
                        if (_plant != null)
                        {
                            y = _plant.Step(p, q, gridV, 50.0, _currentTime);
                        }
                    }

                    // 4. Update Modbus registers
                    ModbusServerWrapper.UpdateMeasurementRegisters(y);

                    // 5. Append to history buffer
                    var step = new SimStep(new InputRow { Time = _currentTime, SetpointP = p, SetpointQ = q, GridV = gridV, GridF = 50.0 }, y);
                    lock (_lock)
                    {
                        _history.Add(step);
                        if (_history.Count > MaxHistoryPoints)
                        {
                            _history.RemoveAt(0);
                        }
                    }

                    // 6. Log to CSV
                    if (_csvWriter != null)
                    {
                        string csvLine = $"{y.Timestamp:F2},{y.SetpointP:F4},{y.SetpointQ:F4}," +
                                         $"{y.PhysP:F4},{y.PhysQ:F4},{y.PhysPF:F4},{y.PhysV:F4},{y.PhysF:F2},{y.PhysI:F4}," +
                                         $"{y.MaxQ:F4},{y.MinQ:F4}," +
                                         $"{y.MeasP:F4},{y.MeasQ:F4},{y.MeasPF:F4},{y.MeasV:F4},{y.MeasF:F2},{y.MeasI:F4}";
                        
                        _csvWriter.WriteLine(csvLine);
                        _csvWriter.Flush();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HIL] Error in simulation loop tick: {ex.Message}");
                }

                // Sleep for sampling interval (100ms)
                _currentTime += Ts;
                Thread.Sleep((int)(Ts * 1000));
            }
        }
    }
}
