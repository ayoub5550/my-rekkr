#!/bin/sh
# Local REKKR Android build. The keystore and its password live OUTSIDE the repo.
#   REKKR_KEYSTORE=/path/rekkr.keystore REKKR_KEYSTORE_PASS=... tools/sandbox/build_android.sh
# Output: Builds/REKKR-<version>.apk (override with REKKR_OUT). Log: Logs/build.log
set -e
cd "$(dirname "$0")/../.."
: "${REKKR_KEYSTORE:?set REKKR_KEYSTORE}"; : "${REKKR_KEYSTORE_PASS:?set REKKR_KEYSTORE_PASS}"
# Match RekkrBuild.Configure's 0.N.x -> N convention instead of silently
# shipping dev8 with dev1's output name and versionCode.
version="$(sed -n 's/^[[:space:]]*public const string Version = "\([0-9][0-9.]*\)";[[:space:]]*$/\1/p' Assets/Rekkr/Scripts/RekkrApp.cs)"
if ! printf '%s\n' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "Cannot read RekkrApp.Version; refusing to build." >&2
    exit 1
fi
default_code="$(printf '%s\n' "$version" | cut -d. -f2)"
export REKKR_VERSION_CODE="${REKKR_VERSION_CODE:-$default_code}"
if ! printf '%s\n' "$REKKR_VERSION_CODE" | grep -Eq '^[0-9]+$' || [ "$REKKR_VERSION_CODE" -le 0 ]; then
    echo "REKKR_VERSION_CODE must be a positive integer." >&2
    exit 1
fi
export REKKR_OUT="${REKKR_OUT:-Builds/REKKR-$version.apk}"
UNITY="${UNITY:-tools/sandbox/run_unity.sh}"
mkdir -p Logs
timeout 5400 "$UNITY" -batchmode -nographics -quit -buildTarget Android -projectPath "$PWD" \
  -executeMethod RekkrBuild.BuildAndroid -logFile Logs/build.log
grep -E "error CS|\[RekkrBuild\] result=" Logs/build.log | tail -5
