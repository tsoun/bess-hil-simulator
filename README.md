BESS HIL Simulator
==================

A Hardware-in-the-Loop (HIL) simulator for Power Conversion System (PCS) and Battery Energy Storage System (BESS) designed to validate Energy Management System (EMS) software.

Overview
--------

This project implements a real-time simulator that models the physical dynamics of grid-connected inverters, including:

-   First-order lag response for active and reactive power

-   Transport delay to simulate SCADA/communication latency

-   P-Q capability curves with voltage-dependent reactive power limits

-   Modbus TCP interface for EMS communication

The simulator provides a realistic testing environment for EMS validation by accurately reproducing the dynamic behavior and communication delays of actual PCS/BESS systems.

Architecture
------------

### System Components

text

┌─────────────────┐    Modbus TCP    ┌──────────────────┐
│   EMS (DUT)     │◄────────────────►│  HIL Simulator   │
│                 │                  │                  │
│ - Control Logic │   P_ref, Q_ref   │ - Physics Engine │
│ - SCADA Display │   P_meas, Q_meas │ - Delay Model    │
└─────────────────┘                  │ - Capability     │
                                     │ - Modbus Server  │
                                     └──────────────────┘

### Mathematical Model

The simulator uses a discrete-time state-space representation:

State Vector:

text

x(k) = [P_phys(k), Q_phys(k), V_grid(k), f_grid(k)]^T

Input Vector:

text

u(k) = [P_setpoint(k), Q_setpoint(k)]^T

Dynamics:

text

x(k+1) = A_d * x(k) + B_d * u(k) + E_scenario * w_scenario(k)

Measurement Model:

text

y(k) = H(x(k-N))  (with N-step transport delay)

Features
--------

-   Real-time Physics Simulation: 100ms time step with configurable time constants

-   Communication Latency: Configurable transport delay (default: 500ms)

-   P-Q Capability Curves: Voltage-dependent reactive power limits

-   Modbus TCP Interface: Standard industrial protocol for EMS integration

-   Interactive Console: Manual command input for testing

-   Data Logging: CSV output for analysis and validation

-   Web Visualization: HTML-based data analyzer with interactive charts

Project Structure
-----------------

text

bess-hil-simulator/
├── Program.cs                 # Main application and real-time loop
├── BessPhysicsModel.cs        # Core physics engine implementation
├── PqCapabilityCurve.cs       # P-Q capability curve logic
├── ModbusServerWrapper.cs     # Modbus TCP server implementation
├── viewer.html               # Web-based data visualization
├── PCS Simulator.pdf         # Technical documentation
└── README.md                 # This file

Configuration Parameters
------------------------

| Parameter | Value | Description |
| --- | --- | --- |
| `T_s` | 0.1 s | Simulation sampling time |
| `T_delay` | 0.5 s | Total measurement/communication latency |
| `τ_P` | 0.1 s | Active power response time constant |
| `τ_Q` | 0.2 s | Reactive power response time constant |
| `S_max` | 0.21 MVA | Inverter apparent power capacity |

Installation & Usage
--------------------

### Prerequisites

-   .NET 6.0 or later

-   Web browser (for data visualization)

### Running the Simulator

1.  Clone the repository:

    bash

    git clone https://github.com/your-username/bess-hil-simulator.git
    cd bess-hil-simulator

2.  Build and run:

    bash

    dotnet build
    dotnet run

3.  Using the simulator:

    -   The simulator starts a Modbus TCP server on port 502

    -   Enter power setpoints in the console: `P[MW] Q[MVAR]` (e.g., `1.0 0.5`)

    -   Type `exit` to stop the simulation

    -   Data is automatically logged to `BessData.csv`

### Data Visualization

1.  Run the simulator to generate `BessData.csv`

2.  Open `viewer.html` in a web browser

3.  Load the CSV file to view interactive charts and analysis

Modbus Register Map
-------------------

### Holding Registers (Write by EMS)

| Register | Address | Description | Data Type |
| --- | --- | --- | --- |
| P_setpoint | 40001-40002 | Active power reference (MW) | Float32 |
| Q_setpoint | 40003-40004 | Reactive power reference (MVAR) | Float32 |

### Input Registers (Read by EMS)

| Register | Address | Description | Data Type |
| --- | --- | --- | --- |
| P_meas | 30001-30002 | Measured active power (MW) | Float32 |
| Q_meas | 30003-30004 | Measured reactive power (MVAR) | Float32 |
| V_meas | 30005-30006 | Measured voltage (pu) | Float32 |
| f_meas | 30007-30008 | Measured frequency (Hz) | Float32 |
| I_meas | 30009-30010 | Measured current (kA) | Float32 |

Testing Scenarios
-----------------

The simulator includes built-in test scenarios:

-   Normal Operation: V = 1.0 pu

-   Voltage Sag: V = 0.95 pu (30s - 40s)

-   Voltage Swell: V = 1.05 pu (60s - 70s)

These scenarios trigger the P-Q capability curve logic, demonstrating dynamic reactive power limit adjustments.

Development
-----------

### Key Classes

-   `BessPhysicsModel`: Implements the core physics engine with delay buffers

-   `PqCapabilityCurve`: Handles voltage-dependent reactive power limits

-   `ModbusServerWrapper`: Manages Modbus TCP communication

-   `Program`: Main application with real-time simulation loop

### Extending the Simulator

-   Add new grid scenarios by modifying the scenario generator in `Program.cs`

-   Implement different capability curves by extending `PqCapabilityCurve.cs`

-   Add new measurement points by modifying the `PlantOutput` structure

License
-------

This project is licensed under the MIT License - see the LICENSE file for details.

Documentation
-------------

For detailed technical documentation, see `PCS Simulator.pdf` which includes:

-   Mathematical modeling details

-   System architecture

-   Validation results

-   Implementation specifications

Contributing
------------

1.  Fork the repository

2.  Create a feature branch

3.  Commit your changes

4.  Push to the branch

5.  Create a Pull Request

Support
-------

For issues and questions:

-   Create an issue in the GitHub repository

-   Check the technical documentation in `PCS Simulator.pdf`
