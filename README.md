BESS HIL Simulator
==================

A Hardware-in-the-Loop (HIL) simulator for Power Conversion System (PCS) and Battery Energy Storage System (BESS) designed to validate Energy Management System (EMS) software.

Overview
--------

This project implements a real-time simulator that models the physical dynamics of grid-connected inverters, including:

*   **First-order lag response** for active and reactive power
    
*   **Transport delay** to simulate SCADA/communication latency
    
*   **P-Q capability curves** with voltage-dependent reactive power limits
    
*   **Modbus TCP interface** for EMS communication
    

The simulator provides a realistic testing environment for EMS validation by accurately reproducing the dynamic behavior and communication delays of actual PCS/BESS systems.

Architecture
------------

### System Components

text

Plain textANTLR4BashCC#CSSCoffeeScriptCMakeDartDjangoDockerEJSErlangGitGoGraphQLGroovyHTMLJavaJavaScriptJSONJSXKotlinLaTeXLessLuaMakefileMarkdownMATLABMarkupObjective-CPerlPHPPowerShell.propertiesProtocol BuffersPythonRRubySass (Sass)Sass (Scss)SchemeSQLShellSwiftSVGTSXTypeScriptWebAssemblyYAMLXML`   ┌─────────────────┐    Modbus TCP    ┌──────────────────┐  │   EMS (DUT)     │◄────────────────►│  HIL Simulator   │  │                 │                  │                  │  │ - Control Logic │   P_ref, Q_ref   │ - Physics Engine │  │ - SCADA Display │   P_meas, Q_meas │ - Delay Model    │  └─────────────────┘                  │ - Capability     │                                       │ - Modbus Server  │                                       └──────────────────┘   `

### Mathematical Model

The simulator uses a discrete-time state-space representation:

**State Vector:**

text

Plain textANTLR4BashCC#CSSCoffeeScriptCMakeDartDjangoDockerEJSErlangGitGoGraphQLGroovyHTMLJavaJavaScriptJSONJSXKotlinLaTeXLessLuaMakefileMarkdownMATLABMarkupObjective-CPerlPHPPowerShell.propertiesProtocol BuffersPythonRRubySass (Sass)Sass (Scss)SchemeSQLShellSwiftSVGTSXTypeScriptWebAssemblyYAMLXML`   x(k) = [P_phys(k), Q_phys(k), V_grid(k), f_grid(k)]^T   `

**Input Vector:**

text

Plain textANTLR4BashCC#CSSCoffeeScriptCMakeDartDjangoDockerEJSErlangGitGoGraphQLGroovyHTMLJavaJavaScriptJSONJSXKotlinLaTeXLessLuaMakefileMarkdownMATLABMarkupObjective-CPerlPHPPowerShell.propertiesProtocol BuffersPythonRRubySass (Sass)Sass (Scss)SchemeSQLShellSwiftSVGTSXTypeScriptWebAssemblyYAMLXML`   u(k) = [P_setpoint(k), Q_setpoint(k)]^T   `

**Dynamics:**

text

Plain textANTLR4BashCC#CSSCoffeeScriptCMakeDartDjangoDockerEJSErlangGitGoGraphQLGroovyHTMLJavaJavaScriptJSONJSXKotlinLaTeXLessLuaMakefileMarkdownMATLABMarkupObjective-CPerlPHPPowerShell.propertiesProtocol BuffersPythonRRubySass (Sass)Sass (Scss)SchemeSQLShellSwiftSVGTSXTypeScriptWebAssemblyYAMLXML`   x(k+1) = A_d * x(k) + B_d * u(k) + E_scenario * w_scenario(k)   `

**Measurement Model:**

text

Plain textANTLR4BashCC#CSSCoffeeScriptCMakeDartDjangoDockerEJSErlangGitGoGraphQLGroovyHTMLJavaJavaScriptJSONJSXKotlinLaTeXLessLuaMakefileMarkdownMATLABMarkupObjective-CPerlPHPPowerShell.propertiesProtocol BuffersPythonRRubySass (Sass)Sass (Scss)SchemeSQLShellSwiftSVGTSXTypeScriptWebAssemblyYAMLXML`   y(k) = H(x(k-N))  (with N-step transport delay)   `

Features
--------

*   **Real-time Physics Simulation**: 100ms time step with configurable time constants
    
*   **Communication Latency**: Configurable transport delay (default: 500ms)
    
