@echo off
echo Copying Pathfinder C# files to Space Engineers...

REM Create target directory structure
mkdir "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder" 2>nul
mkdir "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data" 2>nul

REM Copy C# files
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\FindPathAction.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\FindPathAction.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\Grid.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\Grid.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\NavigationComponent.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\NavigationComponent.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\PathfinderSession.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\PathfinderSession.cs" >nul

REM Copy mod metadata and thumbnail
copy "Mods\Pathfinder\metadata.mod" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\metadata.mod" >nul
copy "Mods\Pathfinder\Thumb.jpg" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Thumb.jpg" >nul

if %errorlevel% equ 0 (
    echo Pathfinder C# files copied successfully!
    echo Location: C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\
          echo Files copied:
          echo   - FindPathAction.cs
          echo   - Grid.cs
          echo   - NavigationComponent.cs
          echo   - PathfinderSession.cs
          echo   - metadata.mod
          echo   - Thumb.jpg
) else (
    echo Error copying files. Check paths and permissions.
)