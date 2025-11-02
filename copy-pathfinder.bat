@echo off
echo Copying Pathfinder C# files to Space Engineers...

REM Create target directory structure
mkdir "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder" 2>nul
mkdir "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar" 2>nul
mkdir "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data" 2>nul

REM Copy C# files
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\NavigationComponent.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\NavigationComponent.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\PathfinderSession.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\PathfinderSession.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\Utils.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\Utils.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\Graph.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\Graph.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\Octant.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\Octant.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\Path.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\Path.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\Edge.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\Edge.cs" >nul
copy "Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\OctreeAStarSettings.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\OctreeAStar\OctreeAStarSettings.cs" >nul

REM Copy mod metadata and thumbnail
copy "Mods\Pathfinder\metadata.mod" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\metadata.mod" >nul
copy "Mods\Pathfinder\Thumb.jpg" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Thumb.jpg" >nul

if %errorlevel% equ 0 (
    echo Pathfinder C# files copied successfully!
    echo Location: C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\Pathfinder\Data\Scripts\Pathfinder\
          echo Files copied:
          echo   - NavigationComponent.cs
          echo   - PathfinderSession.cs
          echo   - Utils.cs
          echo   - OctreeAStar\Graph.cs
          echo   - OctreeAStar\Octant.cs
          echo   - OctreeAStar\Path.cs
          echo   - OctreeAStar\Edge.cs
          echo   - OctreeAStar\OctreeAStarSettings.cs
          echo   - metadata.mod
          echo   - Thumb.jpg
          echo Completed at %DATE% %TIME%
) else (
    echo Error copying files. Check paths and permissions.
)