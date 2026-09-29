#!/bin/sh
# Local REKKR Android build. The keystore and its password live OUTSIDE the repo.
#   REKKR_KEYSTORE=/path/rekkr.keystore REKKR_KEYSTORE_PASS=... REKKR_VERSION_CODE=1 tools/sandbox/build_android.sh
# Output: Builds/REKKR-<version>.apk (override with REKKR_OUT). Log: Logs/build.log
set -e
cd "$(dirname "$0")/../.."
: "${REKKR_KEYSTORE:?set REKKR_KEYSTORE}"; : "${REKKR_KEYSTORE_PASS:?set REKKR_KEYSTORE_PASS}"
export REKKR_VERSION_CODE="${REKKR_VERSION_CODE:-1}"
export REKKR_OUT="${REKKR_OUT:-Builds/REKKR-0.1.0.apk}"
UNITY="${UNITY:-tools/sandbox/run_unity.sh}"
mkdir -p Logs
timeout 5400 "$UNITY" -batchmode -nographics -quit -buildTarget Android -projectPath "$PWD" \
  -executeMethod RekkrBuild.BuildAndroid -logFile Logs/build.log
grep -E "error CS|\[RekkrBuild\] result=" Logs/build.log | tail -5
