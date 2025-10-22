@echo off
echo Copying ProjectorMergeBlockAligner mod files...

if not exist "C:\Users\%USERNAME%\AppData\Roaming\SpaceEngineers\Mods\ProjectorMergeBlockAligner" (
    mkdir "C:\Users\%USERNAME%\AppData\Roaming\SpaceEngineers\Mods\ProjectorMergeBlockAligner"
)

if not exist "C:\Users\%USERNAME%\AppData\Roaming\SpaceEngineers\Mods\ProjectorMergeBlockAligner\Data\Scripts\ProjectorMergeBlockAligner" (
    mkdir "C:\Users\%USERNAME%\AppData\Roaming\SpaceEngineers\Mods\ProjectorMergeBlockAligner\Data\Scripts\ProjectorMergeBlockAligner"
)

copy "Mods\ProjectorMergeBlockAligner\metadata.mod" "C:\Users\%USERNAME%\AppData\Roaming\SpaceEngineers\Mods\ProjectorMergeBlockAligner\metadata.mod"
copy "Mods\ProjectorMergeBlockAligner\Thumb.jpg" "C:\Users\%USERNAME%\AppData\Roaming\SpaceEngineers\Mods\ProjectorMergeBlockAligner\Thumb.jpg"
copy "Mods\ProjectorMergeBlockAligner\Data\Scripts\ProjectorMergeBlockAligner\ProjectorMergeBlockAligner.cs" "C:\Users\%USERNAME%\AppData\Roaming\SpaceEngineers\Mods\ProjectorMergeBlockAligner\Data\Scripts\ProjectorMergeBlockAligner\ProjectorMergeBlockAligner.cs"

echo ProjectorMergeBlockAligner mod files copied successfully!
pause
