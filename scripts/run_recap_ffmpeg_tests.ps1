# Runs the MobileAPI test suite inside a Linux .NET SDK container that has the same
# Debian ffmpeg/ffprobe the MobileAPI image installs, so the real-FFmpeg recap tests
# (RecapFfmpegIntegrationTests) execute instead of being skipped. Needs Docker running.
#
# The tests are built on the host first (restore there uses the machine's trusted
# certificates) and the portable test DLLs are then run inside the container.
#
#   powershell -File scripts/run_recap_ffmpeg_tests.ps1
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repo 'src/MobileAPI/MobileAPI.Tests/MobileAPI.Tests.csproj'
$out = Join-Path ([System.IO.Path]::GetTempPath()) 'mikpunt-mobileapi-tests'

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet build $testProject -c Release -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

docker run --rm `
    -v "${out}:/tests:ro" `
    mcr.microsoft.com/dotnet/sdk:8.0 `
    bash -c "set -e; apt-get update -qq >/dev/null && apt-get install -y -qq --no-install-recommends ffmpeg >/dev/null; ffmpeg -version | head -1; dotnet test /tests/MobileAPI.Tests.dll --logger 'console;verbosity=normal'"
if ($LASTEXITCODE -ne 0) { throw "MobileAPI tests failed in the ffmpeg container (exit $LASTEXITCODE)." }
