---
description: Repository Information Overview
alwaysApply: true
---

# Space Engineers Scripts Information

## Summary
This repository contains C# scripts for the Space Engineers game, organized into reusable mixins and specific script implementations. The codebase is designed for programmable blocks within the game, with various drone and vehicle control systems.

## Structure
- **Scripts/**: Contains individual script projects for different in-game vehicles and systems
- **Mixin/**: Shared code libraries that can be included in multiple scripts
- **.zencoder/**: Configuration for Zencoder
- **.cursor/**: Configuration files for cursor

## Language & Runtime
**Language**: C#
**Version**: C# 6.0 (LangVersion 6)
**Target Framework**: .NET Framework 4.8
**Build System**: MSBuild
**Package Manager**: NuGet

## Dependencies
**Main Dependencies**:
- Mal.Mdk2.References (v2.2.4) - Space Engineers game API references
- Mal.Mdk2.PbPackager (v2.1.5) - Packaging tool for programmable blocks
- Mal.Mdk2.PbAnalyzers (v2.1.13) - Code analyzers for Space Engineers scripts

## Build & Installation
Scripts are built using Visual Studio or MSBuild and deployed to the Space Engineers game. The MDK (Malware's Development Kit) is used to package and deploy scripts to the game.

```bash
# Build a specific script project
msbuild Scripts/DroneControlTower/DroneControlTower.sln /p:Configuration=Release
```

## Projects

### DroneControlTower
**Configuration File**: DroneControlTower.csproj
**Purpose**: Central control system for managing multiple drones, handling docking, cargo requests, and coordination.

### IceLifter
**Configuration File**: IceLifter.csproj
**Purpose**: Script for a vehicle designed to lift and transport ice resources.

### IceTractor
**Configuration File**: IceTractor.csproj
**Purpose**: Script for a vehicle designed to collect and transport ice.

### TiltFlyer
**Configuration File**: TiltFlyer.csproj
**Purpose**: Script for a flying vehicle with tilt-based navigation.

### Mdk.VMiner
**Configuration File**: Mdk.VMiner.csproj
**Purpose**: Script for an automated mining vehicle.

### Mdk.NavigationTest
**Configuration File**: Mdk.NavigationTest.csproj
**Purpose**: Test script for navigation systems.

## Shared Libraries

### Mdk.Utils
**Purpose**: Common utility functions for working with the Space Engineers API.
**Key Features**: Block finding and filtering methods.

### Mdk.Navigation
**Purpose**: Navigation system for Space Engineers vehicles.
**Key Features**: PID controllers, alignment utilities.

### Mdk.StateMachine
**Purpose**: State machine implementation for managing complex behaviors.

### Mdk.PathNavigation
**Purpose**: Path-based navigation for vehicles.

### Mdk.TiltNavigation
**Purpose**: Specialized navigation for tilt-based flying vehicles.

### Mdk.RoverNavigation
**Purpose**: Specialized navigation for ground-based vehicles.

### Mdk.CustomDataConnector
**Purpose**: Utilities for working with custom data on blocks.

### Mdk.NavigationWithCollisionAvoidance
**Purpose**: Enhanced navigation with collision detection and avoidance.