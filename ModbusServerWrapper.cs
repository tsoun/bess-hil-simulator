using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NModbus;

namespace BessHilSimulator
{
    // Wraps the NModbus Slave logic.
    // Allows the Simulator to act as a Server for the EMS.
    public static class ModbusServerWrapper
    {
        private static IModbusSlave? _slave;
        private static TcpListener? _listener;
        
        // Cache to detect if Modbus sent a new value
        private static float _cachedP = 0f;
        private static float _cachedQ = 0f;

        public static void Start(int port = 502)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();

                var factory = new ModbusFactory();
                var network = factory.CreateSlaveNetwork(_listener);
                _slave = factory.CreateSlave(1); // Unit ID = 1
                network.AddSlave(_slave);

                // Run listener in background
                network.ListenAsync();
                Console.WriteLine($"[Modbus] Server active on Port {port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Modbus] Startup Error: {ex.Message}");
                Console.WriteLine("[Modbus] Try running as Administrator or change port to 5020.");
            }
        }

        // Call this to update telemetry visible to the Client (Input Registers)
        public static void UpdateMeasurementRegisters(PlantOutput y)
        {
            if (_slave == null) return;
            
            // Map Plant Output to Input Registers (30001+)
            WriteInputFloat(0, (float)y.MeasP);
            WriteInputFloat(2, (float)y.MeasQ);
            WriteInputFloat(4, (float)y.MeasV);
            WriteInputFloat(6, (float)y.MeasF);
            WriteInputFloat(8, (float)y.MeasI);
        }

        // Call this to check if Client sent new commands (Holding Registers)
        // Returns true if values were updated.
        public static bool GetSetpointCommands(out double p, out double q)
        {
            p = 0; q = 0;
            if (_slave == null) return false;

            // Read Holding Registers (40001+)
            float modbusP = ReadHoldingFloat(0);
            float modbusQ = ReadHoldingFloat(2);

            // Simple change detection
            if (Math.Abs(modbusP - _cachedP) > 0.001 || Math.Abs(modbusQ - _cachedQ) > 0.001)
            {
                _cachedP = modbusP;
                _cachedQ = modbusQ;
                p = modbusP;
                q = modbusQ;
                return true; // New command received
            }
            return false;
        }

        // --- Internal Helpers ---
        
        private static void WriteInputFloat(int address, float value)
        {
            if (_slave == null) return;

            byte[] bytes = BitConverter.GetBytes(value);
            
            // Create array of points (registers)
            ushort[] points = new ushort[2];
            points[0] = BitConverter.ToUInt16(bytes, 0);
            points[1] = BitConverter.ToUInt16(bytes, 2);

            // NModbus DataStore uses ReadPoints/WritePoints instead of indexers
            // Using address + 1 to align with typical 1-based Modbus mapping
            _slave.DataStore.InputRegisters.WritePoints((ushort)(address + 1), points);
        }

        private static float ReadHoldingFloat(int address)
        {
            if (_slave == null) return 0f;

            try 
            {
                // Use ReadPoints to get the data
                var points = _slave.DataStore.HoldingRegisters.ReadPoints((ushort)(address + 1), 2);
                
                if (points == null || points.Length < 2) return 0f;

                ushort r1 = points[0];
                ushort r2 = points[1];
                
                byte[] bytes = new byte[4];
                BitConverter.GetBytes(r1).CopyTo(bytes, 0);
                BitConverter.GetBytes(r2).CopyTo(bytes, 2);
                return BitConverter.ToSingle(bytes, 0);
            }
            catch
            {
                // Return 0 if address is not initialized or out of range
                return 0f;
            }
        }
    }
}