@echo off
rem The Blender MCP bridge without a window (Blender 5.1.2, portable, in D:\Claude programing\tools\Blender5).
rem Open a .blend by passing it: bridge_headless.cmd path\to\file.blend
"D:\Claude programing\tools\Blender5\blender.exe" -b %1 --online-mode --command blender_mcp
