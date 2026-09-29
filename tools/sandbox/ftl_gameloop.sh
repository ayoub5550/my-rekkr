#!/bin/sh
# Firebase Test Lab Game Loop run. Scenarios: 1 showcase + saves, 2 Arabic + Continue + E1-E4,
# 3 graphics benchmark (7 configs x E1M1/E1M7, ~6 min), 4 20-minute soak (run alone, FTL_TIMEOUT=30m).
#   tools/sandbox/ftl_gameloop.sh Builds/REKKR-0.1.0.apk [model] [version]
# Default device: Galaxy S20 FE 5G (r8q, Snapdragon 865 / Adreno 650, 1080x2400, Android 13) = closest to POCO F3.
# Needs: gcloud authenticated with a service account of the Firebase project (Spark plan: 5 physical runs/day).
set -e
APK="${1:?apk}"; MODEL="${2:-r8q}"; VER="${3:-33}"; OUT="${OUT:-Artifacts/ftl-$(date +%Y%m%d-%H%M%S)}"
mkdir -p "$OUT"
gcloud firebase test android run --type game-loop --app "$APK" \
  --device "model=$MODEL,version=$VER,locale=en,orientation=landscape" \
  --timeout ${FTL_TIMEOUT:-15m} --scenario-numbers ${SCENARIOS:-1,2,3} --results-history-name my-rekkr --format=json >"$OUT/result.json" 2>"$OUT/run.err" || true
cat "$OUT/result.json"
B=$(grep -o 'storage/browser/[^] ]*' "$OUT/run.err" | head -1 | sed 's#^storage/browser/#gs://#; s#/$##')
[ -n "$B" ] && gsutil -m cp -r "$B/$MODEL-$VER-en-landscape" "$OUT/" >/dev/null 2>&1
grep -a "\[REKKR" "$OUT"/*/logcat || true
echo "results: $OUT"
