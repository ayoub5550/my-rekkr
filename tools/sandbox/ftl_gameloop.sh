#!/bin/sh
# Firebase Test Lab Game Loop run (autopilot: title -> menu -> settings -> New Game E1 -> ~95 s play).
#   tools/sandbox/ftl_gameloop.sh Builds/REKKR-0.1.0.apk [model] [version]
# Default device: Galaxy S20 FE 5G (r8q, Snapdragon 865 / Adreno 650, 1080x2400, Android 13) = closest to POCO F3.
# Needs: gcloud authenticated with a service account of the Firebase project (Spark plan: 5 physical runs/day).
set -e
APK="${1:?apk}"; MODEL="${2:-r8q}"; VER="${3:-33}"; OUT="${OUT:-Artifacts/ftl-$(date +%Y%m%d-%H%M%S)}"
mkdir -p "$OUT"
gcloud firebase test android run --type game-loop --app "$APK" \
  --device "model=$MODEL,version=$VER,locale=en,orientation=landscape" \
  --timeout 6m --scenario-numbers 1 --results-history-name my-rekkr --format=json >"$OUT/result.json" 2>"$OUT/run.err" || true
cat "$OUT/result.json"
B=$(grep -o 'storage/browser/[^] ]*' "$OUT/run.err" | head -1 | sed 's#^storage/browser/#gs://#; s#/$##')
[ -n "$B" ] && gsutil -m cp -r "$B/$MODEL-$VER-en-landscape" "$OUT/" >/dev/null 2>&1
grep -a "\[REKKR" "$OUT"/*/logcat || true
echo "results: $OUT"
