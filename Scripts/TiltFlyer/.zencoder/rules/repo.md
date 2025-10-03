---
description: Repository Information Overview
alwaysApply: true
---

# TiltFlyer Information

## Summary
TiltFlyer is a C# script for Space Engineers game that implements a tilt-based navigation system for in-game vehicles. It uses PID controllers for alignment and navigation, allowing vehicles to hover at a specific height and follow predefined paths.

## Structure
- **TiltFlyer/**: Main project directory containing the C# script files
  - **bin/**: Compiled binaries
  - **Program.cs**: Main script file containing the program logic
  - **TiltFlyer.csproj**: Project file defining dependencies and build configuration
  - **TiltFlyer.mdk.ini**: MDK configuration for Space Engineers script deployment

## Language & Runtime
**Language**: C#
**Version**: C# 6.0 (LangVersion 6)
**Framework**: .NET Framework 4.8
**Build System**: MSBuild (Visual Studio)
**Package Manager**: NuGet

## Dependencies
**Main Dependencies**:
- **Mal.Mdk2.References** (v2.2.4): Space Engineers game references
- **Mal.Mdk2.PbAnalyzers** (v2.1.13): Programmable Block analyzers
- **Mal.Mdk2.PbPackager** (v2.1.6): Packaging tool for Space Engineers scripts

**Shared Projects**:
- **Mdk.TiltNavigation**: Custom navigation system for tilt-based movement
- **Mdk.Utils**: Utility functions for Space Engineers scripts
- **Mdk.StateMachine**: State machine implementation
- **Mdk.Navigation**: Base navigation system

## Build & Installation
```bash
# Build the project using MSBuild
MSBuild.exe TiltFlyer.sln /p:Configuration=Release /p:Platform=x64

# The script is deployed to Space Engineers using MDK
# MDK handles the deployment to the game automatically
```

## Features
- **Tilt-based Navigation**: Uses ship gyroscopes to tilt the ship in the direction of movement
- **Hover Capability**: Maintains a specified height above the ground
- **Path Following**: Can follow predefined paths (square pattern implemented)
- **State Machine**: Uses a state machine to manage different operational modes
- **PID Controllers**: Configurable PID controllers for alignment and navigation
- **Custom Configuration**: Supports custom PID values through the Programmable Block's custom data

## In-Game Usage
- **toggle**: Switches between idle (hovering) and moving states
- **stop**: Stops all operations and returns to idle state

## Configuration
The script can be configured through the Programmable Block's custom data with the following parameters:
- **KP/KI/KD**: PID values for alignment
- **NAV_KP/NAV_KI/NAV_KD**: PID values for navigation
- **MAX_SPEED_ERROR**: Maximum allowed speed error
- **ACCEL**: Acceleration rate