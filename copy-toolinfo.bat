@echo off
echo Copying ToolInfo C# files to Space Engineers...

REM Create target directory structure
mkdir "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo" 2>nul
mkdir "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data" 2>nul

REM Copy C# files
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\ToolInfoComponent.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\ToolInfoComponent.cs" >nul
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\ToolInfoSession.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\ToolInfoSession.cs" >nul
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\ToolInfoSettings.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\ToolInfoSettings.cs" >nul
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\CanUseToolEvent.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\CanUseToolEvent.cs" >nul
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\ObjectBuilderCanUseToolEvent.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\ObjectBuilderCanUseToolEvent.cs" >nul
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\EventControllerGenericBooleanEvent.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\EventControllerGenericBooleanEvent.cs" >nul
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\IEventControllerBooleanEvent.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\IEventControllerBooleanEvent.cs" >nul
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\IEventControllerEvent.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\IEventControllerEvent.cs" >nul
copy "Mods\ToolInfo\Data\Scripts\ToolInfo\DetailedInfoSync.cs" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\DetailedInfoSync.cs" >nul

REM Copy SBC files
copy "Mods\ToolInfo\Data\EntityComponents.sbc" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\EntityComponents.sbc" >nul
copy "Mods\ToolInfo\Data\EntityContainers.sbc" "C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\EntityContainers.sbc" >nul

if %errorlevel% equ 0 (
    echo ToolInfo C# files copied successfully!
    echo Location: C:\Users\mikha\AppData\Roaming\SpaceEngineers\Mods\ToolInfo\Data\Scripts\ToolInfo\
    echo Files copied:
    echo   - ToolInfoComponent.cs
    echo   - ToolInfoSession.cs
    echo   - ToolInfoSettings.cs
    echo   - CanUseToolEvent.cs
    echo   - ObjectBuilderCanUseToolEvent.cs
    echo   - EventControllerGenericBooleanEvent.cs
    echo   - IEventControllerBooleanEvent.cs
    echo   - IEventControllerEvent.cs
    echo   - DetailedInfoSync.cs
    echo   - EntityComponents.sbc
    echo   - EntityContainers.sbc
) else (
    echo Error copying files. Check paths and permissions.
)
