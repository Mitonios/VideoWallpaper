@echo off

dotnet build
if %ERRORLEVEL% neq 0 (
    echo Build failed.
    exit /b %ERRORLEVEL%
)

dotnet publish -c Release -r win-x64 --self-contained
if %ERRORLEVEL% neq 0 (
    echo Publish failed.
    exit /b %ERRORLEVEL%
)

echo Done.
