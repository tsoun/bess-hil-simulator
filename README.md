BESS HIL Simulator

A Real-Time Hardware-in-the-Loop (HIL) Simulator for Battery Energy Storage Systems.

Overview

This simulator models the physics of a BESS inverter, including 1st-order lag dynamics, transport delays, and P-Q capability curves. It acts as a Modbus TCP Server, allowing external Energy Management Systems (EMS) or SCADA tools to interface with the simulated battery.

Key Features

Physics Engine: Simulates active/reactive power response with configurable latency.

Modbus Interface: Exposes telemetry and accepts setpoints via Modbus TCP (Port 502).

Visualization: Includes a viewer.html dashboard to analyze simulation CSV logs.

Grid Scenarios: Simulates grid voltage sags/swells to test capability curve logic.

Project Structure

.
├── BessPhysicsModel.cs    # Core physics and delay buffers
├── ModbusServerWrapper.cs # NModbus implementation
├── PqCapabilityCurve.cs   # Reactive power limit logic
├── Program.cs             # Main simulation loop
├── viewer.html            # Web-based log analyzer
├── BessHilSimulator.csproj # .NET Project file
└── README.md


Getting Started

Prerequisites

.NET 8.0 SDK

A Modbus Client (optional, for testing)

Building and Running

Restore Dependencies:

dotnet restore


Run the Simulator:
(Note: Port 502 often requires Administrator/Root privileges. If you cannot run as admin, change the port in Program.cs to 5020)

sudo dotnet run
# OR on Windows (Run Terminal as Admin)
dotnet run


Interact:

Manual: Type 1.0 0.5 in the console to request 1.0 MW / 0.5 MVAR.

Modbus: Connect a client to 127.0.0.1:502. Write to Holding Registers 40001 (P) and 40003 (Q).

Analyzing Results

The simulator generates a BessData.csv file.

Open viewer.html in any modern web browser.

Drag and drop BessData.csv into the dashboard to view performance charts.
