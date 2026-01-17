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

"%GLSLC%" colored_triangle.vert -o colored_triangle.vert.spv
if %errorlevel% neq 0 (
    echo Failed to compile colored_triangle.vert
    exit /b 1
)
echo Compiled colored_triangle.vert

"%GLSLC%" colored_triangle.frag -o colored_triangle.frag.spv
if %errorlevel% neq 0 (
    echo Failed to compile colored_triangle.frag
    exit /b 1
)
echo Compiled colored_triangle.frag

echo All shaders compiled successfully!
