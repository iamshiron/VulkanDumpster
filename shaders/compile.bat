@echo off
REM Shader compilation script using glslc from the Vulkan SDK
REM Adjust the VULKAN_SDK path if your installation differs

set GLSLC=C:\VulkanSDK\1.4.335.0\Bin\glslc.exe

REM Check if glslc exists
if not exist "%GLSLC%" (
    echo Error: glslc not found at %GLSLC%
    echo Please update the path to match your Vulkan SDK installation
    exit /b 1
)

echo Compiling shaders...

for %%f in (*.vert) do (
    "%GLSLC%" "%%f" -o "%%f.spv"
    if errorlevel 1 (
        echo Failed to compile %%f
        exit /b 1
    )
    echo Compiled %%f
)

for %%f in (*.frag) do (
    "%GLSLC%" "%%f" -o "%%f.spv"
    if errorlevel 1 (
        echo Failed to compile %%f
        exit /b 1
    )
    echo Compiled %%f
)

echo All shaders compiled successfully!