*   **P-Q Capability Curves**: Voltage-dependent reactive power limits
    
*   **Modbus TCP Interface**: Standard industrial protocol for EMS integration
    
*   **Interactive Console**: Manual command input for testing
    
*   **Data Logging**: CSV output for analysis and validation
    
*   **Web Visualization**: HTML-based data analyzer with interactive charts
    

Project Structure
-----------------

text

Plain textANTLR4BashCC#CSSCoffeeScriptCMakeDartDjangoDockerEJSErlangGitGoGraphQLGroovyHTMLJavaJavaScriptJSONJSXKotlinLaTeXLessLuaMakefileMarkdownMATLABMarkupObjective-CPerlPHPPowerShell.propertiesProtocol BuffersPythonRRubySass (Sass)Sass (Scss)SchemeSQLShellSwiftSVGTSXTypeScriptWebAssemblyYAMLXML`   bess-hil-simulator/  ├── Program.cs                 # Main application and real-time loop  ├── BessPhysicsModel.cs        # Core physics engine implementation  ├── PqCapabilityCurve.cs       # P-Q capability curve logic  ├── ModbusServerWrapper.cs     # Modbus TCP server implementation  ├── viewer.html               # Web-based data visualization  ├── PCS Simulator.pdf         # Technical documentation  └── README.md                 # This file   `

Configuration Parameters
------------------------

ParameterValueDescriptionT\_s0.1 sSimulation sampling timeT\_delay0.5 sTotal measurement/communication latencyτ\_P0.1 sActive power response time constantτ\_Q0.2 sReactive power response time constantS\_max0.21 MVAInverter apparent power capacity

Installation & Usage
--------------------

### Prerequisites

*   .NET 6.0 or later
    
*   Web browser (for data visualization)
    

### Running the Simulator

1.  bashgit clone https://github.com/your-username/bess-hil-simulator.gitcd bess-hil-simulator
    
2.  bashdotnet builddotnet run
    
3.  **Using the simulator:**
    
    *   The simulator starts a Modbus TCP server on port 502
        
    *   Enter power setpoints in the console: P\[MW\] Q\[MVAR\] (e.g., 1.0 0.5)
        
    *   Type exit to stop the simulation
        
    *   Data is automatically logged to BessData.csv
        

### Data Visualization

1.  Run the simulator to generate BessData.csv
    
2.  Open viewer.html in a web browser
    
3.  Load the CSV file to view interactive charts and analysis
    

Modbus Register Map
-------------------

### Holding Registers (Write by EMS)

RegisterAddressDescriptionData TypeP\_setpoint40001-40002Active power reference (MW)Float32Q\_setpoint40003-40004Reactive power reference (MVAR)Float32

### Input Registers (Read by EMS)

RegisterAddressDescriptionData TypeP\_meas30001-30002Measured active power (MW)Float32Q\_meas30003-30004Measured reactive power (MVAR)Float32V\_meas30005-30006Measured voltage (pu)Float32f\_meas30007-30008Measured frequency (Hz)Float32I\_meas30009-30010Measured current (kA)Float32

Testing Scenarios
-----------------

The simulator includes built-in test scenarios:

*   **Normal Operation**: V = 1.0 pu
    
*   **Voltage Sag**: V = 0.95 pu (30s - 40s)
    
*   **Voltage Swell**: V = 1.05 pu (60s - 70s)
    

These scenarios trigger the P-Q capability curve logic, demonstrating dynamic reactive power limit adjustments.

Development
-----------

### Key Classes

*   **BessPhysicsModel**: Implements the core physics engine with delay buffers
    
*   **PqCapabilityCurve**: Handles voltage-dependent reactive power limits
    
*   **ModbusServerWrapper**: Manages Modbus TCP communication
    
*   **Program**: Main application with real-time simulation loop
    

### Extending the Simulator

*   Add new grid scenarios by modifying the scenario generator in Program.cs
    
*   Implement different capability curves by extending PqCapabilityCurve.cs
    
*   Add new measurement points by modifying the PlantOutput structure
    

License
-------

This project is licensed under the MIT License - see the LICENSE file for details.

Documentation
-------------

For detailed technical documentation, see PCS Simulator.pdf which includes:

*   Mathematical modeling details
    
*   System architecture
    
*   Validation results
    
*   Implementation specifications
    

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

*   Create an issue in the GitHub repository
    
*   Check the technical documentation in PCS Simulator.pdf
