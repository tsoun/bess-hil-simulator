import time
import struct
import random
import traceback
from pymodbus.client import ModbusTcpClient

# ==============================================================================
# CONFIGURATION
# ==============================================================================
SERVER_IP = '127.0.0.1'
SERVER_PORT = 502
# The C# simulator shifts addresses by +1 (0 becomes 1).
# So we must read/write starting at 1.
OFFSET_ADDR = 1 

# ==============================================================================
# HELPERS (LITTLE ENDIAN)
# ==============================================================================
def float_to_registers_le(value):
    """
    Converts a float to two 16-bit Modbus registers (Little Endian).
    Matches C# BitConverter.
    Returns [LowWord, HighWord]
    """
    # Pack float to 4 bytes (Little Endian)
    byte_data = struct.pack('<f', value)
    # Unpack to two unsigned shorts (Little Endian)
    # This gives us the exact register values C# expects
    low_word, high_word = struct.unpack('<HH', byte_data)
    return [low_word, high_word]

def registers_to_float_le(registers):
    """
    Converts two 16-bit Modbus registers to a float (Little Endian).
    Expects [LowWord, HighWord]
    """
    if not registers or len(registers) < 2:
        return 0.0
    
    low_word = registers[0]
    high_word = registers[1]
    
    # Pack shorts back to bytes (Little Endian)
    byte_data = struct.pack('<HH', low_word, high_word)
    # Unpack bytes to float (Little Endian)
    return struct.unpack('<f', byte_data)[0]

# ==============================================================================
# MAIN
# ==============================================================================
def main():
    print(f"Connecting to {SERVER_IP}:{SERVER_PORT}...")
    client = ModbusTcpClient(SERVER_IP, port=SERVER_PORT, timeout=3)
    
    if not client.connect():
        print("FAILED to connect. Is the C# Simulator running?")
        return

    print("✓ Connected successfully!")
    
    # Enable the system (Coil 0)
    # Coils usually don't have the +1 offset issue in NModbus logic 
    # unless manually shifted, but usually they are separate.
    try:
        client.write_coil(0, True)
        print("✓ System ENABLED via coil 0")
        time.sleep(0.2)
    except Exception as e:
        print(f"Could not enable system: {e}")
    
    print("\n" + "="*120)
    print(f"{'TIME':<10} | {'CMD P':<8} {'CMD Q':<8} | {'MEAS P':<8} {'MEAS Q':<8} {'MEAS V':<8} {'MEAS F':<8} {'MEAS I':<8} | {'STATUS':<12}")
    print("="*120)

    try:
        cycle = 0
        while True:
            cycle += 1
            
            # 1. Generate random setpoints
            cmd_p = round(random.uniform(-0.15, 0.15), 3)
            cmd_q = round(random.uniform(-0.08, 0.08), 3)

            # 2. Write Setpoints
            # C# ReadHoldingFloat(0) reads Indices 1 & 2.
            # So we write to Address 1.
            try:
                # Write CMD P to Address 1
                client.write_registers(0 + OFFSET_ADDR, float_to_registers_le(cmd_p))
                # Write CMD Q to Address 3 (Logic: 2 + OFFSET)
                client.write_registers(2 + OFFSET_ADDR, float_to_registers_le(cmd_q))
            except Exception as e:
                print(f"Write error: {e}")
                continue

            # Wait for system response
            time.sleep(0.5)

            # 3. Read Measurements
            # C# writes MeasP(0) to Index 1.
            # So we read starting from Address 1.
            try:
                # We need 5 floats = 10 registers
                # CMD P(0)->Addr1, Q(2)->Addr3, V(4)->Addr5, F(6)->Addr7, I(8)->Addr9
                result = client.read_input_registers(address=0 + OFFSET_ADDR, count=10)
                
                if hasattr(result, 'registers') and result.registers and len(result.registers) >= 10:
                    regs = result.registers
                    
                    # Decode (Little Endian)
                    # regs[0], regs[1] corresponds to Address 1, 2
                    meas_p = registers_to_float_le(regs[0:2])
                    meas_q = registers_to_float_le(regs[2:4])
                    meas_v = registers_to_float_le(regs[4:6])
                    meas_f = registers_to_float_le(regs[6:8])
                    meas_i = registers_to_float_le(regs[8:10])
                    
                    # Tracking Status
                    error_p = abs(meas_p - cmd_p)
                    error_q = abs(meas_q - cmd_q)
                    
                    if error_p < 0.005 and error_q < 0.005:
                        status = "✓ AT SETPOINT"
                    elif error_p < 0.05: # Looser tolerance for ramping
                        status = "→ TRACKING"
                    else:
                        status = "⟳ RAMPING"
                    
                    timestamp = time.strftime("%H:%M:%S")
                    print(f"{timestamp:<10} | {cmd_p:>8.3f} {cmd_q:>8.3f} | "
                          f"{meas_p:>8.3f} {meas_q:>8.3f} {meas_v:>8.3f} {meas_f:>8.2f} {meas_i:>8.3f} | {status:<12}")
                    
                else:
                    print(f"Invalid data received: {result}")
                    
            except Exception as e:
                print(f"Read error: {e}")
                traceback.print_exc()

            time.sleep(1.0)

    except KeyboardInterrupt:
        print("\n\n" + "="*120)
        print("Stopping gracefully...")
        
        # Reset setpoints to zero
        try:
            zero_regs = float_to_registers_le(0.0)
            client.write_registers(0 + OFFSET_ADDR, zero_regs)
            client.write_registers(2 + OFFSET_ADDR, zero_regs)
            print("✓ Setpoints reset to zero")
            time.sleep(0.5)
        except:
            pass
        
        # Disable system
        try:
            client.write_coil(0, False)
            print("✓ System DISABLED")
        except:
            pass
            
    finally:
        client.close()
        print("✓ Disconnected")
        print("="*120)

if __name__ == "__main__":
    main()